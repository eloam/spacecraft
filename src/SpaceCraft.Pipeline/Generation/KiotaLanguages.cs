using Kiota.Builder;
using SpaceCraft.Domain.Projects;

namespace SpaceCraft.Pipeline.Generation;

/// <summary>
/// Maps SpaceCraft's languages onto Kiota's.
/// <para>
/// It lives here rather than beside <see cref="SdkLanguageInfo.KiotaName"/> in the domain
/// because <c>GenerationLanguage</c> is Kiota's type, and the domain does not know Kiota exists.
/// That was true when generation shelled out to a CLI and stays true now that it does not.
/// </para>
/// </summary>
internal static class KiotaLanguages
{
    public static GenerationLanguage KiotaLanguage(this SdkLanguage language) => language switch
    {
        SdkLanguage.TypeScript => GenerationLanguage.TypeScript,
        SdkLanguage.CSharp => GenerationLanguage.CSharp,
        SdkLanguage.Java => GenerationLanguage.Java,
        SdkLanguage.Python => GenerationLanguage.Python,
        SdkLanguage.Php => GenerationLanguage.PHP,
        SdkLanguage.Go => GenerationLanguage.Go,
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
    };

    /// <summary>
    /// The key this language has in Kiota's own settings file — its enum name, which is
    /// PascalCase ("TypeScript"), not the lower-case name the CLI took on its command line.
    /// </summary>
    public static string SettingsKey(this SdkLanguage language) =>
        language.KiotaLanguage().ToString();
}
