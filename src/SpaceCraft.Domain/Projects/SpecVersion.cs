using SpaceCraft.Domain.Common;

namespace SpaceCraft.Domain.Projects;

public enum SpecValidationStatus
{
    Pending = 1,
    Valid = 2,

    /// <summary>Parsed and usable, but the document has issues worth surfacing.</summary>
    ValidWithWarnings = 3,

    /// <summary>Could not be parsed, or violates a limit. No SDK can be generated from it.</summary>
    Invalid = 4,
}

/// <summary>
/// One immutable snapshot of an OpenAPI document. The bytes live in object storage —
/// only the metadata is in Postgres, so a 20 MB spec never bloats a row.
/// </summary>
public sealed class SpecVersion : Entity
{
    private SpecVersion() { }

    /// <summary>
    /// The storage key incorporates this entity's id, which only exists once the entity does.
    /// Taking a factory keeps the key immutable and always set, instead of writing a
    /// placeholder and patching it afterwards.
    /// </summary>
    internal SpecVersion(
        Guid apiProjectId,
        int sequence,
        Func<Guid, string> storageKeyFactory,
        string contentHash,
        long sizeInBytes,
        SpecSource source)
    {
        ArgumentNullException.ThrowIfNull(storageKeyFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeInBytes);

        ApiProjectId = apiProjectId;
        Sequence = sequence;
        ContentHash = contentHash;
        SizeInBytes = sizeInBytes;
        Source = source;

        StorageKey = storageKeyFactory(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(StorageKey);
    }

    public Guid ApiProjectId { get; private set; }

    /// <summary>Per-project monotonic counter, surfaced in the UI as "spec #N".</summary>
    public int Sequence { get; private set; }

    /// <summary>Object storage key holding the raw document.</summary>
    public string StorageKey { get; private set; } = null!;

    /// <summary>SHA-256 of the raw bytes; lets us skip a run when nothing actually changed.</summary>
    public string ContentHash { get; private set; } = null!;

    public long SizeInBytes { get; private set; }
    public SpecSource Source { get; private set; }

    public SpecValidationStatus ValidationStatus { get; private set; } = SpecValidationStatus.Pending;

    /// <summary>Diagnostics from the parser, stored as JSON.</summary>
    public string? Diagnostics { get; private set; }

    public int WarningCount { get; private set; }
    public int ErrorCount { get; private set; }

    /// <summary>info.title from the document.</summary>
    public string? ApiTitle { get; private set; }

    /// <summary>info.version from the document — the version the SDK inherits by default.</summary>
    public string? ApiVersion { get; private set; }

    /// <summary>Commit SHA that produced this spec, when it arrived via GitHub.</summary>
    public string? GitCommitSha { get; private set; }

    public void RecordGitOrigin(string commitSha)
    {
        GitCommitSha = commitSha;
        Touch();
    }

    public void MarkValidated(
        SpecValidationStatus status,
        string? apiTitle,
        string? apiVersion,
        int warningCount,
        int errorCount,
        string? diagnosticsJson)
    {
        ValidationStatus = status;
        ApiTitle = apiTitle;
        ApiVersion = apiVersion;
        WarningCount = warningCount;
        ErrorCount = errorCount;
        Diagnostics = diagnosticsJson;
        Touch();
    }

    public bool IsGeneratable =>
        ValidationStatus is SpecValidationStatus.Valid or SpecValidationStatus.ValidWithWarnings;
}
