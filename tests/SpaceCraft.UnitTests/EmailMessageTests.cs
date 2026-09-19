using SpaceCraft.Api.Email;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// The key a log line calls a message by, so that the line can name the message without
/// naming its reader.
/// </summary>
public sealed class EmailMessageTests
{
    [Fact]
    public void Every_message_is_born_with_its_own_key()
    {
        var first = new EmailMessage("somebody@example.test", "Reset your password", "…");
        var second = new EmailMessage("somebody@example.test", "Reset your password", "…");

        // Same reader, same subject, same body — and still two messages, because a relay
        // that refuses one and takes the other is a story the log has to be able to tell.
        first.Id.ShouldNotBe(Guid.Empty);
        second.Id.ShouldNotBe(Guid.Empty);
        first.Id.ShouldNotBe(second.Id);
    }

    [Fact]
    public void The_key_says_nothing_about_who_the_message_is_for()
    {
        var message = new EmailMessage("somebody@example.test", "Welcome to SpaceCraft", "…");

        // A version 7 GUID: a timestamp and random bits. Nothing derived from the address,
        // so a log full of keys cannot be turned back into a list of readers.
        message.Id.Version.ShouldBe(7);
    }
}
