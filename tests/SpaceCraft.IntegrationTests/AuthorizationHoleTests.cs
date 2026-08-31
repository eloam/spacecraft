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
/// Two ways in that an audit found, kept shut.
/// </summary>
/// <remarks>
/// Both were reachable by anyone who could sign up, and both were quiet: the first destroyed
/// another tenant's work and answered 204, the second granted a role and answered 200. They
/// are tested end to end rather than at the handler, because what was wrong in each case was
/// the relationship between two things — a guard and a lookup, a caller and a role — which
/// only exists once the request is assembled.
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class AuthorizationHoleTests(SpaceCraftApi api)
{
    [Fact]
    public async Task A_target_cannot_be_deleted_from_an_organization_it_does_not_belong_to()
    {
        // The victim: an ordinary project with an ordinary target.
        var owner = await api.SignedInAsync(Email());
        var (victimOrg, victimProject) = await ProjectAsync(owner);
        var target = await AddTargetAsync(owner, victimOrg, victimProject, "@acme/billing-sdk");

        // The attacker needs no standing in that organization at all — only its ids, which a
        // Viewer can read, and an organization of their own, which anyone may create.
        var mallory = await api.SignedInAsync(Email());
        var (malloryOrg, _) = await ProjectAsync(mallory);

        var deleted = await mallory.SendAsync(new HttpRequestMessage(
            HttpMethod.Delete,
            new Uri($"/api/v1/organizations/{malloryOrg}/projects/{victimProject}/targets/{target}", UriKind.Relative))
        {
            Content = JsonContent.Create(new { name = "@acme/billing-sdk" }),
        }, TestContext.Current.CancellationToken);

        // 404, and it must not echo the package name back: the refusal used to name it, which
        // handed over the confirmation the endpoint was asking for.
        deleted.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await deleted.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .ShouldNotContain("billing-sdk");

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
        (await db.SdkTargets.AnyAsync(t => t.Id == target, TestContext.Current.CancellationToken))
            .ShouldBeTrue("the target still belongs to the organization that owns it");
    }

    [Fact]
    public async Task An_admin_cannot_grant_themselves_a_role_they_do_not_hold()
    {
        var owner = await api.SignedInAsync(Email());
        var (organizationId, _) = await ProjectAsync(owner);

        var adminEmail = Email();
        var admin = await api.SignedInAsync(adminEmail);
        await JoinAsync(organizationId, adminEmail, OrganizationRole.Admin);

        var promoted = await admin.PatchAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/members/{await UserIdAsync(adminEmail)}", UriKind.Relative),
            new { role = (int)OrganizationRole.Owner },
            TestContext.Current.CancellationToken);

        // Owner is what deleting an organization takes, and the reservation exists precisely
        // because an admin was appointed by somebody else.
        promoted.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
        var userId = await UserIdAsync(adminEmail);
        var role = await db.OrganizationMembers
            .Where(m => m.OrganizationId == organizationId && m.UserId == userId)
            .Select(m => m.Role)
            .SingleAsync(TestContext.Current.CancellationToken);

        role.ShouldBe(OrganizationRole.Admin);
    }

    private static string Email() => $"{Guid.NewGuid():N}@example.test";

    private async Task<Guid> UserIdAsync(string email)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
        return await db.Users.Where(u => u.Email == email).Select(u => u.Id)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private async Task JoinAsync(Guid organizationId, string email, OrganizationRole role)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id)
            .SingleAsync(TestContext.Current.CancellationToken);
        var organization = await db.Organizations.Include(o => o.Members)
            .SingleAsync(o => o.Id == organizationId, TestContext.Current.CancellationToken);

        organization.AddMember(userId, role);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<Guid> AddTargetAsync(
        HttpClient client, Guid organizationId, Guid projectId, string packageName)
    {
        var created = await client.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/projects/{projectId}/targets", UriKind.Relative),
            new { language = 1, packageName, autoPublish = false },
            TestContext.Current.CancellationToken);

        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<IdPayload>(TestContext.Current.CancellationToken))!.Id;
    }

    private static async Task<(Guid OrganizationId, Guid ProjectId)> ProjectAsync(HttpClient client)
    {
        var organization = await client.PostAsJsonAsync(
            "/api/v1/organizations",
            new { name = $"Org{Guid.NewGuid():N}"[..14] },
            TestContext.Current.CancellationToken);

        organization.EnsureSuccessStatusCode();
        var org = await organization.Content.ReadFromJsonAsync<IdPayload>(TestContext.Current.CancellationToken);

        var project = await client.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{org!.Id}/projects", UriKind.Relative),
            new { name = "Billing API" },
            TestContext.Current.CancellationToken);

        project.EnsureSuccessStatusCode();
        var created = await project.Content.ReadFromJsonAsync<IdPayload>(TestContext.Current.CancellationToken);

        return (org.Id, created!.Id);
    }

    private sealed record IdPayload(Guid Id);
}
