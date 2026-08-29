namespace SpaceCraft.Api.Email;

/// <summary>How the connection to the relay is encrypted.
///
/// <para>
/// An enumeration rather than a boolean because there are three answers, and the third one
/// has to be impossible to reach by accident. MailKit's own <c>Auto</c> is deliberately not
/// among them: it upgrades when the server offers STARTTLS and sends the password in the
/// clear when it does not, so it fails open on exactly the connection that matters.
/// </para>
/// </summary>
public enum SmtpSecurity
{
    /// <summary>Connect in the clear and upgrade. Ports 25, 587 and 2587.</summary>
    StartTls = 0,

    /// <summary>Encrypted from the first byte. Ports 465 and 2465.</summary>
    ImplicitTls = 1,

    /// <summary>
    /// No encryption at all. For a relay on the same machine that has no certificate — a
    /// development inbox — and refused anywhere else: the password and every message would
    /// cross the network readable.
    /// </summary>
    None = 2,
}
