using System.Text.RegularExpressions;
using Kiota.Builder;
using Kiota.Builder.Configuration;
using SpaceCraft.Domain.Projects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SpaceCraft.Pipeline.Generation;

/// <summary>
/// Emits SDK sources with Kiota.
/// <para>
/// Kiota is used rather than a hand-written emitter because it already covers every
/// language on SpaceCraft's roadmap (C#, Java, TypeScript, Python, PHP, Go) and is maintained
/// by Microsoft. The trade-off is that generated clients depend on Kiota's per-language
/// abstraction packages and follow its request-builder style rather than reading like a
/// hand-rolled SDK.
/// </para>
/// </summary>
public sealed partial class KiotaSdkGenerator(
    IOptions<PipelineOptions> options,
    ILogger<KiotaSdkGenerator> logger) : ISdkGenerator
{
    private readonly PipelineOptions _options = options.Value;

    public SdkLanguage Language => SdkLanguage.TypeScript;

    public async Task<GenerationOutcome> GenerateAsync(GenerationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var clientName = request.ClientClassName ?? DeriveClientName(request.ApiTitle, request.PackageName);
        var outputPath = Path.Combine(request.WorkspaceRoot, request.OutputDirectory);

        var configuration = new GenerationConfiguration
        {
            OpenAPIFilePath = Path.Combine(request.WorkspaceRoot, request.SpecFilePath),
            OutputPath = outputPath,
            Language = request.Language.KiotaLanguage(),
            ClientClassName = clientName,
            ClientNamespaceName = NamespaceFor(request.PackageName),
            CleanOutput = true,
            // A workspace file is Kiota's own project bookkeeping, for people re-running the
            // CLI in a directory they own. A run workspace is thrown away.
            NoWorkspace = true,
        };

        // Kiota's own diagnostics are the only account of what happened, and the run summary
        // shows them. Collected here rather than read off a pipe.
        var captured = new CapturingLogger();

        // Generation reads a local file and writes local files. It needs no egress — external
        // $refs were already rejected at validation time — so the client is given none.
        using var offline = new HttpClient(new OfflineHandler()) { Timeout = _options.GenerateTimeout };

        // The step's budget. A process could be killed; this asks the builder to stop, which
        // it honours between units of work rather than instantly.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(_options.GenerateTimeout);

        try
        {
            var builder = new KiotaBuilder(captured, configuration, offline);
            await builder.GenerateClientAsync(budget.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Kiota generation exceeded its budget of {Timeout}", _options.GenerateTimeout);
            return GenerationOutcome.Failed(
                $"Generation timed out after {_options.GenerateTimeout.TotalMinutes:0.#} minutes.",
                captured.Text);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A malformed or unsupported document arrives here, and it is the customer's
            // document — so the message has to carry Kiota's own words, not a status code.
            logger.LogWarning(ex, "Kiota generation failed");
            return GenerationOutcome.Failed($"Kiota could not generate the client. {ex.Message}", captured.Text);
        }

        return new GenerationOutcome
        {
            Succeeded = true,
            Output = captured.Text,
            FileCount = Directory.Exists(outputPath)
                ? Directory.EnumerateFiles(outputPath, "*", SearchOption.AllDirectories).Count()
                : 0,
        };
    }

    /// <summary>
    /// Refuses every request. Kiota takes an <see cref="HttpClient"/> so it can fetch a remote
    /// description; ours is always on disk, and the old process had <c>NetworkPolicy.None</c>
    /// declared on it. In-process there is no sandbox to declare it to, so the denial is here,
    /// where it is actually enforced.
    /// </summary>
    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new NotSupportedException(
                $"The generator tried to reach {request?.RequestUri}. Generation runs offline; "
                + "a specification must not pull anything over the network.");
    }

    /// <summary>
    /// Keeps Kiota's log lines so they can be shown in the run summary, the way the CLI's
    /// stdout used to be. Bounded, because a pathological document can be very talkative.
    /// </summary>
    private sealed class CapturingLogger : ILogger<KiotaBuilder>
    {
        private const int MaxLines = 500;
        private readonly System.Text.StringBuilder _text = new();
        private int _lines;

        public string Text => _text.ToString();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (!IsEnabled(logLevel) || _lines >= MaxLines)
            {
                return;
            }

            _lines++;
            _text.Append(logLevel).Append(": ").AppendLine(formatter(state, exception));
        }
    }

    /// <summary>
    /// Turns "Keycloak Admin REST API" into "KeycloakAdminRestApiClient" — a valid
    /// identifier in every target language.
    /// </summary>
    internal static string DeriveClientName(string? apiTitle, string packageName)
    {
        var source = !string.IsNullOrWhiteSpace(apiTitle) ? apiTitle : packageName;

        // Drop an npm scope before deriving an identifier from a package name.
        source = source.Contains('/') ? source[(source.LastIndexOf('/') + 1)..] : source;

        var words = NonWord().Split(source).Where(w => w.Length > 0).ToArray();
        if (words.Length == 0)
        {
            return "ApiClient";
        }

        var name = string.Concat(words.Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));

        if (char.IsDigit(name[0]))
        {
            name = "Api" + name;
        }

        return name.EndsWith("Client", StringComparison.OrdinalIgnoreCase) ? name : name + "Client";
    }

    /// <summary>
    /// "@acme/billing-sdk" becomes "acme.billing_sdk". Hyphens map to underscores rather
    /// than being dropped: no target language accepts a hyphen in an identifier, but
    /// deleting it silently welds words together ("billingsdk").
    /// <para>
    /// A Maven coordinate separates its halves with a colon, and the strip below was doing to
    /// it exactly what this method refuses to do to a hyphen: "com.acme:billing-sdk" came out
    /// as "com.acmebilling_sdk". It maps to a dot, like the npm scope separator, because that
    /// is what both halves already mean — a namespace and a name.
    /// </para>
    /// </summary>
    internal static string NamespaceFor(string packageName)
    {
        var name = packageName.TrimStart('@').Replace('/', '.').Replace(':', '.').Replace('-', '_');
        return NonNamespace().Replace(name, string.Empty).Trim('.') is { Length: > 0 } cleaned
            ? cleaned
            : "sdk";
    }

    [GeneratedRegex(@"[^A-Za-z0-9]+")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"[^A-Za-z0-9._]")]
    private static partial Regex NonNamespace();
}
