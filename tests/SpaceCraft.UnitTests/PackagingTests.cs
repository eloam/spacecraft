using SpaceCraft.Pipeline.Building;
using SpaceCraft.Pipeline.Generation;
using SpaceCraft.Pipeline.Publishing;
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
    [InlineData("npm ERR! You cannot publish over the previously published versions: 1.0.0.")]
    [InlineData("npm ERR! code EPUBLISHCONFLICT")]
    public void An_existing_version_is_recognised_as_a_conflict(string output) =>
        NpmSdkPublisher.IsVersionConflict(output).ShouldBeTrue();

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
}
