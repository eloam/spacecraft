using FluentValidation.TestHelper;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SpaceCraft.Api.Features.Credentials.Requests;
using SpaceCraft.Domain.Projects;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// What the credentials endpoint refuses before a token is ever encrypted — and that each
/// refusal says what the handler used to say, in the words the form shows beside the field.
/// </summary>
public sealed class CredentialRequestValidationTests
{
    private static CreateCredentialRequest.Validator InDevelopment => new(new HostEnvironment(Environments.Development));

    private static CreateCredentialRequest.Validator InProduction => new(new HostEnvironment(Environments.Production));

    private static CreateCredentialRequest Request(
        RegistryKind registry = RegistryKind.Npm,
        string label = "npm automation token",
        string token = "npm_0123456789abcdef",
        string? registryUrl = null) =>
        new(registry, label, token, Username: null, ExpiresAt: null, registryUrl);

    [Fact]
    public void An_undefined_registry_is_the_only_thing_reported()
    {
        // Everything else is wrong too. None of it is judged, because judging it means asking
        // the domain for a registry's address, and the domain throws on one it has never heard of.
        var request = Request(registry: (RegistryKind)99, label: "", token: "");

        var result = Should.NotThrow(() => InProduction.TestValidate(request));

        result.ShouldHaveValidationErrorFor(r => r.Registry)
            .WithErrorMessage("'99' is not a registry.")
            .Only();
    }

    [Fact]
    public void A_blank_label_is_refused() =>
        InProduction.TestValidate(Request(label: "   "))
            .ShouldHaveValidationErrorFor(r => r.Label)
            .WithErrorMessage("Give the credential a label so it can be told apart later.");

    [Fact]
    public void Maven_without_an_address_is_refused() =>
        InProduction.TestValidate(Request(registry: RegistryKind.Maven, token: "a-maven-deploy-token"))
            .ShouldHaveValidationErrorFor(r => r.RegistryUrl)
            .WithErrorMessage(
                "Maven has no address that can be guessed. Give the address that accepts a publish "
                + "— for Maven, the staging or repository URL your deployment uses.");

    [Fact]
    public void A_loopback_address_is_refused_outside_development() =>
        InProduction.TestValidate(Request(registryUrl: "http://127.0.0.1:4873"))
            .ShouldHaveValidationErrorFor(r => r.RegistryUrl)
            .WithErrorMessage(
                "'http://127.0.0.1:4873' is a private or loopback address. A registry has to be "
                + "reachable from where the pipeline runs, and this platform does not publish to "
                + "addresses inside its own network.");

    [Fact]
    public void A_loopback_address_is_allowed_in_development() =>
        InDevelopment.TestValidate(Request(registryUrl: "http://127.0.0.1:4873"))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void A_malformed_address_is_refused_and_the_public_one_offered() =>
        InProduction.TestValidate(Request(registryUrl: "not a url"))
            .ShouldHaveValidationErrorFor(r => r.RegistryUrl)
            .WithErrorMessage(
                "'not a url' is not an absolute http or https URL. "
                + "Leave it empty to publish to https://registry.npmjs.org.");

    [Fact]
    public void A_malformed_address_for_a_registry_without_a_public_one_offers_nothing() =>
        InProduction.TestValidate(Request(registry: RegistryKind.Maven, token: "a-maven-deploy-token", registryUrl: "not a url"))
            .ShouldHaveValidationErrorFor(r => r.RegistryUrl)
            .WithErrorMessage("'not a url' is not an absolute http or https URL.");

    [Fact]
    public void A_blank_token_is_refused() =>
        InProduction.TestValidate(Request(token: " "))
            .ShouldHaveValidationErrorFor(r => r.Token)
            .WithErrorMessage("Provide the registry token.");

    [Fact]
    public void A_token_for_npmjs_that_is_not_shaped_like_one_is_refused() =>
        InProduction.TestValidate(Request(token: "abcdefghijklmnop"))
            .ShouldHaveValidationErrorFor(r => r.Token)
            .WithErrorMessage(
                "'abcd…' does not look like an npm token. Use an automation token (npm_...): "
                + "granular or classic publish tokens with 2FA enabled will fail on an unattended publish.");

    [Fact]
    public void A_private_npm_registry_mints_its_own_tokens() =>
        InProduction.TestValidate(Request(token: "abcdefghijklmnop", registryUrl: "https://nexus.acme.test/repository/npm/"))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void A_token_too_short_to_be_one_is_refused() =>
        InProduction.TestValidate(Request(token: "npm_1"))
            .ShouldHaveValidationErrorFor(r => r.Token)
            .WithErrorMessage(
                "That token is 5 characters. A registry token is far longer — check that the whole value was pasted.");

    [Fact]
    public void A_complete_request_passes() =>
        InProduction.TestValidate(Request()).ShouldNotHaveAnyValidationErrors();

    /// <summary>A host that knows nothing but which environment it runs as.</summary>
    private sealed class HostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "SpaceCraft.UnitTests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
