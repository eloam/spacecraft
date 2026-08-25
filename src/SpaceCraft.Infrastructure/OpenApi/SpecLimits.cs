namespace SpaceCraft.Infrastructure.OpenApi;

/// <summary>
/// Hard limits applied to every uploaded document.
/// <para>
/// An OpenAPI file is untrusted input from the internet. Without ceilings, a small
/// upload can cost unbounded CPU and memory: YAML anchor expansion ("billion laughs"),
/// deeply nested schemas, or tens of thousands of operations. These bounds are checked
/// before and during parsing rather than after.
/// </para>
/// </summary>
public static class SpecLimits
{
    /// <summary>Largest accepted document. Real-world specs sit well under this.</summary>
    public const long MaxSizeInBytes = 10 * 1024 * 1024;

    /// <summary>Wall-clock ceiling for a single parse.</summary>
    public static readonly TimeSpan ParseTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Operations beyond this produce an SDK nobody can compile in reasonable time.</summary>
    public const int MaxOperations = 5_000;

    /// <summary>Schema components ceiling.</summary>
    public const int MaxSchemas = 10_000;
}
