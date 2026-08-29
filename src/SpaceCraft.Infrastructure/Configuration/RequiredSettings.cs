using Microsoft.Extensions.Configuration;

namespace SpaceCraft.Infrastructure.Configuration;

/// <summary>
/// <c>appsettings.json</c> marks the settings a deployment has to supply with
/// <c>[REQUIRED]</c> rather than leaving them blank, so the file says what it needs instead of
/// hiding it in a comment. This is what makes sure the marker was actually replaced.
///
/// <para>
/// Without it the placeholder is simply a string, and a perfectly valid one: it satisfies
/// <c>[Required]</c>, it is not whitespace, so it sails through every option validation and
/// surfaces much later as a rejected SMTP password or a 403 from S3 — with nothing on screen
/// connecting either back to an unset variable.
/// </para>
///
/// <para>
/// Every offending key is reported at once. Finding them one restart at a time is how a
/// five-minute deployment becomes an hour.
/// </para>
/// </summary>
public static class RequiredSettings
{
    public const string Placeholder = "[REQUIRED]";

    /// <summary>
    /// Throws if any setting still holds the placeholder, naming all of them.
    ///
    /// <para>
    /// In every environment, development included. A checkout that has no account behind it
    /// blanks these in <c>appsettings.Development.json</c> or user secrets — which is a
    /// deliberate act, unlike a marker nobody noticed.
    /// </para>
    /// </summary>
    public static void ThrowIfPlaceholdersRemain(this IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // The whole tree, not a list of known keys: a setting added to appsettings.json later
        // is covered without anyone remembering to register it here.
        var unset = configuration.AsEnumerable()
            .Where(setting => string.Equals(setting.Value, Placeholder, StringComparison.OrdinalIgnoreCase))
            .Select(setting => setting.Key)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unset.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{unset.Count} setting{(unset.Count == 1 ? "" : "s")} still hold{(unset.Count == 1 ? "s" : "")} "
            + $"{Placeholder} and must be supplied: {string.Join(", ", unset)}. "
            + "In the environment, nested keys use a double underscore — Email:Smtp:Password "
            + "is Email__Smtp__Password.");
    }
}
