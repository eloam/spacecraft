using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SpaceCraft.Api.Extensions;

/// <summary>
/// Removes the "string" alternative .NET adds to every numeric schema.
/// <para>
/// The JSON schema exporter emits <c>type: ["integer", "string"]</c> for an <c>int</c>,
/// because System.Text.Json <em>can</em> be configured to read numbers from strings. Our API
/// never sends one, and the union leaks into every generated client as <c>number | string</c>,
/// forcing consumers to narrow a type that only has one real shape.
/// </para>
/// <para>
/// Worth the fifty lines: SpaceCraft generates SDKs from OpenAPI documents, so shipping a
/// sloppy one of its own would be a poor advertisement.
/// </para>
/// </summary>
public sealed class NumericSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);

        if (schema.Type is not { } type)
        {
            return Task.CompletedTask;
        }

        var isNumeric = type.HasFlag(JsonSchemaType.Integer) || type.HasFlag(JsonSchemaType.Number);

        if (isNumeric && type.HasFlag(JsonSchemaType.String))
        {
            schema.Type = type & ~JsonSchemaType.String;

            // The pattern only existed to validate the string form.
            schema.Pattern = null;
        }

        return Task.CompletedTask;
    }
}
