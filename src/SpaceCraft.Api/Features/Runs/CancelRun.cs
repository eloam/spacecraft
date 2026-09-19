using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Runs.Responses;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Runs;

public static class CancelRun
{
    public static async Task<Results<Ok<RunResponse>, ProblemHttpResult>> HandleAsync(
        Guid organizationId,
        Guid runId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Member, ct) is { } denied)
        {
            return denied;
        }

        var run = await db.PipelineRuns
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == runId && r.OrganizationId == organizationId, ct);

        if (run is null)
        {
            return ApiResults.NotFound("The run");
        }

        if (run.IsTerminal)
        {
            return ApiResults.Conflict($"This run has already finished ({run.Status}).");
        }

        run.Cancel($"Cancelled by {principal.Email() ?? principal.Id().ToString()}.");
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(RunResponse.From(run));
    }
}
