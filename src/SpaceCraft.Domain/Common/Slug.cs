using System.Text;
using System.Text.RegularExpressions;

namespace SpaceCraft.Domain.Common;

/// <summary>URL-safe identifiers for organizations and projects.</summary>
public static partial class Slug
{
    public const int MaxLength = 48;

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex Valid();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();

    public static bool IsValid(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= MaxLength && Valid().IsMatch(value);

    public static string From(string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);

        // Strip diacritics so "Café API" becomes "cafe-api" rather than losing the word.
        var normalized = input.Normalize(NormalizationForm.FormD);
        var stripped = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                stripped.Append(c);
            }
        }

        var slug = NonAlphanumeric()
            .Replace(stripped.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant(), "-")
            .Trim('-');

        if (slug.Length > MaxLength)
        {
            slug = slug[..MaxLength].TrimEnd('-');
        }

        return slug.Length == 0
            ? throw new ArgumentException($"'{input}' contains no slug-able characters.", nameof(input))
            : slug;
    }
}
