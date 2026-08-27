using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SpaceCraft.Api.Common;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

public class BadRequestExceptionHandlerTests
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
    public async Task A_missing_body_is_reported_as_the_client_error_it_is()
    {
        // What every endpoint taking a [FromBody] parameter throws when the body is absent.
        var problem = new CapturedProblem();
        var context = new DefaultHttpContext();

        var handled = await new BadRequestExceptionHandler(problem).TryHandleAsync(
            context,
            new BadHttpRequestException("Required parameter was not provided from body.", 400),
            CancellationToken.None);

        handled.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe(400);
        problem.Written!.Status.ShouldBe(400);
    }

    [Fact]
    public async Task An_oversized_body_keeps_its_own_status()
    {
        // The status travels on the exception, so this must not be hard-coded to 400.
        var problem = new CapturedProblem();
        var context = new DefaultHttpContext();

        await new BadRequestExceptionHandler(problem).TryHandleAsync(
            context,
            new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge),
            CancellationToken.None);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status413PayloadTooLarge);
    }

    [Fact]
    public async Task A_genuine_fault_is_left_to_the_default_handler()
    {
        // Declining is what keeps real failures reported as 500 rather than swallowed here.
        var problem = new CapturedProblem();
        var context = new DefaultHttpContext();

        var handled = await new BadRequestExceptionHandler(problem).TryHandleAsync(
            context,
            new InvalidOperationException("something actually broke"),
            CancellationToken.None);

        handled.ShouldBeFalse();
        problem.Written.ShouldBeNull();
    }

    [Fact]
    public async Task The_exception_message_is_not_handed_to_the_caller()
    {
        // It names the handler's parameter — an implementation detail, and nothing the
        // caller can act on.
        var problem = new CapturedProblem();

        await new BadRequestExceptionHandler(problem).TryHandleAsync(
            new DefaultHttpContext(),
            new BadHttpRequestException("Required parameter \"ConfirmByNameRequest request\" was not provided from body.", 400),
            CancellationToken.None);

        problem.Written!.Detail!.ShouldNotContain("ConfirmByNameRequest");
    }
}
