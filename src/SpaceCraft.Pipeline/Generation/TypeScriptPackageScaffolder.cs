using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpaceCraft.Domain.Projects;

namespace SpaceCraft.Pipeline.Generation;

/// <summary>
/// Produces the npm packaging around Kiota's TypeScript output: package.json, tsconfig.json,
/// a README and an .npmignore.
/// </summary>
public sealed class TypeScriptPackageScaffolder(IKiotaDependencyProvider dependencies) : IPackageScaffolder
{
    /// <summary>
    /// TypeScript compiler used to build the package. Pinned to a caret range so patch
    /// fixes flow in but a major bump never silently changes the emitted output.
    /// </summary>
    private const string TypeScriptVersion = "^5.7.2";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public SdkLanguage Language => SdkLanguage.TypeScript;

    public async Task ScaffoldAsync(ScaffoldRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var runtime = dependencies.GetRuntimeDependencies(SdkLanguage.TypeScript);

        await File.WriteAllTextAsync(
            Path.Combine(request.SourceDirectory, "package.json"),
            BuildPackageJson(request, runtime),
            ct);

        await File.WriteAllTextAsync(
            Path.Combine(request.SourceDirectory, "tsconfig.json"),
            BuildTsConfig(),
            ct);

        await File.WriteAllTextAsync(
            Path.Combine(request.SourceDirectory, "README.md"),
            BuildReadme(request),
            ct);

        await File.WriteAllTextAsync(
            Path.Combine(request.SourceDirectory, ".npmignore"),
            string.Join('\n', ["src/", "tsconfig.json", "*.tsbuildinfo", ""]),
            ct);

        await WriteBarrelAsync(request, ct);
    }

    /// <summary>
    /// Writes the package entry point.
    /// <para>
    /// Kiota names its root file after the client (petStoreApiClient.ts) and emits no
    /// index. Without a barrel, package.json's "main" points at a file that does not exist
    /// and <c>import { createFooClient } from "@acme/foo"</c> fails for every consumer.
    /// </para>
    /// </summary>
    private static async Task WriteBarrelAsync(ScaffoldRequest request, CancellationToken ct)
    {
        var clientFile = FindClientFile(request.SourceDirectory, request.ClientClassName);

        var exports = new List<string>
        {
            // Node16 module resolution requires the .js specifier even from TypeScript.
            $"export * from \"./{clientFile}.js\";",
        };

        // Re-export the generated model types so consumers can name what the client returns.
        if (File.Exists(Path.Combine(request.SourceDirectory, "models", "index.ts")))
        {
            exports.Add("export * from \"./models/index.js\";");
        }

        await File.WriteAllTextAsync(
            Path.Combine(request.SourceDirectory, "index.ts"),
            string.Join('\n', exports) + "\n",
            ct);
    }

    /// <summary>
    /// Locates the generated root client. Matched case-insensitively against the client name
    /// rather than assumed, so a change in Kiota's file-naming convention surfaces as a build
    /// failure here instead of a broken published package.
    /// </summary>
    internal static string FindClientFile(string sourceDirectory, string clientClassName)
    {
        var match = Directory
            .EnumerateFiles(sourceDirectory, "*.ts", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .FirstOrDefault(name => string.Equals(name, clientClassName, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            return match;
        }

        throw new InvalidOperationException(
            $"No generated client file matching '{clientClassName}.ts' was found in the output. "
            + "Kiota's naming convention may have changed.");
    }

    internal static string BuildPackageJson(ScaffoldRequest request, IReadOnlyList<KiotaDependency> runtime)
    {
        var dependencyMap = new JsonObject();
        foreach (var dependency in runtime)
        {
            // Exact versions: a generated client is compiled against one specific set of
            // Kiota abstractions, and a floating range would let a consumer's install
            // resolve to an incompatible one.
            dependencyMap[dependency.Name] = dependency.Version;
        }

        var manifest = new JsonObject
        {
            ["name"] = request.PackageName,
            ["version"] = request.Version,
            ["description"] = request.ApiDescription is { Length: > 0 } d
                ? Truncate(d, 300)
                : $"Generated TypeScript SDK for {request.ApiTitle ?? request.PackageName}.",
            ["main"] = "dist/index.js",
            ["types"] = "dist/index.d.ts",
            ["type"] = "module",
            ["exports"] = new JsonObject
            {
                ["."] = new JsonObject
                {
                    ["types"] = "./dist/index.d.ts",
                    ["import"] = "./dist/index.js",
                },
            },
            ["files"] = new JsonArray("dist", "README.md"),
            ["scripts"] = new JsonObject
            {
                ["build"] = "tsc",
                ["prepublishOnly"] = "tsc",
            },
            ["dependencies"] = dependencyMap,
            ["devDependencies"] = new JsonObject
            {
                ["typescript"] = TypeScriptVersion,
            },
            // Scoped packages default to restricted; without this npm rejects the publish
            // with a paywall error that reads like an auth failure.
            ["publishConfig"] = new JsonObject
            {
                ["access"] = "public",
            },
            ["sideEffects"] = false,
            // Marks provenance so consumers can tell where the package came from.
            ["spacecraft"] = new JsonObject
            {
                ["generator"] = "kiota",
                ["generatedAt"] = DateTimeOffset.UtcNow.ToString("O"),
            },
        };

        return manifest.ToJsonString(WriteOptions) + "\n";
    }

    internal static string BuildTsConfig() =>
        new JsonObject
        {
            ["compilerOptions"] = new JsonObject
            {
                ["target"] = "ES2022",
                // Kiota emits ESM with explicit .js import specifiers, which requires
                // Node16-style resolution rather than the classic bundler mode.
                ["module"] = "Node16",
                ["moduleResolution"] = "Node16",
                ["declaration"] = true,
                ["declarationMap"] = true,
                ["sourceMap"] = true,
                ["outDir"] = "dist",
                ["rootDir"] = ".",
                ["strict"] = true,
                ["esModuleInterop"] = true,
                ["skipLibCheck"] = true,
                ["forceConsistentCasingInFileNames"] = true,
            },
            ["include"] = new JsonArray("**/*.ts"),
            ["exclude"] = new JsonArray("node_modules", "dist"),
        }.ToJsonString(WriteOptions) + "\n";

    internal static string BuildReadme(ScaffoldRequest request)
    {
        var title = request.ApiTitle ?? request.PackageName;
        var client = request.ClientClassName;

        var readme = new StringBuilder();
        readme.AppendLine(CultureInfo.InvariantCulture, $"# {request.PackageName}");
        readme.AppendLine();
        readme.AppendLine(CultureInfo.InvariantCulture, $"TypeScript SDK for **{title}**, version `{request.Version}`.");
        readme.AppendLine();

        if (request.ApiDescription is { Length: > 0 })
        {
            readme.AppendLine(Truncate(request.ApiDescription, 500));
            readme.AppendLine();
        }

        readme.AppendLine("## Installation");
        readme.AppendLine();
        readme.AppendLine("```bash");
        readme.AppendLine(CultureInfo.InvariantCulture, $"npm install {request.PackageName}");
        readme.AppendLine("```");
        readme.AppendLine();
        readme.AppendLine("## Usage");
        readme.AppendLine();
        readme.AppendLine("```typescript");
        readme.AppendLine("import { AnonymousAuthenticationProvider } from \"@microsoft/kiota-abstractions\";");
        readme.AppendLine("import { FetchRequestAdapter } from \"@microsoft/kiota-http-fetchlibrary\";");
        readme.AppendLine(CultureInfo.InvariantCulture, $"import {{ create{client} }} from \"{request.PackageName}\";");
        readme.AppendLine();
        readme.AppendLine("// Swap in a real authentication provider for APIs that require credentials.");
        readme.AppendLine("const authProvider = new AnonymousAuthenticationProvider();");
        readme.AppendLine("const adapter = new FetchRequestAdapter(authProvider);");
        readme.AppendLine();
        readme.AppendLine(CultureInfo.InvariantCulture, $"const client = create{client}(adapter);");
        readme.AppendLine("```");
        readme.AppendLine();
        readme.AppendLine("## Versioning");
        readme.AppendLine();
        readme.AppendLine("This package's version tracks the `info.version` field of the OpenAPI document");
        readme.AppendLine("it was generated from. A new API version produces a new package version.");
        readme.AppendLine();
        readme.AppendLine("---");
        readme.AppendLine();
        readme.AppendLine("_Generated automatically by [SpaceCraft](https://spacecraft.dev) from an OpenAPI document.");
        readme.AppendLine("Do not edit by hand — changes are overwritten on the next generation._");

        return readme.ToString();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max].TrimEnd() + "…";
}
