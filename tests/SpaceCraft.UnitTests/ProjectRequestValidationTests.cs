using FluentValidation.TestHelper;
using SpaceCraft.Api.Features.Organizations.Requests;
using SpaceCraft.Api.Features.Projects.Requests;
using SpaceCraft.Domain.Common;
using SpaceCraft.Domain.Projects;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// The rules the organization and project request records carry: that each refuses what its
/// handler used to refuse, in the handler's own words and under the property's name, and
/// that a request an unsupported language arrives in is answered rather than thrown on.
/// </summary>
public sealed class ProjectRequestValidationTests
{
    private const string SlugRule = "A slug may contain lowercase letters, digits and single hyphens only.";

    private const string AutoPublishRule =
        "A target that publishes nowhere cannot publish automatically. Choose a "
        + "credential, or leave automatic publishing off.";

    private static readonly Guid Credential = Guid.CreateVersion7();

    // Organizations

    [Fact]
    public void A_well_formed_organization_passes()
    {
        new CreateOrganizationRequest.Validator()
            .TestValidate(new CreateOrganizationRequest("Acme Corp", "acme"))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void An_organization_name_outside_the_naming_rule_is_refused()
    {
        new CreateOrganizationRequest.Validator()
            .TestValidate(new CreateOrganizationRequest("Café/Ürün 2.0", null))
            .ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage(ResourceName.Rule);
    }

    [Fact]
    public void An_organization_slug_with_capitals_is_refused()
    {
        new CreateOrganizationRequest.Validator()
            .TestValidate(new CreateOrganizationRequest("Acme Corp", "Acme"))
            .ShouldHaveValidationErrorFor(request => request.Slug)
            .WithErrorMessage(SlugRule);
    }

    [Fact]
    public void An_organization_slug_is_trimmed_before_it_is_judged()
    {
        new CreateOrganizationRequest.Validator()
            .TestValidate(new CreateOrganizationRequest("Acme Corp", "  acme  "))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void A_blank_organization_slug_is_left_for_the_handler_to_derive()
    {
        var validator = new CreateOrganizationRequest.Validator();

        validator.TestValidate(new CreateOrganizationRequest("Acme Corp", null)).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new CreateOrganizationRequest("Acme Corp", "   ")).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void An_organization_rename_to_a_well_formed_name_passes()
    {
        new RenameRequest.Validator()
            .TestValidate(new RenameRequest("Acme Holdings"))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void An_organization_rename_to_a_blank_name_is_refused()
    {
        new RenameRequest.Validator()
            .TestValidate(new RenameRequest("   "))
            .ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage(ResourceName.Rule);
    }

    // Projects

    [Fact]
    public void A_well_formed_project_passes()
    {
        new CreateProjectRequest.Validator()
            .TestValidate(new CreateProjectRequest("Billing API", "billing-api", null))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void A_project_name_outside_the_naming_rule_is_refused()
    {
        new CreateProjectRequest.Validator()
            .TestValidate(new CreateProjectRequest("-billing", null, true))
            .ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage(ResourceName.Rule);
    }

    [Fact]
    public void A_project_slug_with_a_double_hyphen_is_refused()
    {
        new CreateProjectRequest.Validator()
            .TestValidate(new CreateProjectRequest("Billing API", "billing--api", null))
            .ShouldHaveValidationErrorFor(request => request.Slug)
            .WithErrorMessage(SlugRule);
    }

    [Fact]
    public void A_project_slug_is_trimmed_before_it_is_judged()
    {
        new CreateProjectRequest.Validator()
            .TestValidate(new CreateProjectRequest("Billing API", " billing-api ", null))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void A_blank_project_slug_is_left_for_the_handler_to_derive()
    {
        var validator = new CreateProjectRequest.Validator();

        validator.TestValidate(new CreateProjectRequest("Billing API", null, null)).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new CreateProjectRequest("Billing API", "   ", null)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Every_failing_property_of_a_project_is_reported_at_once()
    {
        var result = new CreateProjectRequest.Validator()
            .TestValidate(new CreateProjectRequest("", "Billing API", null));

        result.ShouldHaveValidationErrorFor(request => request.Name).WithErrorMessage(ResourceName.Rule);
        result.ShouldHaveValidationErrorFor(request => request.Slug).WithErrorMessage(SlugRule);
        result.Errors.Count.ShouldBe(2);
    }

    [Fact]
    public void A_project_rename_to_a_well_formed_name_passes()
    {
        new RenameProjectRequest.Validator()
            .TestValidate(new RenameProjectRequest("Billing API v2"))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void A_project_rename_to_a_name_that_is_too_long_is_refused()
    {
        new RenameProjectRequest.Validator()
            .TestValidate(new RenameProjectRequest(new string('a', ResourceName.MaxLength + 1)))
            .ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage(ResourceName.Rule);
    }

    // Adding a target

    private static AddTargetRequest Target(
        SdkLanguage language = SdkLanguage.TypeScript,
        string packageName = "@acme/billing-sdk",
        RegistryKind? registry = null,
        bool autoPublish = false,
        Guid? credential = null) =>
        new(language, packageName, registry, autoPublish, credential);

    [Fact]
    public void A_typescript_target_passes()
    {
        new AddTargetRequest.Validator()
            .TestValidate(Target())
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void An_unsupported_language_is_reported_and_nothing_below_it_is_judged()
    {
        // Go has no default registry — asking for one throws — and everything else about this
        // request is wrong too. Only the language is reported, in the handler's words.
        var result = new AddTargetRequest.Validator()
            .TestValidate(Target(SdkLanguage.Go, packageName: null!, RegistryKind.Npm, autoPublish: true));

        result.ShouldHaveValidationErrorFor(request => request.Language)
            .WithErrorMessage("Go is not available yet. TypeScript is currently supported.")
            .Only();
    }

    [Fact]
    public void A_registry_the_language_does_not_publish_to_is_refused()
    {
        new AddTargetRequest.Validator()
            .TestValidate(Target(registry: RegistryKind.NuGet))
            .ShouldHaveValidationErrorFor(request => request.Registry)
            .WithErrorMessage(
                "TypeScript publishes to Npm, not to NuGet. Point the credential at your own Npm "
                + "server if it is not the public one.");
    }

    [Fact]
    public void Naming_the_language_s_own_registry_passes()
    {
        new AddTargetRequest.Validator()
            .TestValidate(Target(registry: RegistryKind.Npm))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void A_missing_package_name_is_reported_rather_than_thrown_on()
    {
        new AddTargetRequest.Validator()
            .TestValidate(Target(packageName: null!))
            .ShouldHaveValidationErrorFor(request => request.PackageName)
            .WithErrorMessage("'' is not a valid package name for Npm.")
            .Only();
    }

    [Fact]
    public void A_package_name_the_registry_would_refuse_is_refused_in_the_domain_s_words()
    {
        new AddTargetRequest.Validator()
            .TestValidate(Target(packageName: "Billing SDK"))
            .ShouldHaveValidationErrorFor(request => request.PackageName)
            .WithErrorMessage("'Billing SDK' is not a valid package name for Npm.");
    }

    [Fact]
    public void A_package_name_is_trimmed_before_it_is_judged()
    {
        new AddTargetRequest.Validator()
            .TestValidate(Target(packageName: " billing-sdk "))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Automatic_publishing_without_a_credential_is_refused_under_the_property_s_name()
    {
        // The handler keyed this "autoPublish"; the rule reports the property as it is spelled.
        new AddTargetRequest.Validator()
            .TestValidate(Target(autoPublish: true))
            .ShouldHaveValidationErrorFor("AutoPublish")
            .WithErrorMessage(AutoPublishRule)
            .Only();
    }

    [Fact]
    public void Automatic_publishing_with_a_credential_passes()
    {
        new AddTargetRequest.Validator()
            .TestValidate(Target(autoPublish: true, credential: Credential))
            .ShouldNotHaveAnyValidationErrors();
    }

    // Updating a target

    [Fact]
    public void An_update_that_leaves_automatic_publishing_off_passes()
    {
        new UpdateTargetRequest.Validator()
            .TestValidate(new UpdateTargetRequest("@acme/billing-sdk", false))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void An_update_turning_automatic_publishing_on_without_a_credential_is_refused()
    {
        new UpdateTargetRequest.Validator()
            .TestValidate(new UpdateTargetRequest("@acme/billing-sdk", true))
            .ShouldHaveValidationErrorFor("AutoPublish")
            .WithErrorMessage(AutoPublishRule)
            .Only();
    }

    [Fact]
    public void An_update_s_package_name_is_the_handler_s_to_judge()
    {
        // Its rule is the stored target's registry's, which only the handler can see.
        new UpdateTargetRequest.Validator()
            .TestValidate(new UpdateTargetRequest(null!, false))
            .ShouldNotHaveAnyValidationErrors();
    }
}
