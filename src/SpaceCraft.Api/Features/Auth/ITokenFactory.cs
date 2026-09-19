using SpaceCraft.Infrastructure.Identity;

namespace SpaceCraft.Api.Features.Auth;

public interface ITokenFactory
{
    /// <summary>
    /// Issues an access/refresh pair. <paramref name="sessionId"/> is the refresh token family
    /// the pair belongs to, carried as the <c>sid</c> claim so a request can tell which of the
    /// user's sessions made it — that is what lets "sign out everywhere else" spare the caller.
    /// </summary>
    TokenPair Create(SpaceCraftUser user, Guid sessionId);
}