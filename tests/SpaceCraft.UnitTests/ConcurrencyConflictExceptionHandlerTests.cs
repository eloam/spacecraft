using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaceCraft.Api.Common;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

public class ConcurrencyConflictExceptionHandlerTests
{
    private sealed class CapturedProblem : IProblemDetailsService
    {
        public ProblemDetails? Written { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Written = context.ProblemDetails;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Written = context.ProblemDetails;
            return ValueTask.FromResult(true);
        }
    }

    [Fact]
    public async Task A_write_the_database_refused_as_stale_is_a_conflict_the_caller_can_retry()
    {
        // What SaveChanges throws when the row's token no longer matches: somebody else wrote
        // it between this handler's read and its write. Nothing broke, so not a 500.
        var problem = new CapturedProblem();
        var context = new DefaultHttpContext();

        var handled = await new ConcurrencyConflictExceptionHandler(problem).TryHandleAsync(
            context,
            new DbUpdateConcurrencyException(
                "The database operation was expected to affect 1 row(s), but actually affected 0 row(s)."),
            CancellationToken.None);

        handled.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status409Conflict);
        problem.Written!.Status.ShouldBe(StatusCodes.Status409Conflict);
        problem.Written.Title.ShouldBe("Conflict");

        // The remedy is the caller's, so the message has to say what it is — and not in EF's
        // words about affected rows, which name nothing the caller can act on.
        problem.Written.Detail.ShouldNotBeNull().ShouldContain("try again");
        problem.Written.Detail.ShouldNotContain("row(s)");
    }

    [Fact]
    public async Task Any_other_database_fault_is_left_to_the_default_handler()
    {
        // DbUpdateException is the base of the one above; a unique violation or a broken
        // connection is a genuine fault and stays a 500.
        var problem = new CapturedProblem();
        var context = new DefaultHttpContext();

        var handled = await new ConcurrencyConflictExceptionHandler(problem).TryHandleAsync(
            context,
            new DbUpdateException("duplicate key value violates unique constraint"),
            CancellationToken.None);

        handled.ShouldBeFalse();
        problem.Written.ShouldBeNull();
    }
}
