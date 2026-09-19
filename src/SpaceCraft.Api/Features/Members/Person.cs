using SpaceCraft.Api.Common;

namespace SpaceCraft.Api.Features.Members;

/// <summary>Who somebody is: the name they gave, if any, and the address behind it.</summary>
internal sealed record Person(string? Name, string Email)
{
    /// <summary>The name to put in a title, which is also an email's subject line.</summary>
    public string Shortly => string.IsNullOrWhiteSpace(Name) ? Email : Name;

    /// <summary>
    /// Both, for the body.
    /// </summary>
    /// <remarks>
    /// A display name alone answers "who" only for somebody who already knows them. The
    /// address is the account, and the account is what an admin reading this acts on —
    /// so every sentence that names a person in a notification carries it.
    /// </remarks>
    public override string ToString() =>
        string.IsNullOrWhiteSpace(Name) ? Email : $"{Name} ({Email})";
}
