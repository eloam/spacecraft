using FluentValidation;

namespace SpaceCraft.Api.Common;

/// <summary>
/// Runs the validator registered for each of a handler's arguments before the handler, and
/// answers the same validation problem <see cref="ApiResults.Invalid"/> answers.
/// </summary>
/// <remarks>
/// The shape ASP.NET Core's own minimal-API validation takes: an endpoint filter factory on
/// the group, so no handler has to remember to validate and none pays for it when nothing
/// applies — the factory hands back the next delegate untouched. A request record's rules
/// live in its nested <c>Validator</c>; a record without one is passed through.
/// </remarks>
public static class ValidationFilterFactory
{
    public static EndpointFilterDelegate Create(EndpointFilterFactoryContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);

        // What RequestDelegateFactory itself asks to tell a service parameter from a bound one.
        var isService = context.ApplicationServices.GetRequiredService<IServiceProviderIsService>();

        var validated = context.MethodInfo.GetParameters()
            .Select((parameter, index) =>
                (Index: index, ValidatorType: typeof(IValidator<>).MakeGenericType(parameter.ParameterType)))
            .Where(candidate => isService.IsService(candidate.ValidatorType))
            .ToArray();

        if (validated.Length == 0)
        {
            return next;
        }

        return async invocation =>
        {
            foreach (var (index, validatorType) in validated)
            {
                // An optional body arrives as null and has nothing to validate. A required one
                // that is missing never gets this far: binding throws first, and
                // BadRequestExceptionHandler answers that.
                if (invocation.Arguments[index] is not { } argument)
                {
                    continue;
                }

                var validator = (IValidator)invocation.HttpContext.RequestServices.GetRequiredService(validatorType);
                var result = await validator.ValidateAsync(
                    new ValidationContext<object>(argument), invocation.HttpContext.RequestAborted);

                if (!result.IsValid)
                {
                    return TypedResults.ValidationProblem(result.ToDictionary());
                }
            }

            return await next(invocation);
        };
    }
}
