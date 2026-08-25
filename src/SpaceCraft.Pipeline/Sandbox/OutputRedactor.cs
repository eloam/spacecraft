using System.Text.RegularExpressions;

namespace SpaceCraft.Pipeline.Sandbox;

/// <summary>
/// Strips secrets from captured output before it is persisted.
/// <para>
/// Build logs are stored and shown in the UI. Registry CLIs are not careful — npm will
/// happily echo a token in an error body — so redaction happens on the way out of the
/// sandbox, not at display time.
/// </para>
/// </summary>
public static partial class OutputRedactor
{
    public const string Mask = "***REDACTED***";

    // Belt-and-braces patterns for tokens we were never told about explicitly.
    [GeneratedRegex(@"npm_[A-Za-z0-9]{36}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NpmToken();

    [GeneratedRegex(@"gh[pousr]_[A-Za-z0-9]{36,}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex GitHubToken();

    // Masks to end of line, not just the next token: "Authorization: Bearer <token>" would
    // otherwise only lose the word "Bearer" and leak the token itself.
    [GeneratedRegex(@"(?i)\b(authorization|_auth|_authToken|api[-_]?key|password|secret)\b\s*[:=]\s*[^\r\n]+",
        RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex KeyValueSecret();

    public static string Redact(string output, IReadOnlyList<string> knownSecrets)
    {
        if (string.IsNullOrEmpty(output))
        {
            return output;
        }

        // Exact known values first — these are the ones we are certain about.
        foreach (var secret in knownSecrets.Where(s => !string.IsNullOrWhiteSpace(s) && s.Length >= 8))
        {
            output = output.Replace(secret, Mask, StringComparison.Ordinal);
        }

        try
        {
            output = NpmToken().Replace(output, Mask);
            output = GitHubToken().Replace(output, Mask);
            output = KeyValueSecret().Replace(output, m => $"{m.Groups[1].Value}={Mask}");
        }
        catch (RegexMatchTimeoutException)
        {
            // Pathological output: fail closed rather than persist something unscanned.
            return "[output withheld: redaction timed out]";
        }

        return output;
    }
}
