namespace SpaceCraft.Api.Features.Account.Responses;

/// <summary>Blockers that must be cleared before an account can be deleted.</summary>
public sealed record DeletionBlocker(string OrganizationName, string Reason);
