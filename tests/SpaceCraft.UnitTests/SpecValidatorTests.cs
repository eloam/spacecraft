using System.Linq;
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

    private static Task<SpecValidationResult> ValidateAsync(
        string yaml,
        string fileName = "openapi.yaml",
        bool strict = false,
        string? previousApiVersion = null) =>
        Validator.ValidateAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(yaml)), fileName, strict, previousApiVersion,
            TestContext.Current.CancellationToken);

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
        // The message is the parser's — InfoRequiredFields covers a missing version, and
        // repeating it ourselves would put two findings on one problem.
        result.Diagnostics.ShouldContain(d =>
            d.Severity == DiagnosticSeverity.Error && d.Message.Contains("'version'"));
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

    /// <summary>
    /// Generated against Kiota 1.34.1: the same document with and without operationIds
    /// produced byte-identical TypeScript, down to the file list. The note this used to emit
    /// asked operators to change something with no effect on what they publish.
    /// </summary>
    [Fact]
    public async Task A_missing_operationId_is_not_reported_because_it_changes_nothing()
    {
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

        result.Status.ShouldBe(SpecValidationStatus.Valid);
        result.Diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_finding_points_at_the_operation_with_its_path_key_escaped()
    {
        // The pointer is what the viewer resolves to a line. Unescaped, "#/paths//things/get"
        // cannot be told apart from a nesting of an empty key and "things"; and stopping at
        // the path item would send both operations to the same line.
        var result = await ValidateAsync("""
            openapi: 3.0.3
            info:
              title: Example API
              version: 1.0.0
            servers:
              - url: https://api.example.com
            paths:
              /things:
                get:
                  responses:
                    '200':
                      description: ok
                post:
                  responses:
                    '201':
                      description: created
            """, strict: true);

        result.Diagnostics
            .Where(d => d.Message.Contains("no documentation at all"))
            .Select(d => d.JsonPointer)
            .ShouldBe(["#/paths/~1things/get", "#/paths/~1things/post"], ignoreOrder: true);
    }

    /// <summary>
    /// Pins a dependency the validator never states: it sets LoadExternalRefs and
    /// LeaveStreamOpen but leaves OpenApiReaderSettings.RuleSet alone, and that default is
    /// what runs the parser's own 21 checks during the load. Emptying the ruleset makes them
    /// all vanish silently — including the dangling-$ref detection that strict mode promotes
    /// to an error, and the uniqueness, required-field and path-parameter rules we
    /// deliberately do not duplicate.
    /// </summary>
    [Fact]
    public async Task Loading_applies_the_parsers_own_rules_without_being_asked()
    {
        // PathMustBeUnique is a ruleset rule, not something the reader notices on its own.
        var result = await ValidateAsync("""
            openapi: 3.0.3
            info:
              title: Example API
              version: 1.0.0
            paths:
              /users/{id}:
                get:
                  responses:
                    '204':
                      description: ok
              /users/{userId}:
                get:
                  responses:
                    '204':
                      description: ok
            """);

        result.Status.ShouldBe(SpecValidationStatus.Invalid);
        result.Diagnostics.ShouldContain(d => d.Message.Contains("MUST be unique"));
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

        var result = await Validator.ValidateAsync(
            huge, "openapi.yaml", ct: TestContext.Current.CancellationToken);

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

    // --- version format -----------------------------------------------------------------

    [Fact]
    public async Task A_four_part_version_warns_without_strict_mode_and_names_both_camps()
    {
        // Nothing checked the format at all before: the parser reports whether info.version
        // is present, which is not whether a registry will take it, and an unusable version
        // was discovered by a run failing after upload.
        var result = await ValidateAsync(MinimalValid.Replace("version: 1.0.0", "version: 1.2.3.4", StringComparison.Ordinal));

        var finding = result.Diagnostics.Single(d => d.JsonPointer == "#/info/version");

        // A warning, not an error: which registry this document publishes to is not known
        // here, and 1.2.3.4 is an ordinary NuGet version. Calling it invalid would repeat the
        // mistake of applying one registry's rules to all of them.
        finding.Severity.ShouldBe(DiagnosticSeverity.Warning);
        finding.Message.ShouldContain("NuGet");
        finding.Message.ShouldContain("Npm");
        result.Status.ShouldNotBe(SpecValidationStatus.Invalid);
    }

    [Fact]
    public async Task A_version_no_registry_takes_is_an_error()
    {
        var result = await ValidateAsync(MinimalValid.Replace("version: 1.0.0", "version: '2024-01-15'", StringComparison.Ordinal));

        var finding = result.Diagnostics.Single(d => d.JsonPointer == "#/info/version");

        finding.Severity.ShouldBe(DiagnosticSeverity.Error);
        result.Status.ShouldBe(SpecValidationStatus.Invalid);
    }

    [Fact]
    public async Task A_three_part_version_says_nothing_at_all()
    {
        // The common case must stay quiet, or the warning becomes noise nobody reads.
        var result = await ValidateAsync(MinimalValid);

        result.Diagnostics.ShouldNotContain(d => d.JsonPointer == "#/info/version");
    }

    [Fact]
    public async Task Strict_mode_reports_a_repeat_it_used_to_miss()
    {
        // Comparison read three components, so a fourth made it give up silently: the same
        // NuGet-shaped version published twice slipped past the very rule meant to catch it.
        var result = await ValidateAsync(
            MinimalValid.Replace("version: 1.0.0", "version: 1.2.3.4", StringComparison.Ordinal),
            strict: true,
            previousApiVersion: "1.2.3.4");

        result.Diagnostics.ShouldContain(d =>
            d.Severity == DiagnosticSeverity.Error && d.Message.Contains("will not take the same version twice", StringComparison.Ordinal));
    }
}
