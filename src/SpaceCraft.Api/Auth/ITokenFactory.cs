using SpaceCraft.Infrastructure.Identity;

namespace SpaceCraft.Api.Auth;

public interface ITokenFactory
{
    /// <summary>
    /// Issues an access/refresh pair. <paramref name="sessionId"/> is the refresh token family
    /// the pair belongs to, carried as the <c>sid</c> claim so a request can tell which of the
    /// user's sessions made it — that is what lets "sign out everywhere else" spare the caller.
    /// </summary>
    TokenPair Create(SpaceCraftUser user, Guid sessionId);

    /// <summary>SHA-256 of a bearer-style token, for storage. Raw tokens are never persisted.</summary>
    string HashToken(string token);
}