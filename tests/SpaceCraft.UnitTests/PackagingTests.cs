using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpaceCraft.Pipeline;
using SpaceCraft.Pipeline.Building;
using SpaceCraft.Pipeline.Generation;
using SpaceCraft.Pipeline.Publishing;
using SpaceCraft.Pipeline.Sandbox;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

public class ClientNamingTests
{
    [Theory]
    [InlineData("Pet Store API", "@acme/petstore-sdk", "PetStoreApiClient")]
    [InlineData("Keycloak Admin REST API", "@x/y", "KeycloakAdminRestApiClient")]
    [InlineData(null, "@acme/billing-sdk", "BillingSdkClient")]
    [InlineData("Already A Client", "@x/y", "AlreadyAClient")]
    public void Client_names_are_valid_identifiers(string? apiTitle, string packageName, string expected) =>
        KiotaSdkGenerator.DeriveClientName(apiTitle, packageName).ShouldBe(expected);

    [Fact]
    public void A_title_starting_with_a_digit_is_prefixed_so_it_stays_a_valid_identifier() =>
        KiotaSdkGenerator.DeriveClientName("2fa Service", "@x/y").ShouldBe("Api2faServiceClient");

    [Fact]
    public void A_title_with_no_usable_characters_falls_back() =>
        KiotaSdkGenerator.DeriveClientName("!!!", "!!!").ShouldBe("ApiClient");

    [Theory]
    [InlineData("@acme/billing-sdk", "acme.billing_sdk")]
    [InlineData("plain-package", "plain_package")]
    public void Namespaces_are_derived_from_the_package_name(string packageName, string expected) =>
        KiotaSdkGenerator.NamespaceFor(packageName).ShouldBe(expected);
}

public class TarballNamingTests
{
    [Fact]
    public void The_tarball_name_is_read_from_npm_output() =>
        NpmSdkBuilder.ExtractTarballName("npm notice filename: acme-petstore-sdk-1.2.0.tgz\nacme-petstore-sdk-1.2.0.tgz")
            .ShouldBe("acme-petstore-sdk-1.2.0.tgz");

    [Fact]
    public void No_tarball_in_the_output_yields_null() =>
        NpmSdkBuilder.ExtractTarballName("nothing here").ShouldBeNull();

    [Theory]
    [InlineData("@acme/billing-sdk", "1.2.0", "acme-billing-sdk-1.2.0.tgz")]
    [InlineData("plain", "0.1.0", "plain-0.1.0.tgz")]
    public void The_conventional_name_matches_npms_own(string packageName, string version, string expected) =>
        NpmSdkBuilder.ConventionalTarballName(packageName, version).ShouldBe(expected);
}

public class NpmPublisherTests
{
    [Theory]
    // npmjs, modern and legacy wording.
    [InlineData("npm ERR! You cannot publish over the previously published versions: 1.0.0.")]
    [InlineData("npm ERR! code EPUBLISHCONFLICT")]
    // Verdaccio answers 409 with its own phrasing. Reachable by anyone now that the
    // destination comes from the credential rather than from configuration.
    [InlineData("npm error 409 Conflict - PUT http://verdaccio:4873/billing-sdk - this package is already present")]
    [InlineData("Nexus: version already exists")]
    [InlineData("Artifactory: release artifact cannot be republished")]
    public void An_existing_version_is_recognised_as_a_conflict(string output) =>
        NpmSdkPublisher.IsVersionConflict(output).ShouldBeTrue();

    [Theory]
    [InlineData("npm ERR! 404 Not Found - PUT http://verdaccio:4873/billing-sdk")]
    [InlineData("npm error code ENEEDAUTH\nnpm error need auth This command requires you to be logged in")]
    [InlineData("npm ERR! 500 Internal Server Error")]
    public void A_failure_that_is_not_a_conflict_stays_a_failure(string output) =>
        // Broad matching is safe only while it stays away from these: reading a refusal as
        // "already published" would mark a package as shipped when nothing was.
        NpmSdkPublisher.IsVersionConflict(output).ShouldBeFalse();

    [Fact]
    public void An_authentication_failure_is_not_a_version_conflict() =>
        // Misreading this as "already published" would silently mark a failed publish as done.
        NpmSdkPublisher.IsVersionConflict("npm ERR! code E401\nnpm ERR! Incorrect or missing password.")
            .ShouldBeFalse();

    [Theory]
    [InlineData("https://registry.npmjs.org", "//registry.npmjs.org/:_authToken")]
    [InlineData("http://localhost:4873", "//localhost:4873/:_authToken")]
    [InlineData("https://npm.pkg.github.com/", "//npm.pkg.github.com/:_authToken")]
    public void The_npmrc_auth_key_matches_the_registry(string registry, string expected) =>
        NpmSdkPublisher.AuthKeyFor(registry).ShouldBe(expected);

    [Fact]
    public async Task Npm_is_pointed_at_the_npmrc_that_was_written()
    {
        // The variable once named /workspace/<artifact>/.npmrc, a bind-mount path from a
        // sandbox the runner no longer has; npm found the file anyway, as the project config
        // of the directory the publish runs in. It has to be the path the file is written
        // to, so that the credential does not hang on where the command runs — and the file
        // has to exist when npm runs, since it is deleted afterwards.
        var workspace = Directory.CreateTempSubdirectory("spacecraft-publish-");

        try
        {
            Directory.CreateDirectory(Path.Combine(workspace.FullName, "artifact"));
            await File.WriteAllBytesAsync(
                Path.Combine(workspace.FullName, "artifact", "acme-billing-sdk-1.0.0.tgz"),
                [],
                TestContext.Current.CancellationToken);

            var runner = new SpyRunner();
            var publisher = new NpmSdkPublisher(
                runner, Options.Create(new PipelineOptions()), NullLogger<NpmSdkPublisher>.Instance);

            await publisher.PublishAsync(
                new PublishRequest
                {
                    WorkspaceRoot = workspace.FullName,
                    ArtifactDirectory = "artifact",
                    ArtifactFileName = "acme-billing-sdk-1.0.0.tgz",
                    PackageName = "@acme/billing-sdk",
                    Version = "1.0.0",
                    RegistryUrl = "https://registry.npmjs.org",
                    Token = "npm_token",
                },
                TestContext.Current.CancellationToken);

            var expected = Path.Combine(workspace.FullName, "artifact", ".npmrc");
            runner.Request.ShouldNotBeNull().Environment["npm_config_userconfig"].ShouldBe(expected);
            runner.NpmrcExistedWhenRun.ShouldBeTrue();
            File.Exists(expected).ShouldBeFalse("the file is removed once npm has run");
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    /// <summary>Records the request and whether the file it points npm at was there to read.</summary>
    private sealed class SpyRunner : ICommandRunner
    {
        public CommandRequest? Request { get; private set; }

        public bool NpmrcExistedWhenRun { get; private set; }

        public Task<CommandResult> RunAsync(CommandRequest request, CancellationToken ct = default)
        {
            Request = request;
            NpmrcExistedWhenRun = File.Exists(request.Environment["npm_config_userconfig"]);

            return Task.FromResult(new CommandResult
            {
                ExitCode = 0,
                Output = string.Empty,
                Duration = TimeSpan.Zero,
            });
        }
    }
}
