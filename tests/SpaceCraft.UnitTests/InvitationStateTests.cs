using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Identity;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// That an invitation gives the same answer whether it is asked in memory or in a query.
/// </summary>
/// <remarks>
/// It once did not. The predicate lived twice — once as a method the endpoints called, once
/// copied into the query behind the list an admin reads — and when declining was added only
/// the first was told. An invitation somebody had already refused went on being shown as
/// pending, with a link that no longer worked.
/// <para>
/// This walks every state through both forms, so the next one cannot reach one and miss the
/// other.
/// </para>
/// </remarks>
public sealed class InvitationStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<string, Action<OrganizationInvitation>, bool> States() => new()
    {
        { "untouched", _ => { }, true },
        { "accepted", i => i.Accept(Guid.NewGuid(), Now), false },
        { "withdrawn", i => i.Revoke(Now), false },
        { "declined", i => i.Decline(Now), false },
    };

    [Theory]
    [MemberData(nameof(States))]
    public void Both_forms_agree(string state, Action<OrganizationInvitation> put, bool usable)
    {
        var invitation = Invitation(expiresAt: Now.AddDays(7));
        put(invitation);

        invitation.IsRedeemable(Now).ShouldBe(usable, state);
        OrganizationInvitation.Usable(Now).Compile()(invitation).ShouldBe(usable, state);
    }

    [Fact]
    public void An_expired_invitation_is_usable_in_neither()
    {
        var invitation = Invitation(expiresAt: Now.AddSeconds(-1));

        invitation.IsRedeemable(Now).ShouldBeFalse();
        OrganizationInvitation.Usable(Now).Compile()(invitation).ShouldBeFalse();
    }

    private static OrganizationInvitation Invitation(DateTimeOffset expiresAt) =>
        new(
            Guid.NewGuid(),
            "invitee@example.test",
            OrganizationRole.Member,
            Guid.NewGuid(),
            new string('a', 64),
            expiresAt);
}
