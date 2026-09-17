using System.ComponentModel.DataAnnotations;

namespace SpaceCraft.Api.Common;

/// <summary>
/// Where this deployment is reached from outside: the front end, and the API itself.
/// </summary>
/// <remarks>
/// Every link an email carries is built from these — the password reset, the invitation, the
/// waitlist confirmation — so a wrong value does not fail, it sends people somewhere else.
/// Which is why there is no default. These were read nine times with a fallback to a domain
/// the platform had moved away from, and a deployment that forgot the key would have mailed
/// working-looking links to the old one, with nothing to say so.
/// <para>
/// Both are validated at startup: present, absolute, and https outside Development. Absent
/// is refused with the key named, which is the only honest answer to "where do I live".
/// </para>
/// </remarks>
public sealed class PublicUrlsOptions : IValidatableObject
{
    public const string SectionName = "PublicUrls";

    /// <summary>The front end, where links in emails send people.</summary>
    [Required]
    public string? App { get; set; }

    /// <summary>The API, where images an email references are served from.</summary>
    [Required]
    public string? Api { get; set; }

    // Normalised once, here, rather than TrimEnd('/') at every use — one of the five callers
    // had forgotten it, and a trailing slash in configuration produced "//api/v1/..." links.
    //
    // Methods, not properties, and that is not style. Data-annotation validation reads every
    // public property through reflection, so a computed property on a value it is about to
    // report as missing throws first, and the report never happens. The null-forgiving
    // operator asserts what ValidateOnStart established by the time anything calls these.
    public string AppRoot() => App!.TrimEnd('/');

    public string ApiRoot() => Api!.TrimEnd('/');

    /// <summary>
    /// What the annotations cannot say: a URL, not merely a string. The https rule is not here
    /// because it depends on the environment, which a class does not know; the registration
    /// adds it.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var (name, value) in new[] { (nameof(App), App), (nameof(Api), Api) })
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out _))
            {
                yield return new ValidationResult(
                    $"PublicUrls:{name} must be an absolute URL; it was '{value}'.", [name]);
            }
        }
    }
}
