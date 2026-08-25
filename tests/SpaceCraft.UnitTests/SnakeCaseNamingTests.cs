using SpaceCraft.Infrastructure.Persistence;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

public class SnakeCaseNamingTests
{
    [Theory]
    [InlineData("SpecVersionId", "spec_version_id")]
    [InlineData("Id", "id")]
    [InlineData("CreatedAt", "created_at")]
    [InlineData("LeaseExpiresAt", "lease_expires_at")]
    [InlineData("ix_pipeline_runs_queue", "ix_pipeline_runs_queue")]
    [InlineData("", "")]
    public void Names_convert_to_snake_case(string input, string expected) =>
        SnakeCaseNaming.ToSnakeCase(input).ShouldBe(expected);

    [Theory]
    [InlineData("GitHubRepository", "github_repository")]
    [InlineData("GitHubSpecPath", "github_spec_path")]
    public void Known_acronyms_stay_together(string input, string expected) =>
        // Naive splitting would produce "git_hub_repository", which reads badly in a query.
        SnakeCaseNaming.ToSnakeCase(input).ShouldBe(expected);

    [Fact]
    public void Consecutive_capitals_are_treated_as_one_word() =>
        SnakeCaseNaming.ToSnakeCase("APIKeyValue").ShouldBe("api_key_value");

    [Fact]
    public void Digits_start_a_new_segment() =>
        SnakeCaseNaming.ToSnakeCase("Sha256Hash").ShouldBe("sha256_hash");
}
