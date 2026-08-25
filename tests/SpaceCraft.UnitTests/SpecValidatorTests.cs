using System.Text;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Infrastructure.OpenApi;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// Documents what validation actually catches. Microsoft.OpenApi handles parsing and
/// structural diagnostics; everything security- or product-related is our own policy layer,
/// and these tests are what stop that layer from silently regressing.
/// </summary>
public class SpecValidatorTests
{
    private static readonly OpenApiSpecValidator Validator = new(NullLogger<OpenApiSpecValidator>.Instance);

    private static Task<SpecValidationResult> ValidateAsync(string yaml, string fileName = "openapi.yaml") =>
        Validator.ValidateAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(yaml)), fileName, TestContext.Current.CancellationToken);

    private const string MinimalValid = """
        openapi: 3.0.3
        info:
          title: Example API
          version: 1.0.0
        paths:
          /things:
            get:
              operationId: listThings
              responses:
                '200':
                  description: ok
        """;

    [Fact]
    public async Task A_well_formed_document_is_valid()
    {
        var result = await ValidateAsync(MinimalValid);

        result.Status.ShouldBe(SpecValidationStatus.Valid);
        result.ApiTitle.ShouldBe("Example API");
        result.ApiVersion.ShouldBe("1.0.0");
        result.OperationCount.ShouldBe(1);
        result.IsGeneratable.ShouldBeTrue();
    }

    [Fact]
    public async Task Json_is_accepted_as_well_as_yaml()
    {
        const string Json = """
            {"openapi":"3.0.3","info":{"title":"Example API","version":"1.0.0"},
             "paths":{"/things":{"get":{"operationId":"listThings",
             "responses":{"200":{"description":"ok"}}}}}}
            """;

        var result = await ValidateAsync(Json, "openapi.json");

        result.IsGeneratable.ShouldBeTrue();
        result.ApiTitle.ShouldBe("Example API");
    }

    [Fact]
    public async Task Malformed_yaml_is_a_result_not_an_exception()
    {
        // A parse failure is an expected outcome of accepting uploads, not a crash.
        var result = await ValidateAsync("this: is: not: valid: openapi:");

        result.Status.ShouldBe(SpecValidationStatus.Invalid);
        result.ErrorCount.ShouldBeGreaterThan(0);
        result.IsGeneratable.ShouldBeFalse();
    }

    [Fact]
    public async Task A_missing_version_is_rejected_because_the_sdk_version_comes_from_it()
    {
        var result = await ValidateAsync("""
            openapi: 3.0.3
            info:
              title: Example API
            paths:
              /things:
                get:
                  operationId: listThings
                  responses:
                    '200':
                      description: ok
            """);

        result.Status.ShouldBe(SpecValidationStatus.Invalid);
        result.Diagnostics.ShouldContain(d => d.Message.Contains("info.version"));
    }

    [Fact]
    public async Task A_document_with_no_operations_is_rejected()
    {
        var result = await ValidateAsync("""
            openapi: 3.0.3
            info:
              title: Empty API
              version: 1.0.0
            paths: {}
            """);

        result.Status.ShouldBe(SpecValidationStatus.Invalid);
        result.Diagnostics.ShouldContain(d => d.Message.Contains("no operations"));
    }

    [Fact]
    public async Task A_missing_operationId_is_a_warning_not_a_failure()
    {
        // Generators synthesise a name from the path; the result is just uglier.
        var result = await ValidateAsync("""
            openapi: 3.0.3
            info:
              title: Example API
              version: 1.0.0
            paths:
              /things:
                get:
                  responses:
                    '200':
                      description: ok
            """);

        result.Status.ShouldBe(SpecValidationStatus.ValidWithWarnings);
        result.WarningCount.ShouldBe(1);
        result.IsGeneratable.ShouldBeTrue();
    }

    [Fact]
    public async Task A_remote_ref_does_not_cause_an_outbound_request()
    {
        // The security property that matters most: a spec pointing at cloud instance
        // metadata must never make the worker fetch it. Resolution is off, so the
        // reference is simply left unresolved.
        var result = await ValidateAsync("""
            openapi: 3.0.3
            info:
              title: SSRF Attempt
              version: 1.0.0
            paths:
              /things:
                get:
                  operationId: listThings
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            $ref: 'http://169.254.169.254/latest/meta-data/iam/security-credentials/'
            """);

        // The document is rejected outright rather than partially resolved — and, crucially,
        // the call returns locally without ever dialling 169.254.169.254.
        result.Status.ShouldBe(SpecValidationStatus.Invalid);
        result.IsGeneratable.ShouldBeFalse();
    }

    [Fact]
    public async Task An_oversized_document_is_rejected_before_parsing()
    {
        var huge = new MemoryStream(new byte[SpecLimits.MaxSizeInBytes + 1]);

        var result = await Validator.ValidateAsync(huge, "openapi.yaml", TestContext.Current.CancellationToken);

        result.Status.ShouldBe(SpecValidationStatus.Invalid);
        result.Diagnostics.ShouldContain(d => d.Message.Contains("limit is"));
    }

    [Fact]
    public async Task An_empty_document_is_rejected()
    {
        var result = await ValidateAsync("");

        result.IsGeneratable.ShouldBeFalse();
    }

    [Fact]
    public async Task Openapi_3_1_documents_are_accepted()
    {
        var result = await ValidateAsync("""
            openapi: 3.1.0
            info:
              title: Modern API
              version: 2.0.0
            paths:
              /things:
                get:
                  operationId: listThings
                  responses:
                    '200':
                      description: ok
            """);

        result.IsGeneratable.ShouldBeTrue();
        result.ApiVersion.ShouldBe("2.0.0");
    }
}
