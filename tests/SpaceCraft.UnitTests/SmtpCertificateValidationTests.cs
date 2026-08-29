using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using SpaceCraft.Api.Email;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// The transport accepts one thing a stricter client would refuse — a revocation check that
/// could not be carried out — and nothing else. These pin the difference, because the cheap
/// version of this fix is one line that accepts a certificate known to be revoked.
/// </summary>
public class SmtpCertificateValidationTests
{
    private static X509ChainStatus[] Status(params X509ChainStatusFlags[] flags) =>
        flags.Select(flag => new X509ChainStatus { Status = flag }).ToArray();

    [Fact]
    public void A_clean_certificate_is_accepted() =>
        SmtpEmailTransport.IsAcceptable(SslPolicyErrors.None, null, requireRevocation: false).ShouldBeTrue();

    [Theory]
    [InlineData(X509ChainStatusFlags.RevocationStatusUnknown)]
    [InlineData(X509ChainStatusFlags.OfflineRevocation)]
    public void An_unanswered_revocation_check_is_accepted(X509ChainStatusFlags flag) =>
        // Common on macOS and in any container with no route to an OCSP responder. Refusing
        // would stop password resets the day a responder has a bad hour.
        SmtpEmailTransport.IsAcceptable(
            SslPolicyErrors.RemoteCertificateChainErrors, Status(flag), requireRevocation: false)
            .ShouldBeTrue();

    [Fact]
    public void A_revocation_that_came_back_positive_is_refused() =>
        SmtpEmailTransport.IsAcceptable(
            SslPolicyErrors.RemoteCertificateChainErrors, Status(X509ChainStatusFlags.Revoked),
            requireRevocation: false)
            .ShouldBeFalse();

    [Theory]
    [InlineData(X509ChainStatusFlags.UntrustedRoot)]
    [InlineData(X509ChainStatusFlags.NotTimeValid)]
    [InlineData(X509ChainStatusFlags.PartialChain)]
    public void A_chain_that_fails_for_any_other_reason_is_refused(X509ChainStatusFlags flag) =>
        SmtpEmailTransport.IsAcceptable(
            SslPolicyErrors.RemoteCertificateChainErrors, Status(flag), requireRevocation: false)
            .ShouldBeFalse();

    [Fact]
    public void An_unanswered_check_alongside_a_real_failure_is_refused() =>
        // All of them, not any: an expired certificate does not become acceptable because a
        // revocation check also failed to complete.
        SmtpEmailTransport.IsAcceptable(
            SslPolicyErrors.RemoteCertificateChainErrors,
            Status(X509ChainStatusFlags.RevocationStatusUnknown, X509ChainStatusFlags.NotTimeValid),
            requireRevocation: false)
            .ShouldBeFalse();

    [Theory]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch)]
    [InlineData(SslPolicyErrors.RemoteCertificateNotAvailable)]
    public void A_wrong_or_missing_certificate_is_never_negotiable(SslPolicyErrors errors) =>
        SmtpEmailTransport.IsAcceptable(
            errors, Status(X509ChainStatusFlags.RevocationStatusUnknown), requireRevocation: false)
            .ShouldBeFalse();

    [Fact]
    public void A_chain_error_with_nothing_explaining_it_is_refused() =>
        // Empty status with a chain error is not something to interpret generously.
        SmtpEmailTransport.IsAcceptable(
            SslPolicyErrors.RemoteCertificateChainErrors, [], requireRevocation: false)
            .ShouldBeFalse();

    [Theory]
    [InlineData(X509ChainStatusFlags.RevocationStatusUnknown)]
    [InlineData(X509ChainStatusFlags.OfflineRevocation)]
    public void Requiring_revocation_refuses_what_it_would_otherwise_accept(X509ChainStatusFlags flag) =>
        // The whole point of the setting: a deployment that can reach a responder gets to
        // insist on an answer, rather than inheriting a default chosen for one that cannot.
        SmtpEmailTransport.IsAcceptable(
            SslPolicyErrors.RemoteCertificateChainErrors, Status(flag), requireRevocation: true)
            .ShouldBeFalse();

    [Fact]
    public void Requiring_revocation_still_accepts_a_clean_certificate() =>
        // Strict is about an unanswered check, not about refusing everything: a certificate
        // whose revocation came back negative has no chain error to interpret.
        SmtpEmailTransport.IsAcceptable(SslPolicyErrors.None, null, requireRevocation: true)
            .ShouldBeTrue();
}
