using SpaceCraft.Domain.Organizations;
using SpaceCraft.Domain.Projects;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

public class OrganizationTests
{
    private static Organization NewOrg(out Guid ownerId)
    {
        ownerId = Guid.CreateVersion7();
        return new Organization("Acme Corp", "acme-corp", ownerId);
    }

    [Fact]
    public void The_creator_becomes_the_owner()
    {
        var org = NewOrg(out var ownerId);

        org.Members.ShouldHaveSingleItem().UserId.ShouldBe(ownerId);
        org.Members.Single().Role.ShouldBe(OrganizationRole.Owner);
    }

    [Fact]
    public void A_user_cannot_be_added_twice()
    {
        var org = NewOrg(out var ownerId);

        Should.Throw<InvalidOperationException>(() => org.AddMember(ownerId, OrganizationRole.Admin));
    }

    [Fact]
    public void The_last_owner_cannot_be_removed()
    {
        // Otherwise the organization is left with nobody able to administer it.
        var org = NewOrg(out var ownerId);
        org.AddMember(Guid.CreateVersion7(), OrganizationRole.Admin);

        Should.Throw<InvalidOperationException>(() => org.RemoveMember(ownerId));
    }

    [Fact]
    public void An_owner_can_be_removed_once_another_owner_exists()
    {
        var org = NewOrg(out var ownerId);
        org.AddMember(Guid.CreateVersion7(), OrganizationRole.Owner);

        org.RemoveMember(ownerId);

        org.Members.Count.ShouldBe(1);
    }

    [Fact]
    public void Renaming_moves_the_slug_with_the_name()
    {
        var org = NewOrg(out _);

        org.Rename("Acme Corporation");

        org.Name.ShouldBe("Acme Corporation");
        org.Slug.ShouldBe("acme-corporation");
    }

    [Fact]
    public void A_blank_name_is_refused() =>
        Should.Throw<ArgumentException>(() => NewOrg(out _).Rename("   "));

    [Fact]
    public void A_name_that_would_not_slug_cleanly_is_refused() =>
        // Allowing it would produce an address the user never chose and cannot predict.
        Should.Throw<ArgumentException>(() => NewOrg(out _).Rename("Café/Ürün"));

    [Fact]
    public void Renaming_a_project_moves_its_slug_too()
    {
        var org = NewOrg(out _);
        var project = org.AddProject("Billing", "billing");

        org.RenameProject(project.Id, "Billing API");

        project.Name.ShouldBe("Billing API");
        project.Slug.ShouldBe("billing-api");
    }

    [Fact]
    public void A_rename_that_would_collide_with_a_sibling_is_refused()
    {
        // Project slugs are unique within an organization, and the slug is the URL: two
        // projects cannot share one.
        var org = NewOrg(out _);
        var first = org.AddProject("Billing", "billing");
        org.AddProject("Pet Store", "pet-store");

        Should.Throw<InvalidOperationException>(() => org.RenameProject(first.Id, "Pet Store"))
            .Message.ShouldContain("pet-store");
    }

    [Fact]
    public void Renaming_a_project_to_its_own_name_is_not_a_collision()
    {
        var org = NewOrg(out _);
        var project = org.AddProject("Billing", "billing");

        Should.NotThrow(() => org.RenameProject(project.Id, "Billing"));
    }

    [Fact]
    public void The_only_owner_cannot_be_demoted()
    {
        // Demoting the last owner strands the organization exactly as removing them would,
        // so both routes to that state have to be closed.
        var org = NewOrg(out var ownerId);
        org.AddMember(Guid.CreateVersion7(), OrganizationRole.Admin);

        Should.Throw<InvalidOperationException>(
            () => org.ChangeMemberRole(ownerId, OrganizationRole.Admin));
    }

    [Fact]
    public void An_owner_can_be_demoted_once_another_owner_exists()
    {
        var org = NewOrg(out var ownerId);
        org.AddMember(Guid.CreateVersion7(), OrganizationRole.Owner);

        org.ChangeMemberRole(ownerId, OrganizationRole.Member);

        org.Members.Single(m => m.UserId == ownerId).Role.ShouldBe(OrganizationRole.Member);
    }

    [Fact]
    public void Promoting_someone_is_always_allowed()
    {
        var org = NewOrg(out _);
        var member = org.AddMember(Guid.CreateVersion7(), OrganizationRole.Viewer);

        org.ChangeMemberRole(member.UserId, OrganizationRole.Admin);

        member.Role.ShouldBe(OrganizationRole.Admin);
    }

    [Fact]
    public void Changing_the_role_of_a_non_member_is_refused() =>
        Should.Throw<InvalidOperationException>(
            () => NewOrg(out _).ChangeMemberRole(Guid.CreateVersion7(), OrganizationRole.Admin));

    [Fact]
    public void Project_slugs_are_unique_within_an_organization()
    {
        var org = NewOrg(out _);
        org.AddProject("Billing", "billing");

        Should.Throw<InvalidOperationException>(() => org.AddProject("Billing v2", "billing"));
    }

    [Fact]
    public void An_invalid_slug_is_rejected_at_construction() =>
        Should.Throw<ArgumentException>(() => new Organization("Acme", "Not A Slug", Guid.CreateVersion7()));

    [Fact]
    public void A_name_that_would_not_slug_cleanly_is_refused_at_construction_too()
    {
        // The same rule Rename applies, from the start: the constructor used to accept any
        // non-blank name, so an aggregate could hold a name whose address it could never
        // derive, and only find out on the first rename.
        Should.Throw<ArgumentException>(() => new Organization("Café/Ürün", "cafe", Guid.CreateVersion7()));
        Should.Throw<ArgumentException>(() => NewOrg(out _).AddProject("Café/Ürün", "cafe"));
    }

    [Fact]
    public void The_name_is_stored_trimmed()
    {
        var org = new Organization("  Acme Corp  ", "acme-corp", Guid.CreateVersion7());

        org.Name.ShouldBe("Acme Corp");
    }

    [Fact]
    public void A_slug_chosen_explicitly_is_kept_until_the_first_rename()
    {
        // Accepted as given: a caller may choose an address that is not the derived one.
        var org = new Organization("Acme Corp", "acme", Guid.CreateVersion7());
        var project = org.AddProject("Billing API", "bill");

        org.Slug.ShouldBe("acme");
        project.Slug.ShouldBe("bill");

        // And re-derived by a rename, as the settings page says it will be. One rule for the
        // slug rather than a second one for slugs somebody once typed.
        org.Rename("Acme Corporation");
        org.RenameProject(project.Id, "Billing");

        org.Slug.ShouldBe("acme-corporation");
        project.Slug.ShouldBe("billing");
    }

    [Theory]
    [InlineData(OrganizationRole.Viewer, OrganizationRole.Member, false)]
    [InlineData(OrganizationRole.Admin, OrganizationRole.Member, true)]
    [InlineData(OrganizationRole.Owner, OrganizationRole.Admin, true)]
    [InlineData(OrganizationRole.Member, OrganizationRole.Member, true)]
    public void Roles_are_ordered_by_privilege(OrganizationRole actual, OrganizationRole required, bool expected)
    {
        var org = NewOrg(out _);
        var member = org.AddMember(Guid.CreateVersion7(), actual);

        member.Can(required).ShouldBe(expected);
    }
}

public class ApiProjectTests
{
    private static Organization NewOrg() => new("Acme", "acme", Guid.CreateVersion7());

    [Fact]
    public void Spec_versions_are_numbered_sequentially()
    {
        var project = NewOrg().AddProject("Billing", "billing");

        var first = project.AddSpecVersion(id => $"specs/{id:N}.yaml", "hash1", 100, SpecSource.Upload);
        var second = project.AddSpecVersion(id => $"specs/{id:N}.yaml", "hash2", 100, SpecSource.Upload);

        first.Sequence.ShouldBe(1);
        second.Sequence.ShouldBe(2);
        project.LatestSpec.ShouldBe(second);
    }

    [Fact]
    public void The_storage_key_is_built_from_the_entity_id()
    {
        var project = NewOrg().AddProject("Billing", "billing");

        var spec = project.AddSpecVersion(id => $"specs/{id:N}.yaml", "hash", 100, SpecSource.Upload);

        spec.StorageKey.ShouldBe($"specs/{spec.Id:N}.yaml");
    }

    [Fact]
    public void One_target_per_language()
    {
        var project = NewOrg().AddProject("Billing", "billing");
        project.AddTarget(SdkLanguage.TypeScript, "@acme/a", RegistryKind.Npm);

        Should.Throw<InvalidOperationException>(
            () => project.AddTarget(SdkLanguage.TypeScript, "@acme/b", RegistryKind.Npm));
    }

    [Fact]
    public void A_package_name_invalid_for_its_registry_is_rejected()
    {
        var project = NewOrg().AddProject("Billing", "billing");

        // Uppercase is not a legal npm package name.
        Should.Throw<ArgumentException>(
            () => project.AddTarget(SdkLanguage.TypeScript, "@Acme/Billing", RegistryKind.Npm));
    }

    [Fact]
    public void Renaming_a_target_forgets_the_release_published_under_the_old_name()
    {
        var target = NewTarget();
        target.RecordPublication("1.4.2", DateTimeOffset.UtcNow);

        target.Rename("@acme/billing-sdk");

        target.PackageName.ShouldBe("@acme/billing-sdk");
        // It went out as @acme/a@1.4.2, and reporting it beside the new name would claim a
        // release that was never made.
        target.LastPublishedVersion.ShouldBeNull();
        target.LastPublishedAt.ShouldBeNull();
    }

    [Fact]
    public void Renaming_a_target_to_the_name_it_already_has_keeps_its_release()
    {
        var target = NewTarget();
        target.RecordPublication("1.4.2", DateTimeOffset.UtcNow);

        target.Rename("@acme/a");

        target.LastPublishedVersion.ShouldBe("1.4.2");
    }

    [Fact]
    public void A_rename_to_a_name_invalid_for_the_registry_leaves_the_target_alone()
    {
        var target = NewTarget();

        // Uppercase is not a legal npm package name.
        Should.Throw<ArgumentException>(() => target.Rename("@Acme/Billing"));

        target.PackageName.ShouldBe("@acme/a");
    }

    private static SdkTarget NewTarget() =>
        NewOrg().AddProject("Billing", "billing")
            .AddTarget(SdkLanguage.TypeScript, "@acme/a", RegistryKind.Npm);

    [Fact]
    public void A_spec_that_failed_validation_is_not_generatable()
    {
        var project = NewOrg().AddProject("Billing", "billing");
        var spec = project.AddSpecVersion(id => $"{id:N}", "hash", 10, SpecSource.Upload);

        spec.MarkValidated(SpecValidationStatus.Invalid, null, null, null, 0, 0, 2, null);

        spec.IsGeneratable.ShouldBeFalse();
    }

    [Fact]
    public void Warnings_do_not_block_generation()
    {
        var project = NewOrg().AddProject("Billing", "billing");
        var spec = project.AddSpecVersion(id => $"{id:N}", "hash", 10, SpecSource.Upload);

        spec.MarkValidated(SpecValidationStatus.ValidWithWarnings, "API", "1.0.0", "3.0.3", 0, 3, 0, null);

        spec.IsGeneratable.ShouldBeTrue();
    }
}

public class PackageCoordinatesTests
{
    [Theory]
    [InlineData("@acme/billing-sdk", true)]
    [InlineData("billing-sdk", true)]
    [InlineData("@Acme/Billing", false)]
    [InlineData("Billing", false)]
    [InlineData(".hidden", false)]
    [InlineData("", false)]
    public void Npm_names_must_be_lowercase_and_well_formed(string name, bool expected) =>
        PackageCoordinates.IsValidFor(RegistryKind.Npm, name).ShouldBe(expected);

    [Theory]
    [InlineData("Acme.Billing.Sdk", true)]
    [InlineData("acme-billing", true)]
    [InlineData("bad name", false)]
    public void NuGet_names_allow_mixed_case(string name, bool expected) =>
        PackageCoordinates.IsValidFor(RegistryKind.NuGet, name).ShouldBe(expected);

    [Theory]
    [InlineData("com.acme:billing-sdk", true)]
    [InlineData("billing-sdk", false)]
    public void Maven_requires_group_and_artifact(string name, bool expected) =>
        PackageCoordinates.IsValidFor(RegistryKind.Maven, name).ShouldBe(expected);
}
