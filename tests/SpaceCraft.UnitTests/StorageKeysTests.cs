using SpaceCraft.Infrastructure.Storage;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// A storage key is a path, and part of it comes from a file name the client chose. These
/// tests exist for that part: nothing a caller sends may climb out of the prefix its
/// organization and project own.
/// </summary>
public class StorageKeysTests
{
    private static readonly Guid Org = Guid.Parse("01a03992-c7bf-7326-9e21-357ced45a013");
    private static readonly Guid Project = Guid.Parse("01a039df-05f2-7c12-84ca-f4661bec34e8");
    private static readonly Guid Spec = Guid.Parse("01a039df-0695-71c1-af5a-d0eb0ce3fd95");
    private static readonly Guid Run = Guid.Parse("01a03f48-510f-78d2-84a7-2b67b5cb256a");

    private static string ProjectPrefix => $"orgs/{Org:D}/projects/{Project:D}";

    [Fact]
    public void A_spec_lands_under_its_project_named_after_the_uploaded_file()
    {
        StorageKeys.Spec(Org, Project, Spec, "keycloak.yaml", ".yaml")
            .ShouldBe($"{ProjectPrefix}/specs/keycloak-{Spec:D}.yaml");
    }

    [Fact]
    public void An_artifact_and_a_log_land_under_the_run_that_produced_them()
    {
        StorageKeys.Artifact(Org, Project, Run, "package.tgz")
            .ShouldBe($"{ProjectPrefix}/runs/{Run:D}/artifact/package.tgz");

        StorageKeys.StepLog(Org, Project, Run, "Generate")
            .ShouldBe($"{ProjectPrefix}/runs/{Run:D}/logs/generate.log");
    }

    [Fact]
    public void Everything_a_project_owns_shares_one_prefix() =>
        // What the layout is for: one listing, one lifecycle rule, one bulk delete.
        new[]
        {
            StorageKeys.Spec(Org, Project, Spec, "api.yaml", ".yaml"),
            StorageKeys.Artifact(Org, Project, Run, "package.tgz"),
            StorageKeys.StepLog(Org, Project, Run, "Build"),
        }.ShouldAllBe(key => key.StartsWith(ProjectPrefix, StringComparison.Ordinal));

    [Theory]
    // Path traversal, in both separators — Path.GetFileName only splits on the host's.
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\spec.yaml")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Users\\yohann\\spec.yaml")]
    // Nothing to build a name from.
    [InlineData("...")]
    [InlineData("---")]
    [InlineData(".yaml")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    // A script Slug strips entirely, which makes it throw rather than return empty.
    [InlineData("名前.yaml")]
    public void A_hostile_file_name_cannot_escape_the_project_prefix(string? fileName)
    {
        var key = StorageKeys.Spec(Org, Project, Spec, fileName!, ".yaml");

        key.ShouldStartWith($"{ProjectPrefix}/specs/");
        key.ShouldEndWith(".yaml");

        // Whatever survived, it added no depth and no way back up.
        var name = key[$"{ProjectPrefix}/specs/".Length..];
        name.ShouldNotContain("/");
        name.ShouldNotContain("\\");
        name.ShouldNotContain("..");
    }

    [Fact]
    public void A_name_that_survives_traversal_stripping_still_names_the_file() =>
        // The leaf is kept, not discarded with the path it was hiding behind.
        StorageKeys.Spec(Org, Project, Spec, "../../secrets/billing-api.yaml", ".yaml")
            .ShouldBe($"{ProjectPrefix}/specs/billing-api-{Spec:D}.yaml");

    [Fact]
    public void Accents_and_spaces_survive_as_letters() =>
        StorageKeys.Spec(Org, Project, Spec, "spéc final.yaml", ".yaml")
            .ShouldBe($"{ProjectPrefix}/specs/spec-final-{Spec:D}.yaml");

    [Fact]
    public void The_extension_comes_from_validation_not_from_the_name() =>
        // The uploaded name claims .exe; only what validation accepted is appended.
        StorageKeys.Spec(Org, Project, Spec, "payload.exe", ".json")
            .ShouldBe($"{ProjectPrefix}/specs/payload-{Spec:D}.json");

    [Fact]
    public void A_very_long_name_stays_within_the_column()
    {
        var key = StorageKeys.Spec(Org, Project, Spec, new string('a', 300) + ".yaml", ".yaml");

        // SpecVersion.StorageKey is nvarchar(512) in ProjectConfiguration.
        key.Length.ShouldBeLessThan(512);
    }

    [Theory]
    [InlineData(".json", "json")]
    [InlineData(".yaml", "yaml")]
    [InlineData(".yml", "yaml")]
    [InlineData(".JSON", "json")]
    public void The_format_is_read_back_from_the_key_that_stored_it(string extension, string expected) =>
        // The key is the only record of what was uploaded; nothing else can disagree with it.
        StorageKeys.FormatOf(StorageKeys.Spec(Org, Project, Spec, "api", extension))
            .ShouldBe(expected);
}
