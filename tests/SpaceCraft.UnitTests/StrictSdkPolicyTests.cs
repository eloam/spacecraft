using System.Text;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Infrastructure.OpenApi;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// The strict checks answer "can a usable SDK come out of this", which is a different
/// question from "is this valid OpenAPI". Every case here is valid OpenAPI.
/// </summary>
public class StrictSdkPolicyTests
{
    private static readonly OpenApiSpecValidator Validator = new(NullLogger<OpenApiSpecValidator>.Instance);

    private static Task<SpecValidationResult> StrictAsync(string yaml, string? previousApiVersion = null) =>
        Validator.ValidateAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(yaml)), "openapi.yaml", strict: true,
            previousApiVersion, TestContext.Current.CancellationToken);

    private static Task<SpecValidationResult> LenientAsync(string yaml, string? previousApiVersion = null) =>
        Validator.ValidateAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(yaml)), "openapi.yaml", strict: false,
            previousApiVersion, TestContext.Current.CancellationToken);

    /// <summary>A document that passes every strict check, for tests to vary one thing from.</summary>
    private const string Clean = """
        openapi: 3.0.3
        info:
          title: Example API
          version: 1.0.0
          description: An example.
          contact:
            name: Example
          license:
            name: MIT
        servers:
          - url: https://api.example.com
        tags:
          - name: Things
        security:
          - bearer: []
        components:
          securitySchemes:
            bearer:
              type: http
              scheme: bearer
          schemas:
            Thing:
              type: object
              properties:
                id:
                  type: string
        paths:
          /things:
            get:
              operationId: listThings
              summary: Lists things.
              tags: [Things]
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Thing'
        """;

    private static string Without(string section) => Clean.Replace(section, "");

    [Fact]
    public async Task The_clean_document_raises_nothing()
    {
        var result = await StrictAsync(Clean);

        result.Diagnostics.ShouldBeEmpty();
        result.Status.ShouldBe(SpecValidationStatus.Valid);
    }

    [Fact]
    public async Task Strict_is_off_by_default_so_nothing_here_fires_without_it()
    {
        // Same document as the base-URL test below, which strict rejects.
        var result = await LenientAsync(Without("""
            servers:
              - url: https://api.example.com

            """));

        result.ErrorCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_document_with_no_servers_cannot_produce_a_client()
    {
        var result = await StrictAsync(Without("""
            servers:
              - url: https://api.example.com

            """));

        result.Status.ShouldBe(SpecValidationStatus.Invalid);
        result.Diagnostics.ShouldContain(d =>
            d.Severity == DiagnosticSeverity.Error && d.Message.Contains("no servers"));
    }

    [Fact]
    public async Task A_relative_server_url_is_no_better_than_none()
    {
        var result = await StrictAsync(Clean.Replace("https://api.example.com", "/"));

        result.Status.ShouldBe(SpecValidationStatus.Invalid);
        result.Diagnostics.ShouldContain(d => d.Message.Contains("not an absolute http(s) URL"));
    }

    [Fact]
    public async Task A_server_variable_without_a_default_cannot_be_resolved()
    {
        var result = await StrictAsync(Clean.Replace(
            "  - url: https://api.example.com",
            "  - url: https://{region}.example.com"));

        result.Status.ShouldBe(SpecValidationStatus.Invalid);
        result.Diagnostics.ShouldContain(d => d.Message.Contains("has no default"));
    }

    /// <summary>
    /// Generated against Kiota 1.34.1: operationId "/identity/confirm-email" produced
    /// Identity/ConfirmEmail/ConfirmEmailRequestBuilder with a post() method — named from the
    /// path, exactly as it would have been with no operationId at all.
    /// </summary>
    [Fact]
    public async Task An_unusable_operationId_costs_nothing_and_is_not_reported()
    {
        var result = await StrictAsync(Clean.Replace("listThings", "/things/list"));

        result.Diagnostics.ShouldBeEmpty();
    }

    /// <summary>
    /// Generated against Kiota 1.34.1: list_things and listThings on one path produced get()
    /// and post() on a single request builder. There is no collision to report.
    /// </summary>
    [Fact]
    public async Task Two_operationIds_that_look_alike_do_not_collide()
    {
        var result = await StrictAsync("""
            openapi: 3.0.3
            info:
              title: Example API
              version: 1.0.0
              description: An example.
              contact: { name: Example }
              license: { name: MIT }
            servers:
              - url: https://api.example.com
            tags:
              - name: Things
            security:
              - bearer: []
            components:
              securitySchemes:
                bearer:
                  type: http
                  scheme: bearer
            paths:
              /things:
                get:
                  operationId: list_things
                  summary: Lists things.
                  tags: [Things]
                  responses: { '204': { description: ok } }
                post:
                  operationId: listThings
                  summary: Adds a thing.
                  tags: [Things]
                  responses: { '204': { description: ok } }
            """);

        result.Diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_GET_with_a_request_body_cannot_be_sent_at_all()
    {
        // fetch rejects it, so the generated TypeScript client cannot make this call.
        var result = await StrictAsync(Clean.Replace("""
                  tags: [Things]
            """, """
                  tags: [Things]
                  requestBody:
                    content:
                      application/json:
                        schema:
                          $ref: '#/components/schemas/Thing'
            """));

        result.Status.ShouldBe(SpecValidationStatus.Invalid);
        result.Diagnostics.ShouldContain(d =>
            d.Severity == DiagnosticSeverity.Error && d.Message.Contains("the generator drops the body"));
    }

    [Fact]
    public async Task A_path_parameter_that_is_never_declared_is_an_error()
    {
        var result = await StrictAsync(Clean.Replace("  /things:", "  /things/{id}:"));

        result.Status.ShouldBe(SpecValidationStatus.Invalid);
        result.Diagnostics.ShouldContain(d => d.Message.Contains("{id}"));
    }

    [Fact]
    public async Task A_security_scheme_that_is_required_but_undefined_is_an_error()
    {
        var result = await StrictAsync(Clean.Replace("  - bearer: []", "  - oauth: []"));

        result.Status.ShouldBe(SpecValidationStatus.Invalid);
        result.Diagnostics.ShouldContain(d => d.Message.Contains("not defined in components.securitySchemes"));
    }

    /// <summary>
    /// Kiota omits it in silence: the generated model simply has no field for it. Bad, but
    /// the client still works — so a warning, not an error.
    /// </summary>
    [Fact]
    public async Task A_required_property_that_does_not_exist_leaves_the_model_without_it()
    {
        var result = await StrictAsync(Clean.Replace("""
                Thing:
                  type: object
                  properties:
                    id:
                      type: string
            """, """
                Thing:
                  type: object
                  required: [name]
                  properties:
                    id:
                      type: string
            """));

        result.Status.ShouldBe(SpecValidationStatus.ValidWithWarnings);
        result.Diagnostics.ShouldContain(d =>
            d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("'name' is required"));
    }

    [Fact]
    public async Task A_version_registries_cannot_take_is_caught_at_upload()
    {
        // Without this the run fails after a build, which is a much later place to learn it.
        // The rule moved out of strict mode and now runs on every upload: it is an error only
        // when no registry at all would take the version, which "spring-release" is.
        var result = await StrictAsync(Clean.Replace("version: 1.0.0", "version: spring-release"));

        result.Status.ShouldBe(SpecValidationStatus.Invalid);
        result.Diagnostics.ShouldContain(d => d.Message.Contains("cannot be published to any registry"));
    }

    [Fact]
    public async Task A_version_that_only_needs_normalising_is_a_note()
    {
        var result = await StrictAsync(Clean.Replace("version: 1.0.0", "version: \"1.0\""));

        // Every registry takes 1.0, but not all under the same string: npm completes it and
        // Maven does not, so the note names who rewrites it rather than claiming one answer.
        result.Status.ShouldBe(SpecValidationStatus.Valid);
        result.Diagnostics.ShouldContain(d =>
            d.Severity == DiagnosticSeverity.Information
            && d.Message.Contains("published as '1.0.0' to Npm"));
    }

    [Fact]
    public async Task A_response_that_describes_no_body_is_a_warning()
    {
        var result = await StrictAsync(Clean.Replace("""
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            $ref: '#/components/schemas/Thing'
            """, """
                    '200':
                      description: ok
            """));

        result.Status.ShouldBe(SpecValidationStatus.ValidWithWarnings);
        result.Diagnostics.ShouldContain(d => d.Message.Contains("raw ArrayBuffer the caller has to parse"));
    }

    [Fact]
    public async Task An_inline_response_schema_is_a_note_about_the_generated_name()
    {
        var result = await StrictAsync(Clean.Replace("""
                          schema:
                            $ref: '#/components/schemas/Thing'
            """, """
                          schema:
                            type: object
                            properties:
                              id:
                                type: string
            """));

        result.Diagnostics.ShouldContain(d =>
            d.Severity == DiagnosticSeverity.Information && d.Message.Contains("invents a model name"));
    }

    [Fact]
    public async Task A_schema_nothing_references_is_dead_weight_in_the_package()
    {
        var result = await StrictAsync(Clean.Replace("""
            Thing:
                  type: object
            """, """
            Orphan:
                  type: object
                Thing:
                  type: object
            """));

        result.Diagnostics.ShouldContain(d => d.Message.Contains("'Orphan' is never referenced"));
    }

    [Fact]
    public async Task Strict_turns_the_parsers_own_warnings_into_errors()
    {
        // A $ref pointing at nothing: the parser reports it, and generation cannot survive it.
        var broken = Clean.Replace("#/components/schemas/Thing", "#/components/schemas/Missing");

        (await LenientAsync(broken)).Status.ShouldBe(SpecValidationStatus.ValidWithWarnings);
        (await StrictAsync(broken)).Status.ShouldBe(SpecValidationStatus.Invalid);
    }

    [Fact]
    public async Task Two_parameters_that_become_one_argument_collide()
    {
        var result = await StrictAsync(Clean.Replace("""
                  tags: [Things]
            """, """
                  tags: [Things]
                  parameters:
                    - name: client-id
                      in: query
                      schema: { type: string }
                    - name: clientId
                      in: query
                      schema: { type: string }
            """));

        result.Status.ShouldBe(SpecValidationStatus.ValidWithWarnings);
        result.Diagnostics.ShouldContain(d =>
            d.Message.Contains("differ only by punctuation") && d.Message.Contains("argument names"));
    }

    /// <summary>
    /// Microsoft.OpenApi merges an operation's parameters up into its path item, so reading
    /// both lists counted every one of them twice. On a real Keycloak path that produced
    /// eleven "declared twice" errors against parameters declared exactly once.
    /// </summary>
    [Fact]
    public async Task Parameters_are_counted_once_even_though_the_parser_lists_them_twice()
    {
        var result = await StrictAsync(Clean.Replace("""
                  summary: Lists things.
            """, """
                  summary: Lists things.
                  parameters:
                    - name: first
                      in: query
                      schema: { type: integer }
                    - name: max
                      in: query
                      schema: { type: integer }
            """));

        result.Diagnostics.ShouldBeEmpty();
    }

    /// <summary>
    /// Generated against Kiota 1.34.1: TypeScript keeps user_id and userId as two keys, but
    /// C# emits "public string? UserId" twice, which does not compile. Language-dependent,
    /// so a warning.
    /// </summary>
    [Fact]
    public async Task Two_properties_that_differ_only_by_punctuation_are_a_warning()
    {
        var result = await StrictAsync(Clean.Replace("""
                  properties:
                    id:
                      type: string
            """, """
                  properties:
                    user_id:
                      type: string
                    userId:
                      type: string
            """));

        result.Status.ShouldBe(SpecValidationStatus.ValidWithWarnings);
        result.Diagnostics.ShouldContain(d => d.Message.Contains("such as C#, would emit one member twice"));
    }

    [Fact]
    public async Task A_wildcard_media_type_leaves_no_serialiser_to_pick()
    {
        // Quoted: an unquoted "*" opens a YAML alias, not a media type.
        var result = await StrictAsync(Clean.Replace("application/json:", "\"*/*\":"));

        result.Diagnostics.ShouldContain(d => d.Message.Contains("no serialiser to pick"));
    }

    [Fact]
    public async Task A_tag_that_groups_operations_without_being_declared_is_a_note()
    {
        var result = await StrictAsync(Clean.Replace("""
            tags:
              - name: Things
            """, ""));

        result.Diagnostics.ShouldContain(d =>
            d.Severity == DiagnosticSeverity.Information && d.Message.Contains("Tag 'Things'"));
    }

    [Fact]
    public async Task A_title_with_nothing_to_name_a_package_from_is_an_error()
    {
        var result = await StrictAsync(Clean.Replace("title: Example API", "title: \"--- ***\""));

        result.Status.ShouldBe(SpecValidationStatus.Invalid);
        result.Diagnostics.ShouldContain(d => d.Message.Contains("nothing left to build a package name from"));
    }

    /// <summary>
    /// Guards against re-adding a check the parser already performs. Each of these produces
    /// exactly one finding, and it is the parser's — two findings on one problem is how a
    /// findings list stops being read.
    /// </summary>
    [Theory]
    [InlineData("title: Example API", "", "'title'")]
    [InlineData("version: 1.0.0", "", "'version'")]
    [InlineData("  - url: https://api.example.com", "  - description: nowhere", "'url'")]
    public async Task The_parser_reports_these_alone(string find, string replace, string fragment)
    {
        var result = await StrictAsync(Clean.Replace(find, replace));

        result.Diagnostics.Count(d => d.Message.Contains(fragment)).ShouldBe(1);
    }

    [Fact]
    public async Task Paths_that_collide_once_templated_are_left_to_the_parser()
    {
        // PathMustBeUnique already reports this as an error, so there is no rule of ours here.
        var result = await StrictAsync(Clean.Replace("""
              /things:
            """, """
              /things/{id}:
                get:
                  operationId: getThing
                  tags: [Things]
                  responses:
                    '204':
                      description: ok
              /things/{thingId}:
                get:
                  operationId: getThingAgain
                  tags: [Things]
                  responses:
                    '204':
                      description: ok
              /things:
            """));

        result.Status.ShouldBe(SpecValidationStatus.Invalid);
        result.Diagnostics.Count(d => d.Message.Contains("MUST be unique")).ShouldBe(1);
    }

    [Fact]
    public async Task Only_the_first_of_several_servers_reaches_the_client()
    {
        var result = await StrictAsync(Clean.Replace(
            "  - url: https://api.example.com",
            "  - url: https://api.example.com\n  - url: https://staging.example.com"));

        result.Diagnostics.ShouldContain(d =>
            d.Severity == DiagnosticSeverity.Information && d.Message.Contains("built against the first"));
    }

    [Fact]
    public async Task A_cookie_parameter_is_flagged_as_poorly_supported()
    {
        var result = await StrictAsync(Clean.Replace("""
                  tags: [Things]
            """, """
                  tags: [Things]
                  parameters:
                    - name: session
                      in: cookie
                      schema: { type: string }
            """));

        result.Diagnostics.ShouldContain(d => d.Message.Contains("sent in a cookie"));
    }

    [Fact]
    public async Task An_operation_with_no_prose_ships_an_undocumented_method()
    {
        var result = await StrictAsync(Clean.Replace("summary: Lists things.", ""));

        // Strip the one line of prose the clean fixture carries.
        result.Diagnostics.ShouldContain(d =>
            d.Severity == DiagnosticSeverity.Information
            && d.Message.Contains("no documentation at all"));
    }

    [Fact]
    public async Task Binary_under_a_JSON_media_type_is_a_contradiction()
    {
        // Keycloak's realm-import endpoints do exactly this.
        var result = await StrictAsync("""
            openapi: 3.0.3
            info:
              title: Example API
              version: 1.0.0
              description: An example.
              contact: { name: Example }
              license: { name: MIT }
            servers:
              - url: https://api.example.com
            paths:
              /import:
                post:
                  operationId: import
                  summary: Imports a realm.
                  requestBody:
                    content:
                      application/json:
                        schema: { type: string, format: binary }
                  responses:
                    '204':
                      description: ok
            """);

        result.Diagnostics.ShouldContain(d => d.Message.Contains("binary is a byte stream"));
    }

    [Fact]
    public async Task A_scheme_the_client_cannot_wire_is_flagged()
    {
        var result = await StrictAsync(Clean.Replace("""
                bearer:
                  type: http
                  scheme: bearer
            """, """
                bearer:
                  type: openIdConnect
                  openIdConnectUrl: https://id.example.com/.well-known/openid-configuration
            """));

        result.Diagnostics.ShouldContain(d => d.Message.Contains("cannot wire on its own"));
    }

    [Fact]
    public async Task Republishing_the_same_version_is_an_error()
    {
        // Registries are append-only: 1.0.0 cannot be published twice.
        var result = await StrictAsync(Clean, previousApiVersion: "1.0.0");

        result.Status.ShouldBe(SpecValidationStatus.Invalid);
        result.Diagnostics.ShouldContain(d => d.Message.Contains("will not take the same version twice"));
    }

    [Fact]
    public async Task Going_backwards_is_an_error()
    {
        var result = await StrictAsync(Clean, previousApiVersion: "2.3.0");

        result.Status.ShouldBe(SpecValidationStatus.Invalid);
        result.Diagnostics.ShouldContain(d => d.Message.Contains("Releases only move forward"));
    }

    [Fact]
    public async Task Moving_forward_raises_nothing() =>
        (await StrictAsync(Clean, previousApiVersion: "0.9.0")).Diagnostics.ShouldBeEmpty();

    [Fact]
    public async Task Ten_follows_nine_rather_than_sorting_before_it() =>
        // The comparison is numeric; as strings "1.10.0" would come before "1.9.0".
        (await StrictAsync(Clean.Replace("version: 1.0.0", "version: 1.10.0"), previousApiVersion: "1.9.0"))
            .Diagnostics.ShouldBeEmpty();

    [Fact]
    public async Task The_first_document_of_a_project_has_nothing_to_move_past() =>
        (await StrictAsync(Clean, previousApiVersion: null)).Diagnostics.ShouldBeEmpty();

    [Fact]
    public async Task Version_history_is_a_strict_concern_only() =>
        (await LenientAsync(Clean, previousApiVersion: "2.3.0")).ErrorCount.ShouldBe(0);
}
