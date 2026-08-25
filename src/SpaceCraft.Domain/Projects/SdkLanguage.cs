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
        SdkLanguage.Go => RegistryKind.Npm, // Go has no central registry; proxied from VCS tags.
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
