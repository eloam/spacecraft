namespace SpaceCraft.Api.Features.Waitlist.Requests;

public sealed record WaitlistSignupRequest(string Email, string? Source);
