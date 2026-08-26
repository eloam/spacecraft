using System.Security.Claims;
using SpaceCraft.Api.Common;
using Microsoft.IdentityModel.JsonWebTokens;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

public class CurrentUserTests
{
    private static ClaimsPrincipal PrincipalWith(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));

    [Fact]
    public void The_session_is_read_from_the_sid_claim()
    {
        var session = Guid.CreateVersion7();

        PrincipalWith((JwtRegisteredClaimNames.Sid, session.ToString()))
            .SessionId().ShouldBe(session);
    }

    [Fact]
    public void A_token_issued_before_sessions_existed_has_no_session() =>
        // Tokens in the wild predate the claim, so every caller has to cope with null rather
        // than assuming it is there.
        PrincipalWith((JwtRegisteredClaimNames.Sub, Guid.CreateVersion7().ToString()))
            .SessionId().ShouldBeNull();

    [Fact]
    public void A_malformed_session_claim_is_treated_as_absent() =>
        PrincipalWith((JwtRegisteredClaimNames.Sid, "not-a-guid")).SessionId().ShouldBeNull();

    [Fact]
    public void The_subject_is_required()
    {
        // Every authenticated endpoint depends on this; failing loudly beats a default Guid
        // that would silently address someone else's data.
        var principal = PrincipalWith((JwtRegisteredClaimNames.Email, "someone@example.com"));

        Should.Throw<InvalidOperationException>(() => principal.Id());
    }
}
