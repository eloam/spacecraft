using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpaceCraft.Api.Common;
using Microsoft.Extensions.Options;

namespace SpaceCraft.Api.Email;

/// <summary>
/// Sends through Scaleway Transactional Email.
/// <para>
/// The sending address must belong to a domain verified in the Scaleway console — SPF, DKIM
/// and MX records — or every call is rejected. That is a deployment prerequisite, not
/// something configuration can work around.
/// </para>
/// <para>
/// Message bodies are never logged. A password reset link is a bearer credential, and an
/// error path that dumps the body would put it in the log store anyway, which is exactly
/// what moving off the logging transport was meant to stop.
/// </para>
/// </summary>
public sealed class ScalewayEmailTransport(
    HttpClient http,
    IOptions<ScalewayEmailOptions> options,
    ILogger<ScalewayEmailTransport> logger) : IEmailTransport
{
    private readonly ScalewayEmailOptions _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var payload = new SendEmailRequest(
            new Address(_options.FromEmail, _options.FromName),
            [new Address(message.To, null)],
            message.Subject,
            message.TextBody,
            _options.ProjectId!);

        // Retries, backoff with jitter, per-attempt timeouts and the circuit breaker all live
        // in the resilience pipeline attached to this client, not here.
        try
        {
            // Serialised up front rather than with PostAsJsonAsync: that streams the body
            // and so sends Transfer-Encoding: chunked with no Content-Length. Plenty of API
            // gateways handle chunked requests poorly, and a buffered body is also what makes
            // a retry replayable.
            using var content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await http.PostAsync(
                $"/transactional-email/v1alpha1/regions/{_options.Region}/emails", content, ct);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Sent {Subject} to {Recipient}", message.Subject, Mask(message.To));
                return;
            }

            // Status and reason are safe to record; the body we sent carries a live
            // credential and never is.
            var detail = await SafeReadAsync(response, ct);
            logger.LogError(
                "Scaleway rejected {Subject} for {Recipient}: {Status} {Detail}",
                message.Subject, Mask(message.To), (int)response.StatusCode, detail);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                   && !ct.IsCancellationRequested)
        {
            // The pipeline has already exhausted its retries, or the breaker is open.
            logger.LogError(ex, "Could not deliver {Subject} to {Recipient}",
                message.Subject, Mask(message.To));
        }
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            return body.Length > 500 ? body[..500] : body;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return "(response body unavailable)";
        }
    }

    /// <summary>
    /// Logs enough of an address to correlate a complaint with a delivery, without putting
    /// the full address in a log that may be shipped elsewhere.
    /// </summary>
    private static string Mask(string address)
    {
        var at = address.IndexOf('@', StringComparison.Ordinal);
        return at <= 1 ? "***" : $"{address[0]}***{address[at..]}";
    }

    /// <summary>
    /// Name is omitted rather than sent as null — an explicit null is a value a strict API
    /// may reject, while an absent optional field never is.
    /// </summary>
    private sealed record Address(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("name")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name);

    private sealed record SendEmailRequest(
        [property: JsonPropertyName("from")] Address From,
        [property: JsonPropertyName("to")] Address[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("project_id")] string ProjectId);
}
