using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Domain.Pipelines;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Runs;

public static class GetStepLog
{
    public static async Task<Results<ContentHttpResult, ProblemHttpResult, ValidationProblem>> HandleAsync(
        Guid organizationId,
        Guid runId,
        string step,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        IObjectStorage storage,
        CancellationToken ct)
    {
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Viewer, ct) is { } denied)
        {
            return denied;
        }

        if (!Enum.TryParse<StepKind>(step, ignoreCase: true, out var kind))
        {
            return ApiResults.Invalid(nameof(step), $"Unknown step '{step}'.");
        }

        var run = await db.PipelineRuns
            .AsNoTracking()
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == runId && r.OrganizationId == organizationId, ct);

        if (run is null)
        {
            return ApiResults.NotFound("The run");
        }

        var logKey = run.Steps.FirstOrDefault(s => s.Kind == kind)?.LogStorageKey;
        if (logKey is null)
        {
            return ApiResults.NotFound("A log for this step");
        }

        try
        {
            // Logs are already redacted on their way out of the sandbox.
            var content = await storage.GetTextAsync(logKey, ct);
            return TypedResults.Text(content, "text/plain; charset=utf-8");
        }
        catch (ObjectNotFoundException)
        {
            return ApiResults.NotFound("A log for this step");
        }
    }
}
