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

public sealed class KeyProtectionOptions
{
    public const string SectionName = "KeyProtection";

    public KeyProtectionProvider Provider { get; set; } = KeyProtectionProvider.None;

    public ScalewayKmsOptions Kms { get; set; } = new();
}

/// <summary>
/// Scaleway Key Manager, addressed over its documented HTTP API.
/// <para>
/// <see cref="BaseUrl"/> exists for the same reason <c>ObjectStorage:ServiceUrl</c> does:
/// production points at Scaleway and development points at the fake in
/// <c>infra/SpaceCraft.FakeScalewayKms</c>, with one implementation behind both. The client
/// is production code either way — it has no "if development" branch, so the path that runs
/// in production is the path exercised on every developer machine.
/// </para>
/// </summary>
public sealed class ScalewayKmsOptions
{
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
    public string Region { get; set; } = "fr-par";

    /// <summary>The symmetric key that wraps the ring. A UUID, in the request path.</summary>
    public string? KeyId { get; set; }

    /// <summary>
    /// Sent as <c>X-Auth-Token</c>, with no "Bearer" prefix — Scaleway's own scheme. This is
    /// the one secret that must not live in the database it protects.
    /// </summary>
    public string? SecretKey { get; set; }

    /// <summary>Regions the real API accepts, per its OpenAPI contract.</summary>
    public static readonly string[] KnownRegions = ["fr-par", "nl-ams", "pl-waw"];
}
