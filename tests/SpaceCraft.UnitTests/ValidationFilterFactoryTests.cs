using System.Reflection;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using SpaceCraft.Api.Common;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// The filter that stands between a bound request and its handler: it must run the
/// validator a request record carries, answer the same problem the handlers used to, and
/// cost nothing where there is nothing to validate.
/// </summary>
public sealed class ValidationFilterFactoryTests
{
    public sealed record Sample(string Name)
    {
        public sealed class Validator : AbstractValidator<Sample>
        {
            public Validator()
            {
                RuleFor(sample => sample.Name).NotEmpty().WithMessage("Give it a name.");
            }
        }
    }

    // Only their signatures matter: the factory reads the parameters, never calls them.
    private static Ok<string> Handle(Sample sample) => TypedResults.Ok(sample.Name);

    private static Ok<string> HandleOptional(Sample? sample) => TypedResults.Ok(sample?.Name ?? string.Empty);

    private static Ok<Guid> HandleNothing(Guid id) => TypedResults.Ok(id);

    private static readonly EndpointFilterDelegate Next = _ => ValueTask.FromResult<object?>(TypedResults.NoContent());

    private static ServiceProvider Services() =>
        new ServiceCollection().AddValidatorsFromAssemblyContaining<ValidationFilterFactoryTests>().BuildServiceProvider();

    private static EndpointFilterDelegate FilterFor(string handler, IServiceProvider services) =>
        ValidationFilterFactory.Create(
            new EndpointFilterFactoryContext
            {
                MethodInfo = typeof(ValidationFilterFactoryTests).GetMethod(handler, BindingFlags.NonPublic | BindingFlags.Static)!,
                ApplicationServices = services,
            },
            Next);

    private static DefaultEndpointFilterInvocationContext Invocation(IServiceProvider services, params object?[] arguments) =>
        new DefaultEndpointFilterInvocationContext(new DefaultHttpContext { RequestServices = services }, arguments);

    [Fact]
    public void A_handler_with_nothing_to_validate_is_left_alone()
    {
        using var services = Services();

        FilterFor(nameof(HandleNothing), services).ShouldBeSameAs(Next);
    }

    [Fact]
    public async Task An_invalid_request_is_answered_before_the_handler_runs()
    {
        using var services = Services();
        var filter = FilterFor(nameof(Handle), services);

        var result = await filter(Invocation(services, new Sample("")));

        var problem = result.ShouldBeOfType<ValidationProblem>();
        problem.ProblemDetails.Errors.ShouldContainKey("Name");
        problem.ProblemDetails.Errors["Name"].ShouldBe(["Give it a name."]);
    }

    [Fact]
    public async Task A_valid_request_reaches_the_handler()
    {
        using var services = Services();
        var filter = FilterFor(nameof(Handle), services);

        var result = await filter(Invocation(services, new Sample("fine")));

        result.ShouldBeOfType<NoContent>();
    }

    [Fact]
    public async Task An_absent_optional_body_is_not_validated()
    {
        using var services = Services();
        var filter = FilterFor(nameof(HandleOptional), services);

        var result = await filter(Invocation(services, [null]));

        result.ShouldBeOfType<NoContent>();
    }
}
