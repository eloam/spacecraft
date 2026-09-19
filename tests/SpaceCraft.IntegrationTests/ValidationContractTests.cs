using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SpaceCraft.Domain.Common;
using Shouldly;
using Xunit;

namespace SpaceCraft.IntegrationTests;

/// <summary>
/// The shape of a validation answer, as the web client reads it.
/// </summary>
/// <remarks>
/// The rules moved from the handlers into each request's Validator, run by a filter on the
/// API group. Nothing in the unit tests can prove that the filter is wired, that the
/// validators are found by the assembly scan, or that the problem document reaches the wire
/// with the key the client looks up — errors.Email, spelled exactly so — and the message it
/// shows. These do, once, per kind of answer.
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class ValidationContractTests(SpaceCraftApi api)
{
    [Fact]
    public async Task A_malformed_invitation_is_refused_under_the_field_the_page_reads()
    {
        var owner = await api.SignedInAsync(Email());
        var organizationId = await OrganizationAsync(owner);

        var added = await owner.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/members", UriKind.Relative),
            new { email = "nope", role = 2 },
            TestContext.Current.CancellationToken);

        added.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await added.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(TestContext.Current.CancellationToken);
        problem.ShouldNotBeNull();
        problem.Errors.ShouldContainKey("Email");
        problem.Errors["Email"].ShouldBe(["Enter a valid email address."]);
    }

    [Fact]
    public async Task A_name_outside_the_naming_rule_is_refused_with_the_rule()
    {
        var owner = await api.SignedInAsync(Email());

        var created = await owner.PostAsJsonAsync(
            "/api/v1/organizations",
            new { name = "Billing/v2" },
            TestContext.Current.CancellationToken);

        created.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await created.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(TestContext.Current.CancellationToken);
        problem.ShouldNotBeNull();
        problem.Errors.ShouldContainKey("Name");
        problem.Errors["Name"].ShouldBe([ResourceName.Rule]);
    }

    [Fact]
    public async Task Registering_without_a_password_is_a_validation_problem_not_a_crash()
    {
        // A null password used to reach UserManager.CreateAsync, which throws on it.
        var client = api.CreateClient();

        var registered = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email = Email() },
            TestContext.Current.CancellationToken);

        registered.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await registered.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(TestContext.Current.CancellationToken);
        problem.ShouldNotBeNull();
        problem.Errors.ShouldContainKey("Password");
        problem.Errors["Password"].ShouldBe(["A password is required."]);
    }

    [Fact]
    public async Task A_missing_body_is_still_the_flat_bad_request_problem()
    {
        // Binding fails before any validator could run, and the answer must stay the one
        // BadRequestExceptionHandler writes rather than become a validation problem with a
        // parameter name in it.
        var owner = await api.SignedInAsync(Email());

        using var empty = new StringContent(string.Empty, System.Text.Encoding.UTF8, "application/json");
        var created = await owner.PostAsync(
            new Uri("/api/v1/organizations", UriKind.Relative),
            empty,
            TestContext.Current.CancellationToken);

        created.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await created.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);
        problem.ShouldNotBeNull();
        problem.Title.ShouldBe("Bad request");
        problem.Detail.ShouldBe("The request body was missing or could not be read.");
    }

    private static string Email() => $"{Guid.NewGuid():N}@example.test";

    private static async Task<Guid> OrganizationAsync(HttpClient client)
    {
        var created = await client.PostAsJsonAsync(
            "/api/v1/organizations",
            new { name = $"Org{Guid.NewGuid():N}"[..14] },
            TestContext.Current.CancellationToken);

        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<IdPayload>(TestContext.Current.CancellationToken))!.Id;
    }

    private sealed record IdPayload(Guid Id);
}
