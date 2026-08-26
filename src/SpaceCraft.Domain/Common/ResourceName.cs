using System.Text.RegularExpressions;

namespace SpaceCraft.Domain.Common;

/// <summary>
/// The naming rule for organizations and projects.
/// <para>
/// Names feed the slug, and the slug is the URL. Restricting the characters that may go in
/// keeps that relationship legible: what you type is what appears in the address bar, rather
/// than a transliteration you have to guess at. "Café/Ürün 2.0" silently becoming
/// "cafe-urun-2-0" is the sort of surprise this avoids.
/// </para>
/// <para>
/// Spaces are allowed and become hyphens. Forbidding them would invalidate every ordinary
/// name — "Acme Corp", "Pet Store" — and force people to type the slug themselves, which is
/// exactly the job the slug exists to do.
/// </para>
/// </summary>
public static partial class ResourceName
{
    public const int MaxLength = 100;

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9 \-]*$")]
    private static partial Regex Allowed();

    public const string Rule =
        "Use letters, numbers, spaces and hyphens only, starting with a letter or number.";

    public static bool IsValid(string? name)
    {
        var trimmed = name?.Trim();

        return !string.IsNullOrEmpty(trimmed)
            && trimmed.Length <= MaxLength
            && Allowed().IsMatch(trimmed);
    }

    /// <summary>Throws with the rule spelled out, for the domain's own guard clauses.</summary>
    public static string Require(string? name)
    {
        var trimmed = name?.Trim();

        if (!IsValid(trimmed))
        {
            throw new ArgumentException(Rule, nameof(name));
        }

        return trimmed!;
    }
}
