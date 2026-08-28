using System.Globalization;
using System.Text.RegularExpressions;
using SpaceCraft.Domain.Projects;
using Microsoft.OpenApi;

namespace SpaceCraft.Infrastructure.OpenApi;

/// <summary>
/// The checks that decide whether a usable SDK can come out of a document, as opposed to
/// whether the document is valid OpenAPI — which the parser already answers.
///
/// The distinction runs through every rule here. A missing base URL, a method name that
/// cannot be spelled, a request body on a GET: none of them make the document invalid, and a
/// generic linter has no reason to care. They decide whether the package we publish works.
///
/// Errors are reserved for what leaves the SDK broken or unbuildable. Warnings are for an SDK
/// that works but is worse than it should be. Information is for the shape of the generated
/// code and for the metadata the package carries.
/// </summary>
public static partial class StrictSdkPolicy
{
    private static readonly HashSet<string> BodylessStatus = ["204", "205", "304"];

    /// <summary>npm's ceiling, and the lowest of the registries SpaceCraft publishes to.</summary>
    private const int PackageNameLimit = 214;

    /// <summary>Formats a generator maps to a real type. Anything else falls back to string.</summary>
    private static readonly HashSet<string> KnownFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "int32", "int64", "float", "double", "decimal", "byte", "binary", "date", "date-time",
        "duration", "time", "password", "email", "idn-email", "uuid", "uri", "uri-reference",
        "uri-template", "hostname", "idn-hostname", "ipv4", "ipv6", "regex", "json-pointer",
        "relative-json-pointer", "int8", "int16", "uint8", "char",
    };

    public static IEnumerable<SpecDiagnostic> Apply(OpenApiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var operations = Operations(document).ToList();

        foreach (var finding in BaseUrl(document)) yield return finding;
        foreach (var finding in Naming(document, operations)) yield return finding;
        foreach (var finding in Requests(operations)) yield return finding;
        foreach (var finding in Responses(operations)) yield return finding;
        foreach (var finding in Schemas(document)) yield return finding;
        foreach (var finding in Security(document, operations)) yield return finding;
        foreach (var finding in Metadata(document)) yield return finding;
    }

    /// <summary>
    /// The one check that needs more than the document: how its version stands against the
    /// one before it.
    ///
    /// Registries are append-only. Publishing 1.0.0 twice is impossible, and publishing 0.9.0
    /// after 1.0.0 leaves consumers on a version their tooling will never offer them again.
    /// Both fail at the very last step of a pipeline, minutes after a build — which is the
    /// worst place to learn it.
    /// </summary>
    public static IEnumerable<SpecDiagnostic> AgainstPrevious(
        OpenApiDocument document,
        string? previousApiVersion)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(previousApiVersion)) yield break;

        var declared = document.Info?.Version;
        if (string.IsNullOrWhiteSpace(declared)) yield break;

        // Null means one of the two carries no version a registry would take, which the
        // metadata rules already report on their own.
        if (SemanticVersion.Compare(declared, previousApiVersion) is not { } order) yield break;

        if (order == 0)
        {
            yield return Error(
                $"info.version is still '{declared}', the version the previous document "
                + "declared. A registry will not take the same version twice.",
                "#/info/version");
        }
        else if (order < 0)
        {
            yield return Error(
                $"info.version '{declared}' comes before '{previousApiVersion}', which this "
                + "project already published from. Releases only move forward.",
                "#/info/version");
        }
    }

    // --- base url --------------------------------------------------------------------------

    /// <summary>
    /// Without an absolute server URL the generated client has nowhere to send anything. Every
    /// caller has to supply the host by hand, which is the one thing an SDK exists to spare
    /// them — so this is an error, not a remark.
    /// </summary>
    private static IEnumerable<SpecDiagnostic> BaseUrl(OpenApiDocument document)
    {
        var servers = document.Servers ?? [];

        if (servers.Count == 0)
        {
            yield return Error("The document declares no servers, so the generated client has no base URL.", "servers");
            yield break;
        }

        if (servers.Count > 1)
        {
            yield return Information(
                $"{servers.Count} servers are declared; the generated client is built against the first, '{servers[0].Url}'.",
                Pointer("servers"));
        }

        for (var i = 0; i < servers.Count; i++)
        {
            var url = servers[i].Url;
            var pointer = Pointer("servers", i.ToString(CultureInfo.InvariantCulture));

            // A server with no url at all is the parser's ServerRequiredFields to report, not
            // ours. Skipping it here keeps one problem to one finding.
            if (string.IsNullOrWhiteSpace(url)) continue;

            // A variable with no default cannot be resolved at generation time, which leaves
            // the client with a URL it still cannot use.
            foreach (Match match in ServerVariablePattern().Matches(url))
            {
                var name = match.Groups[1].Value;
                if (servers[i].Variables?.TryGetValue(name, out var variable) is true
                    && !string.IsNullOrWhiteSpace(variable.Default))
                {
                    continue;
                }

                yield return Error(
                    $"Server variable '{name}' has no default, so '{url}' cannot be resolved into a base URL.",
                    pointer);
            }

            // The scheme has to be checked, not just absoluteness: "/" parses as an absolute
            // file URI, so TryCreate alone calls it valid and lets a hostless server through.
            var callable = Uri.TryCreate(url, UriKind.Absolute, out var parsed)
                && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);

            if (!callable && !url.Contains('{'))
            {
                yield return Error(
                    $"Server url '{url}' is not an absolute http(s) URL, so the generated client has no host to call.",
                    pointer);
            }
        }
    }

    // --- naming ----------------------------------------------------------------------------

    /// <summary>
    /// Everything that decides what the SDK's public surface is called. These are errors
    /// because the name is the contract: a caller writes it into their code.
    /// </summary>
    private static IEnumerable<SpecDiagnostic> Naming(
        OpenApiDocument document,
        IReadOnlyList<OperationRef> operations)
    {
        // Nothing about operationId is checked. Against Kiota 1.34.1, an operationId of
        // "/identity/confirm-email" produces Identity/ConfirmEmail/…RequestBuilder and a
        // post() method, exactly as the path alone would; two operationIds that normalise to
        // one name produce get() and post() on one builder and never collide. Kiota names
        // from paths and verbs, so a mangled or duplicated operationId costs nothing.

        var schemaNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in document.Components?.Schemas?.Keys ?? [])
        {
            var normalised = Normalise(name);
            if (normalised.Length == 0) continue;

            if (schemaNames.TryGetValue(normalised, out var other) && other != name)
            {
                // A warning, and deliberately conservative. Kiota's own normalisation is
                // narrower than this one — it strips "-" but keeps "_", so user-profile and
                // user_profile really do stay two types. This flags names close enough to
                // collide in some generator without claiming they collide in yours.
                yield return Warning(
                    $"Schemas '{name}' and '{other}' differ only by punctuation or case, which some generators collapse into one type.",
                    Pointer("components", "schemas", name));
            }
            else
            {
                schemaNames[normalised] = name;
            }
        }
    }

    // --- requests --------------------------------------------------------------------------

    private static IEnumerable<SpecDiagnostic> Requests(IReadOnlyList<OperationRef> operations)
    {
        foreach (var op in operations)
        {
            if (op.Operation.RequestBody is not null)
            {
                // Not a style preference: the Fetch standard makes a GET with a body a
                // TypeError, so the generated TypeScript client cannot send this at all.
                if (op.Method == HttpMethod.Get)
                {
                    // Kiota 1.34.1 says so itself: "A GET operation with a body was found.
                    // The request body will be ignored." The generated method has no body
                    // parameter at all, so the call can never carry what the contract asks for.
                    yield return Error(
                        "A GET with a request body: the generator drops the body, so the call can never send it.",
                        op.Pointer);
                }
                else if (op.Method == HttpMethod.Delete)
                {
                    yield return Warning(
                        "A DELETE with a request body is legal but widely dropped in transit; the generated client may send nothing.",
                        op.Pointer);
                }
            }

            var declared = PathParameterPattern().Matches(op.Path).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            var supplied = op.Parameters
                .Where(p => p.In == ParameterLocation.Path && p.Name is not null)
                .Select(p => p.Name!)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var missing in declared.Where(d => !supplied.Contains(d)))
            {
                yield return Error(
                    $"The path uses {{{missing}}} but no parameter declares it, so the generated method cannot fill the URL.",
                    op.Pointer);
            }

            foreach (var parameter in op.Parameters.Where(p => p.Schema is null && (p.Content?.Count ?? 0) == 0))
            {
                yield return Warning(
                    $"Parameter '{parameter.Name}' has neither a schema nor content, so it is generated as an untyped string.",
                    op.Pointer);
            }

            foreach (var parameter in op.Parameters.Where(p => p.In == ParameterLocation.Cookie))
            {
                yield return Warning(
                    $"Parameter '{parameter.Name}' is sent in a cookie, which generated clients support poorly or not at all.",
                    op.Pointer);
            }

            // Two distinct names that reduce to one argument. Not exact duplicates: the
            // parser collapses a parameter declared twice before this ever sees it, so such a
            // rule could only ever fire on a false positive.
            var argumentNames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var parameter in op.Parameters)
            {
                if (parameter.Name is null) continue;

                var normalised = Normalise(parameter.Name);
                if (normalised.Length == 0) continue;

                var key = $"{parameter.In}:{normalised}";

                if (argumentNames.TryGetValue(key, out var other))
                {
                    yield return Warning(
                        $"Parameters '{parameter.Name}' and '{other}' differ only by punctuation "
                        + "or case; languages that normalise argument names would collapse them into one.",
                        op.Pointer);
                }
                else
                {
                    argumentNames[key] = parameter.Name;
                }
            }
        }
    }

    // --- responses -------------------------------------------------------------------------

    private static IEnumerable<SpecDiagnostic> Responses(IReadOnlyList<OperationRef> operations)
    {
        foreach (var op in operations)
        {
            var responses = op.Operation.Responses;

            if (responses is null || responses.Count == 0
                || !responses.Keys.Any(k => k.StartsWith('2') || k == "default"))
            {
                yield return Warning(
                    "No successful response is described, so the generated method returns a raw ArrayBuffer.",
                    op.Pointer);
                continue;
            }

            var successShapes = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (status, response) in responses)
            {
                if (!status.StartsWith('2')) continue;

                var content = response?.Content;

                if ((content?.Count ?? 0) == 0)
                {
                    if (!BodylessStatus.Contains(status))
                    {
                        yield return Warning(
                            $"Response {status} describes no content, so the generated method returns a raw ArrayBuffer the caller has to parse.",
                            op.Pointer);
                    }
                    continue;
                }

                if (!content!.Keys.Any(IsJson))
                {
                    yield return Warning(
                        $"Response {status} offers no JSON media type, so the generated client needs serialisation it does not have.",
                        op.Pointer);
                }

                foreach (var (mediaType, media) in content.Where(e => IsJson(e.Key)))
                {
                    if (media?.Schema?.Format == "binary")
                    {
                        yield return Warning(
                            $"Response {status} declares format 'binary' under '{mediaType}'; binary is a byte stream, which a JSON body is not, and the generated method takes an ArrayBuffer where the contract documents JSON.",
                            op.Pointer);
                    }
                }

                foreach (var wildcard in content.Keys.Where(t => t.Contains('*', StringComparison.Ordinal)))
                {
                    yield return Warning(
                        $"Response {status} is typed '{wildcard}', which leaves the generated client no serialiser to pick.",
                        op.Pointer);
                }

                foreach (var media in content.Values)
                {
                    successShapes.Add(Describe(media?.Schema));
                }
            }

            if (successShapes.Count > 1)
            {
                yield return Warning(
                    "Several successful responses carry different schemas, so the generated method has an ambiguous return type.",
                    op.Pointer);
            }

            var requestContent = op.Operation.RequestBody?.Content;

            foreach (var (mediaType, media) in requestContent?.Where(e => IsJson(e.Key)) ?? [])
            {
                if (media?.Schema?.Format == "binary")
                {
                    yield return Warning(
                        $"The request body declares format 'binary' under '{mediaType}'; binary is a byte stream, which a JSON body is not, and the generated method takes an ArrayBuffer where the contract documents JSON.",
                        op.Pointer);
                }
            }

            if (requestContent?.Count > 0 && !requestContent.Keys.Any(IsJson))
            {
                yield return Warning(
                    "The request body offers no JSON media type, so the generated client needs serialisation it does not have.",
                    op.Pointer);
            }

            foreach (var media in requestContent?.Values ?? [])
            {
                if (media?.Schema is not null && !IsReference(media.Schema))
                {
                    yield return Information(
                        "The request body uses an inline schema, so the generator invents a model name that can change between runs.",
                        op.Pointer);
                    break;
                }
            }

            foreach (var response in responses.Values)
            {
                var inline = (response?.Content?.Values ?? []).Any(m => m?.Schema is not null && !IsReference(m.Schema));
                if (inline)
                {
                    yield return Information(
                        "A response uses an inline schema, so the generator invents a model name that can change between runs.",
                        op.Pointer);
                    break;
                }
            }

            if ((op.Operation.Tags?.Count ?? 0) == 0)
            {
                yield return Information(
                    "This operation has no tags, so it lands in a flat namespace instead of being grouped with its neighbours.",
                    op.Pointer);
            }

            if (string.IsNullOrWhiteSpace(op.Operation.Summary)
                && string.IsNullOrWhiteSpace(op.Operation.Description))
            {
                yield return Information(
                    "Neither summary nor description, so this method ships with no documentation at all.",
                    op.Pointer);
            }

            if (op.Operation.Deprecated)
            {
                yield return Information(
                    "This operation is deprecated; the generated SDK will carry it as such.",
                    op.Pointer);
            }
        }
    }

    // --- schemas ---------------------------------------------------------------------------

    private static IEnumerable<SpecDiagnostic> Schemas(OpenApiDocument document)
    {
        var schemas = document.Components?.Schemas;
        if (schemas is null) yield break;

        foreach (var (name, schema) in schemas)
        {
            var pointer = Pointer("components", "schemas", name);

            foreach (var finding in InspectSchema(schema, pointer, depth: 0))
            {
                yield return finding;
            }
        }

        foreach (var name in UnreferencedSchemas(document, schemas))
        {
            yield return Information(
                $"Schema '{name}' is never referenced, so it becomes dead code in the published package.",
                Pointer("components", "schemas", name));
        }
    }

    /// <summary>
    /// Walks a schema and its sub-schemas. Bounded: a document can nest deeply, and a cyclic
    /// $ref would otherwise walk forever — following references is not needed here anyway,
    /// since every named schema is inspected on its own.
    /// </summary>
    private static IEnumerable<SpecDiagnostic> InspectSchema(IOpenApiSchema? schema, string pointer, int depth)
    {
        if (schema is null || depth > 12 || IsReference(schema)) yield break;

        if (schema.Type == JsonSchemaType.Array && schema.Items is null)
        {
            yield return Warning(
                "This array declares no items, so the generated type is an untyped node rather than an array.",
                pointer);
        }

        if (schema.Type == JsonSchemaType.Object
            && (schema.Properties?.Count ?? 0) == 0
            && schema.AdditionalProperties is null
            && (schema.AllOf?.Count ?? 0) == 0
            && (schema.OneOf?.Count ?? 0) == 0
            && (schema.AnyOf?.Count ?? 0) == 0)
        {
            yield return Warning(
                "This object declares neither properties nor additionalProperties, so it generates an opaque model.",
                pointer);
        }

        if (((schema.OneOf?.Count ?? 0) > 0 || (schema.AnyOf?.Count ?? 0) > 0) && schema.Discriminator is null)
        {
            yield return Warning(
                "A oneOf/anyOf without a discriminator leaves the generator unable to tell the variants apart at runtime.",
                pointer);
        }

        // The same collision the schema names and operationIds are checked for, one level in:
        // two properties that become one field, and one of them silently wins.
        var fieldNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in schema.Properties?.Keys ?? new Dictionary<string, IOpenApiSchema>().Keys)
        {
            var normalised = Normalise(property);
            if (normalised.Length == 0) continue;

            if (fieldNames.TryGetValue(normalised, out var other))
            {
                // Generated against Kiota 1.34.1: TypeScript keeps both keys verbatim, but C#
                // emits "public string? UserId" twice for user_id and userId, which does not
                // compile. A warning because it depends on the target language.
                yield return Warning(
                    $"Properties '{property}' and '{other}' differ only by punctuation or case; languages that normalise field names, such as C#, would emit one member twice.",
                    pointer);
            }
            else
            {
                fieldNames[normalised] = property;
            }
        }

        foreach (var required in schema.Required ?? new HashSet<string>(StringComparer.Ordinal))
        {
            if (schema.Properties?.ContainsKey(required) is not true && (schema.AllOf?.Count ?? 0) == 0)
            {
                // Kiota omits it without a word, so the model simply has no way to carry a
                // field the contract calls required — bad, but not broken.
                yield return Warning(
                    $"'{required}' is required but is not among this schema's properties, so the generated model has no field for it.",
                    pointer);
            }
        }

        if (!string.IsNullOrWhiteSpace(schema.Format) && !KnownFormats.Contains(schema.Format))
        {
            yield return Warning(
                $"Format '{schema.Format}' is not one a generator maps to a type, so the field falls back to a string.",
                pointer);
        }

        foreach (var finding in EnumFindings(schema, pointer)) yield return finding;

        foreach (var (name, property) in schema.Properties ?? new Dictionary<string, IOpenApiSchema>())
        {
            foreach (var finding in InspectSchema(property, Pointer(pointer, "properties", name), depth + 1))
            {
                yield return finding;
            }
        }

        foreach (var finding in InspectSchema(schema.Items, Pointer(pointer, "items"), depth + 1))
        {
            yield return finding;
        }
    }

    private static IEnumerable<SpecDiagnostic> EnumFindings(IOpenApiSchema schema, string pointer)
    {
        var values = schema.Enum;
        if (values is null || values.Count == 0) yield break;

        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in values)
        {
            var text = value?.ToString();

            if (string.IsNullOrWhiteSpace(text))
            {
                yield return Warning(
                    "An enum value is empty, which cannot become a member name in the generated type.",
                    pointer);
                continue;
            }

            var normalised = Normalise(text);
            if (normalised.Length == 0)
            {
                yield return Warning(
                    $"Enum value '{text}' has nothing left to build a member name from.",
                    pointer);
            }
            else if (!names.Add(normalised))
            {
                yield return Warning(
                    $"Enum value '{text}' collides with another value once normalised into a member name.",
                    pointer);
            }
        }
    }

    private static IEnumerable<string> UnreferencedSchemas(
        OpenApiDocument document,
        IDictionary<string, IOpenApiSchema> schemas)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);

        foreach (var op in Operations(document))
        {
            foreach (var parameter in op.Parameters) Follow(parameter.Schema, reached, schemas, 0);
            foreach (var media in op.Operation.RequestBody?.Content?.Values ?? [])
                Follow(media?.Schema, reached, schemas, 0);
            foreach (var response in op.Operation.Responses?.Values ?? Array.Empty<IOpenApiResponse>().AsEnumerable())
                foreach (var media in response?.Content?.Values ?? [])
                    Follow(media?.Schema, reached, schemas, 0);
        }

        return schemas.Keys.Where(name => !reached.Contains(name));
    }

    /// <summary>Marks a schema and everything it reaches, so only truly orphaned names remain.</summary>
    private static void Follow(
        IOpenApiSchema? schema,
        HashSet<string> reached,
        IDictionary<string, IOpenApiSchema> schemas,
        int depth)
    {
        if (schema is null || depth > 24) return;

        if (schema is OpenApiSchemaReference reference)
        {
            var name = reference.Reference?.Id;
            // Already marked means already followed — this is also what stops a $ref cycle.
            if (name is null || !reached.Add(name)) return;
            if (schemas.TryGetValue(name, out var target)) Follow(target, reached, schemas, depth + 1);
            return;
        }

        foreach (var property in schema.Properties?.Values ?? []) Follow(property, reached, schemas, depth + 1);
        foreach (var member in schema.AllOf ?? []) Follow(member, reached, schemas, depth + 1);
        foreach (var member in schema.OneOf ?? []) Follow(member, reached, schemas, depth + 1);
        foreach (var member in schema.AnyOf ?? []) Follow(member, reached, schemas, depth + 1);
        Follow(schema.Items, reached, schemas, depth + 1);
        Follow(schema.AdditionalProperties, reached, schemas, depth + 1);
    }

    // --- security --------------------------------------------------------------------------

    private static IEnumerable<SpecDiagnostic> Security(
        OpenApiDocument document,
        IReadOnlyList<OperationRef> operations)
    {
        var defined = document.Components?.SecuritySchemes?.Keys.ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);
        var reported = new HashSet<string>(StringComparer.Ordinal);

        IEnumerable<(string Scheme, string Pointer)> Referenced()
        {
            foreach (var requirement in document.Security ?? [])
                foreach (var scheme in requirement.Keys)
                    yield return (SchemeName(scheme), "#/security");

            foreach (var op in operations)
                foreach (var requirement in op.Operation.Security ?? [])
                    foreach (var scheme in requirement.Keys)
                        yield return (SchemeName(scheme), op.Pointer);
        }

        foreach (var (scheme, pointer) in Referenced())
        {
            if (scheme.Length == 0 || defined.Contains(scheme) || !reported.Add(scheme)) continue;

            yield return Error(
                $"Security scheme '{scheme}' is required but is not defined in components.securitySchemes.",
                pointer);
        }

        foreach (var (name, scheme) in document.Components?.SecuritySchemes
            ?? new Dictionary<string, IOpenApiSecurityScheme>())
        {
            // Both need discovery or a certificate the generator has no way to obtain, so the
            // client it emits cannot authenticate without hand-written code around it.
            if (scheme.Type is SecuritySchemeType.OpenIdConnect or SecuritySchemeType.MutualTLS)
            {
                yield return Warning(
                    $"Security scheme '{name}' is {scheme.Type}, which the generated client cannot wire on its own.",
                    Pointer("components", "securitySchemes", name));
            }
        }

        var hasGlobal = (document.Security?.Count ?? 0) > 0;
        var unsecured = operations.Where(o => !hasGlobal && (o.Operation.Security?.Count ?? 0) == 0).ToList();

        // Reported once rather than per operation: on a document that declares no security at
        // all this is one fact about the API, and repeating it several hundred times would
        // bury everything else.
        if (unsecured.Count == operations.Count && operations.Count > 0)
        {
            yield return Warning(
                "No operation declares any security, so the generated SDK offers no way to authenticate.",
                "#/security");
        }
    }

    // --- metadata --------------------------------------------------------------------------

    private static IEnumerable<SpecDiagnostic> Metadata(OpenApiDocument document)
    {
        var info = document.Info;

        if (string.IsNullOrWhiteSpace(info?.Description))
        {
            yield return Information(
                "info.description is empty; it becomes the published package's description.",
                "#/info");
        }

        if (info?.License is null)
        {
            yield return Information(
                "info.license is not set; a package published without a licence is one people cannot safely adopt.",
                "#/info");
        }

        if (info?.Contact is null)
        {
            yield return Information(
                "info.contact is not set, so the published package names nobody to report problems to.",
                "#/info");
        }

        // The title becomes the package name. One that reduces to nothing, or that no registry
        // would accept, is a publish that fails at the very last step.
        var title = info?.Title;
        if (!string.IsNullOrWhiteSpace(title))
        {
            if (Normalise(title).Length == 0)
            {
                yield return Error(
                    $"info.title '{title}' has nothing left to build a package name from.",
                    "#/info/title");
            }
            else if (title.Length > PackageNameLimit)
            {
                yield return Warning(
                    $"info.title is {title.Length} characters; registries cap package names at {PackageNameLimit}.",
                    "#/info/title");
            }
        }

        var declaredTags = (document.Tags ?? new HashSet<OpenApiTag>())
            .Select(t => t.Name)
            .Where(n => n is not null)
            .Select(n => n!)
            .ToHashSet(StringComparer.Ordinal);

        var usedTags = Operations(document)
            .SelectMany(o => o.Operation.Tags?.Select(t => t.Name) ?? [])
            .Where(t => t is not null)
            .Select(t => t!)
            .Distinct(StringComparer.Ordinal);

        foreach (var tag in usedTags.Where(t => !declaredTags.Contains(t)))
        {
            yield return Information(
                $"Tag '{tag}' groups operations but is never declared, so the SDK has no description to give it.",
                "#/tags");
        }

        var declared = info?.Version;
        if (string.IsNullOrWhiteSpace(declared)) yield break;

        var normalised = SemanticVersion.TryNormalize(declared);

        if (normalised is null)
        {
            yield return Error(
                $"info.version '{declared}' cannot be read as major.minor.patch, which registries require.",
                "#/info/version");
        }
        else if (!string.Equals(normalised, declared, StringComparison.Ordinal))
        {
            yield return Information(
                $"info.version '{declared}' will be published as '{normalised}'.",
                "#/info/version");
        }
    }

    // --- plumbing --------------------------------------------------------------------------

    private sealed record OperationRef(
        string Path,
        HttpMethod Method,
        OpenApiOperation Operation,
        IReadOnlyList<IOpenApiParameter> Parameters,
        string Pointer)
    {
        public string Label => $"{Method.Method.ToUpperInvariant()} {Path}";
    }

    private static IEnumerable<OperationRef> Operations(OpenApiDocument document)
    {
        foreach (var (path, item) in document.Paths ?? [])
        {
            foreach (var (method, operation) in item.Operations ?? [])
            {
                // Deduplicated by location and name rather than concatenated. Microsoft.OpenApi
                // merges an operation's parameters up into its path item — a path item here
                // reports 13 where the operation declares 11 — so adding both lists counted
                // every operation parameter twice and reported each as a duplicate.
                var parameters = new List<IOpenApiParameter>();
                var seen = new HashSet<string>(StringComparer.Ordinal);

                foreach (var parameter in (operation.Parameters ?? []).Concat(item.Parameters ?? []))
                {
                    if (seen.Add($"{parameter.In}:{parameter.Name}"))
                    {
                        parameters.Add(parameter);
                    }
                }

                yield return new OperationRef(
                    path,
                    method,
                    operation,
                    parameters,
                    Pointer("paths", path, method.Method.ToLowerInvariant()));
            }
        }
    }

    private static bool IsReference(IOpenApiSchema schema) => schema is OpenApiSchemaReference;

    private static bool IsJson(string mediaType) =>
        mediaType.Contains("json", StringComparison.OrdinalIgnoreCase);

    private static string Describe(IOpenApiSchema? schema) => schema switch
    {
        null => "(none)",
        OpenApiSchemaReference reference => reference.Reference?.ReferenceV3 ?? "(ref)",
        _ => $"{schema.Type}:{schema.Format}:{string.Join(',', schema.Properties?.Keys ?? [])}",
    };

    private static string SchemeName(object scheme) => scheme switch
    {
        OpenApiSecuritySchemeReference reference => reference.Reference?.Id ?? string.Empty,
        IOpenApiSecurityScheme named => named.Scheme ?? string.Empty,
        _ => scheme?.ToString() ?? string.Empty,
    };

    /// <summary>
    /// Reduces a name to what a generator would keep of it. Two names that reduce to the same
    /// string produce the same member, whatever the generator's own casing convention is.
    /// </summary>
    private static string Normalise(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string Pointer(params string[] segments) =>
        (segments[0].StartsWith('#') ? segments[0] : "#/" + Escape(segments[0]))
        + string.Concat(segments.Skip(1).Select(s => "/" + Escape(s)));

    private static string Escape(string segment) => segment.Replace("~", "~0").Replace("/", "~1");

    private static SpecDiagnostic Error(string message, string pointer) =>
        new(DiagnosticSeverity.Error, message, pointer);

    private static SpecDiagnostic Warning(string message, string pointer) =>
        new(DiagnosticSeverity.Warning, message, pointer);

    private static SpecDiagnostic Information(string message, string pointer) =>
        new(DiagnosticSeverity.Information, message, pointer);

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex(@"\{([^}]+)\}")]
    private static partial Regex PathParameterPattern();

    [GeneratedRegex(@"\{([^}]+)\}")]
    private static partial Regex ServerVariablePattern();
}
