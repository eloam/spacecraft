namespace SpaceCraft.Api.Email;

/// <summary>
/// One invitation to send, handed to <see cref="InvitationDispatcher"/>.
/// </summary>
/// <remarks>
/// The invitation row is written on the request thread — the admin who sent it must see it in
/// the listing straight away, and a failure to write it is theirs to hear about. Only the
/// email leaves through here, because a mail relay's latency is not the admin's problem and a
/// relay being down must not lose the invitation itself.
/// </remarks>
public sealed record InvitationRequest(Guid InvitationId, string RawToken);
