using SpaceCraft.Infrastructure.Identity;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

public class PasswordResetTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    private static PasswordResetToken NewToken(TimeSpan? validFor = null) =>
        new(Guid.CreateVersion7(), "a".PadRight(64, 'b'), Now.Add(validFor ?? TimeSpan.FromHours(1)));

    [Fact]
    public void A_fresh_token_is_redeemable() =>
        NewToken().IsRedeemable(Now).ShouldBeTrue();

    [Fact]
    public void An_expired_token_is_not_redeemable() =>
        NewToken().IsRedeemable(Now.AddHours(2)).ShouldBeFalse();

    [Fact]
    public void A_token_expiring_exactly_now_is_not_redeemable() =>
        // The boundary belongs to the past: a token is valid strictly before its expiry.
        NewToken(TimeSpan.FromHours(1)).IsRedeemable(Now.AddHours(1)).ShouldBeFalse();

    [Fact]
    public void A_used_token_cannot_be_redeemed_again()
    {
        var token = NewToken();
        token.MarkUsed(Now);

        // Single use is what stops a reset link in a mailbox from being a permanent key.
        token.IsRedeemable(Now).ShouldBeFalse();
    }

    [Fact]
    public void A_superseded_token_is_not_redeemable()
    {
        var token = NewToken();
        token.Invalidate(Now);

        token.IsRedeemable(Now).ShouldBeFalse();
    }

    [Fact]
    public void Invalidation_keeps_the_first_timestamp()
    {
        var token = NewToken();
        token.Invalidate(Now);
        token.Invalidate(Now.AddMinutes(5));

        // When the token was neutralised is the interesting fact, not the last time
        // something touched it.
        token.InvalidatedAt.ShouldBe(Now);
    }

    [Fact]
    public void A_token_requires_a_hash() =>
        Should.Throw<ArgumentException>(
            () => new PasswordResetToken(Guid.CreateVersion7(), "  ", Now.AddHours(1)));
}
