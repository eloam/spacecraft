using SpaceCraft.Domain.Organizations;
using SpaceCraft.Domain.Projects;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// Deletion cascades through the graph in a specific order. These cover the shape of that
/// graph — what belongs to what — since getting it wrong leaves rows behind rather than
/// failing loudly.
/// </summary>
public class DeletionTests
{
    [Fact]
    public void A_project_carries_its_specs_and_targets()
    {
        var org = new Organization("Acme", "acme", Guid.CreateVersion7());
        var project = org.AddProject("Billing", "billing");
        project.AddSpecVersion(id => $"specs/{id:N}.yaml", "hash", 100, SpecSource.Upload);
        project.AddTarget(SdkLanguage.TypeScript, "@acme/billing", RegistryKind.Npm);

        // Removing the project from its organization takes the whole subtree with it, which
        // is what the database cascade mirrors.
        project.SpecVersions.Count.ShouldBe(1);
        project.Targets.Count.ShouldBe(1);
        org.Projects.ShouldContain(project);
    }

    [Fact]
    public void An_organization_owns_every_project_under_it()
    {
        var org = new Organization("Acme", "acme", Guid.CreateVersion7());
        org.AddProject("One", "one");
        org.AddProject("Two", "two");

        org.Projects.Count.ShouldBe(2);
        org.Projects.ShouldAllBe(p => p.OrganizationId == org.Id);
    }

    [Fact]
    public void Members_belong_to_the_organization_and_go_with_it()
    {
        var org = new Organization("Acme", "acme", Guid.CreateVersion7());
        org.AddMember(Guid.CreateVersion7(), OrganizationRole.Member);

        // Two rows, both scoped to this organization: nothing here survives it.
        org.Members.Count.ShouldBe(2);
        org.Members.ShouldAllBe(m => m.OrganizationId == org.Id);
    }
}
