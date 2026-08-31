namespace SpaceCraft.Domain.Common;

/// <summary>
/// Fits a value to the column that stores it.
/// </summary>
/// <remarks>
/// Applied where the property is assigned rather than at each call site, because the values
/// that overflow come from places nobody bounds: a compiler's error output, a registry's
/// rejection body, an <c>info.title</c> somebody wrote. An over-long one used to reach
/// SaveChanges and throw — and in the pipeline it threw a second time inside the handler
/// trying to record the failure, faulting the whole runner invocation over a message.
/// </remarks>
public static class Text
{
    /// <summary>The mark left where text was cut, so a truncated value does not read as complete.</summary>
    private const string Ellipsis = "…";

    public static string? Clamp(string? value, int max)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);

        if (value is null || value.Length <= max)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, max - Ellipsis.Length), Ellipsis);
    }
}
