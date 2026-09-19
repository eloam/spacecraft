using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// The guard every organization-scoped endpoint opens with. Two refusals that must not be
/// confused: hiding an organization from a stranger, and telling a member what a role costs.
/// </summary>
public class OrganizationAccessGuardTests
{
    private static readonly Guid Org = Guid.Parse("01a03992-c7bf-7326-9e21-357ced45a013");
    private static readonly Guid User = Guid.Parse("01a039df-05f2-7c12-84ca-f4661bec34e8");

    /// <summary>Answers one role, or none at all for someone who is not a member.</summary>
    private sealed class Membership(OrganizationRole? role) : IOrganizationAccess
    {
        public Task<OrganizationRole?> GetRoleAsync(Guid organizationId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(role);
    }

    private static async Task<ProblemDetails?> DenialFor(OrganizationRole? held, OrganizationRole required)
    {
        var result = await new Membership(held).DenyUnlessAsync(Org, User, required, TestContext.Current.CancellationToken);
        return (result as ProblemHttpResult)?.ProblemDetails;
    }

    [Fact]
    public async Task A_non_member_is_told_the_organization_does_not_exist()
    {
        // The mask is the point: answering anything else would confirm that this id is real,
        // and the API would be a directory of organizations.
        var problem = await DenialFor(held: null, required: OrganizationRole.Viewer);

        problem.ShouldNotBeNull();
        problem.Status.ShouldBe(StatusCodes.Status404NotFound);
        problem.Detail.ShouldBe("The organization was not found.");
    }

    [Fact]
    public async Task A_member_whose_role_is_too_low_is_told_which_role_it_takes()
    {
        // Not the 404: they are already reading the organization, so masking it conceals
        // nothing and only contradicts the page they are on.
        var problem = await DenialFor(held: OrganizationRole.Member, required: OrganizationRole.Admin);

        problem.ShouldNotBeNull();
        problem.Status.ShouldBe(StatusCodes.Status403Forbidden);
        problem.Detail.ShouldBe("This action requires the Admin role.");
    }

    [Theory]
    [InlineData(OrganizationRole.Viewer)]
    [InlineData(OrganizationRole.Member)]
    [InlineData(OrganizationRole.Admin)]
    [InlineData(OrganizationRole.Owner)]
    public async Task Every_role_at_or_above_the_bar_is_let_through(OrganizationRole held)
    {
        (await new Membership(held).DenyUnlessAsync(Org, User, OrganizationRole.Viewer, TestContext.Current.CancellationToken))
            .ShouldBeNull();
    }

    [Fact]
    public async Task A_check_that_passes_hands_back_the_role_it_read()
    {
        // So the handlers that go on to compare roles — who may grant which, who may remove
        // whom — do not ask the database a second time for what the guard just read.
        var check = await new Membership(OrganizationRole.Admin)
            .CheckAsync(Org, User, OrganizationRole.Admin, TestContext.Current.CancellationToken);

        check.Denied.ShouldBeNull();
        check.Role.ShouldBe(OrganizationRole.Admin);
    }

    [Fact]
    public async Task A_check_that_refuses_still_says_which_role_was_held()
    {
        // The 403 names the role it takes; the role held is the other half of that sentence,
        // and a handler answering with more detail should not have to look it up again.
        var check = await new Membership(OrganizationRole.Member)
            .CheckAsync(Org, User, OrganizationRole.Admin, TestContext.Current.CancellationToken);

        check.Denied.ShouldBeOfType<ProblemHttpResult>().ProblemDetails.Status.ShouldBe(StatusCodes.Status403Forbidden);
        check.Role.ShouldBe(OrganizationRole.Member);
    }

    [Fact]
    public async Task A_non_member_has_no_role_to_hand_back()
    {
        var check = await new Membership(null)
            .CheckAsync(Org, User, OrganizationRole.Viewer, TestContext.Current.CancellationToken);

        check.Denied.ShouldBeOfType<ProblemHttpResult>().ProblemDetails.Status.ShouldBe(StatusCodes.Status404NotFound);
        check.Role.ShouldBeNull();
    }

    [Fact]
    public async Task A_viewer_may_download_an_artifact()
    {
        // The floor this endpoint settled on, pinned: the artifact reads like the step logs
        // that describe it, not like cancelling the run.
        (await new Membership(OrganizationRole.Viewer).DenyUnlessAsync(Org, User, OrganizationRole.Viewer, TestContext.Current.CancellationToken))
            .ShouldBeNull();
    }
}
