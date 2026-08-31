using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using Shouldly;
using Xunit;

namespace SpaceCraft.IntegrationTests;

/// <summary>
/// Who may see what, over real HTTP against a real database.
/// </summary>
/// <remarks>
/// The guard itself is unit-tested. What cannot be unit-tested is whether every endpoint
/// actually goes through it, whether the row it then loads is scoped to the same organization
/// the guard checked, and what a caller is told when it says no — all of which are properties
/// of the assembled request path.
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class TenancyTests(SpaceCraftApi api)
{
    private static string Email() => $"{Guid.NewGuid():N}@example.test";

    private static async Task<Guid> CreateOrganizationAsync(HttpClient client, string name)
    {
        var created = await client.PostAsJsonAsync("/api/v1/organizations", new { name }, TestContext.Current.CancellationToken);
        created.EnsureSuccessStatusCode();

        var body = await created.Content.ReadFromJsonAsync<OrganizationPayload>(TestContext.Current.CancellationToken);
        return body!.Id;
    }

    [Fact]
    public async Task A_stranger_is_told_the_organization_does_not_exist()
    {
        var owner = await api.SignedInAsync(Email());
        var organizationId = await CreateOrganizationAsync(owner, "Acme");

        var stranger = await api.SignedInAsync(Email());
        var response = await stranger.GetAsync(new Uri($"/api/v1/organizations/{organizationId}/runs", UriKind.Relative), TestContext.Current.CancellationToken);

        // The mask: answering anything else would confirm that this id is real, and the API
        // would be a directory of organizations.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<ProblemPayload>(TestContext.Current.CancellationToken))!.Detail
            .ShouldBe("The organization was not found.");
    }

    [Fact]
    public async Task A_member_whose_role_is_too_low_is_told_which_role_it_takes()
    {
        var owner = await api.SignedInAsync(Email());
        var organizationId = await CreateOrganizationAsync(owner, "Beta");

        var viewerEmail = Email();
        var viewer = await api.SignedInAsync(viewerEmail);
        await JoinAsync(organizationId, viewerEmail, OrganizationRole.Viewer);

        // Credentials are an admin concern; a viewer is a member, so the mask has nothing
        // left to conceal and would only contradict the organization they can already see.
        var response = await viewer.GetAsync(new Uri($"/api/v1/organizations/{organizationId}/credentials", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<ProblemPayload>(TestContext.Current.CancellationToken))!.Detail
            .ShouldBe("This action requires the Admin role.");
    }

    [Fact]
    public async Task A_viewer_may_read_the_runs_of_the_organization_they_belong_to()
    {
        var owner = await api.SignedInAsync(Email());
        var organizationId = await CreateOrganizationAsync(owner, "Gamma");

        var viewerEmail = Email();
        var viewer = await api.SignedInAsync(viewerEmail);
        await JoinAsync(organizationId, viewerEmail, OrganizationRole.Viewer);

        var response = await viewer.GetAsync(new Uri($"/api/v1/organizations/{organizationId}/runs", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// Adds a membership directly. The invitation flow has its own tests and sends mail;
    /// what is under test here is what a role permits, not how someone came by it.
    /// </summary>
    private async Task JoinAsync(Guid organizationId, string email, OrganizationRole role)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        var user = await db.Users.SingleAsync(u => u.Email == email, TestContext.Current.CancellationToken);
        var organization = await db.Organizations
            .Include(o => o.Members)
            .SingleAsync(o => o.Id == organizationId, TestContext.Current.CancellationToken);

        organization.AddMember(user.Id, role);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private sealed record OrganizationPayload(Guid Id, string Slug);

    private sealed record ProblemPayload(string? Detail);
}
