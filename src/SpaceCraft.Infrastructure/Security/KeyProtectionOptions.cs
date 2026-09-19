using System.ComponentModel.DataAnnotations;

namespace SpaceCraft.Infrastructure.Security;

/// <summary>
/// How the Data Protection key ring itself is protected at rest.
/// </summary>
/// <remarks>
/// Where the ring is *stored* and how it is *protected* are separate settings in Data
/// Protection, and only the first was ever configured: the ring is persisted to Postgres —
/// so the API and the runner share it without a filesystem between them — but written in
/// clear. The keys therefore sat in the same database as the credentials they encrypt, and a
/// single dump carried both halves.
/// </remarks>
public enum KeyProtectionProvider
{
    /// <summary>
    /// The ring is written unencrypted. Refused outside Development, where it is the same
    /// kind of mistake as an unencrypted SMTP relay.
    /// </summary>
    None = 0,

    /// <summary>Wrapped by a Scaleway Key Manager symmetric key.</summary>
    ScalewayKms = 1,
}

/// <summary>
/// The provider, and the Key Manager it names. One flat section: there is one real provider,
/// and a Kms sub-object beneath it only made every key longer — KeyProtection:KeyId said
/// nothing KeyProtection:KeyId does not. The enum stays because both its values are used:
/// None is written explicitly by the e2e and integration hosts to switch the wrapper off.
/// <para>
/// <see cref="BaseUrl"/> exists for the same reason <c>ObjectStorage:ServiceUrl</c> does:
/// production points at Scaleway and development points at the fake in
/// <c>infra/SpaceCraft.FakeScalewayKms</c>, with one implementation behind both. The client
/// is production code either way — it has no "if development" branch, so the path that runs
/// in production is the path exercised on every developer machine.
/// </para>
/// </summary>
public sealed class KeyProtectionOptions : IValidatableObject
{
    public const string SectionName = "KeyProtection";

    public KeyProtectionProvider Provider { get; set; } = KeyProtectionProvider.None;

    /// <summary>
    /// Origin only, no path: <c>https://api.scaleway.com</c> in production. The route below
    /// it is fixed by Scaleway's API and is not configurable.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// A path segment in every call. The API accepts fr-par, nl-ams and pl-waw; the fake
    /// accepts whatever it is given, so a typo here surfaces in production rather than
    /// locally — which is why it is validated before the first call.
    /// </summary>
    public string? Region { get; set; }

    /// <summary>The symmetric key that wraps the ring. A UUID, in the request path.</summary>
    public string? KeyId { get; set; }

    /// <summary>
    /// Sent as <c>X-Auth-Token</c>, with no "Bearer" prefix — Scaleway's own scheme. This is
    /// the one secret that must not live in the database it protects.
    /// </summary>
    public string? SecretKey { get; set; }

    /// <summary>Regions the real API accepts, per its OpenAPI contract.</summary>
    public static readonly string[] KnownRegions = ["fr-par", "nl-ams", "pl-waw"];

    /// <summary>
    /// What the four settings must be once a Key Manager is named. None of them can carry
    /// <c>[Required]</c>: all of them blank is the legitimate state of a host running with
    /// <see cref="KeyProtectionProvider.None"/>, which the e2e and integration hosts write on
    /// purpose. The rules therefore apply only once <see cref="Provider"/> selects the KMS —
    /// the same shape as the all-or-none rule on the SMTP relay.
    /// <para>
    /// The two rules that depend on the environment — https, and a region the real API
    /// serves — are not here, because a class does not know where it is running; the
    /// registration adds them. Runs under the same ValidateDataAnnotations as an attribute
    /// would, so every failing member of this section is reported together.
    /// </para>
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Provider is not KeyProtectionProvider.ScalewayKms)
        {
            yield break;
        }

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
        {
            yield return new ValidationResult(
                $"KeyProtection:BaseUrl must be an absolute URL; it was '{BaseUrl}'.",
                [nameof(BaseUrl)]);
        }

        if (!Guid.TryParse(KeyId, out _))
        {
            yield return new ValidationResult(
                $"KeyProtection:KeyId must be the key's UUID; it was '{KeyId}'.",
                [nameof(KeyId)]);
        }

        if (string.IsNullOrWhiteSpace(SecretKey))
        {
            yield return new ValidationResult(
                "KeyProtection:SecretKey is not configured.",
                [nameof(SecretKey)]);
        }

        // Presence only; whether the region is one the real API serves is the registration's
        // rule, since the fake answers to any of them. This had a default of fr-par, which is
        // also a region the API serves — so a missing key passed the known-regions check and
        // a deployment in nl-ams would have called the wrong region with a valid-looking name.
        if (string.IsNullOrWhiteSpace(Region))
        {
            yield return new ValidationResult(
                "KeyProtection:Region is not configured.",
                [nameof(Region)]);
        }
    }
}
