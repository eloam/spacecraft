namespace SpaceCraft.Domain.Projects;

/// <summary>
/// Languages SpaceCraft can emit. Values are pinned so persisted rows survive reordering.
/// </summary>
public enum SdkLanguage
{
    TypeScript = 1,
    CSharp = 2,
    Java = 3,
    Python = 4,
    Php = 5,
    Go = 6,
}

public enum RegistryKind
{
    Npm = 1,
    NuGet = 2,
    Maven = 3,
    PyPI = 4,
    Packagist = 5,
}

public static class SdkLanguageInfo
{
    /// <summary>The registry a language publishes to by default.</summary>
    public static RegistryKind DefaultRegistry(this SdkLanguage language) => language switch
    {
        SdkLanguage.TypeScript => RegistryKind.Npm,
        SdkLanguage.CSharp => RegistryKind.NuGet,
        SdkLanguage.Java => RegistryKind.Maven,
        SdkLanguage.Python => RegistryKind.PyPI,
        SdkLanguage.Php => RegistryKind.Packagist,
        // Deliberately unmapped. Go modules are fetched from their version-control tags, so
        // there is no registry to publish to — and the previous mapping said npm, which would
        // have routed a Go module to the npm publisher and uploaded it there. An unmapped
        // language throws below, which is the honest answer until Go publishing means
        // something.
        SdkLanguage.Go => throw new ArgumentOutOfRangeException(
            nameof(language), language, "Go modules are consumed from VCS tags, not published to a registry."),
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
    };

    /// <summary>The identifier Kiota expects for <c>--language</c>.</summary>
    public static string KiotaName(this SdkLanguage language) => language switch
    {
        SdkLanguage.TypeScript => "typescript",
        SdkLanguage.CSharp => "csharp",
        SdkLanguage.Java => "java",
        SdkLanguage.Python => "python",
        SdkLanguage.Php => "php",
        SdkLanguage.Go => "go",
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
    };

    /// <summary>
    /// Languages wired end-to-end today. The vertical slice ships TypeScript only;
    /// the rest are modelled but rejected at the API boundary until their builder exists.
    /// </summary>
    public static bool IsSupported(this SdkLanguage language) => language is SdkLanguage.TypeScript;
}
