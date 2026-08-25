namespace SpaceCraft.Api.Email;

/// <summary>
/// A waitlist signup awaiting its confirmation email. The IP is hashed at the edge rather
/// than carried raw through the queue.
/// </summary>
public sealed record WaitlistSignup(string EmailAddress, string? Source, string? HashedIp);
