using SpaceCraft.Domain.Projects;
using SpaceCraft.Domain.Registries;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

public class PublicationRetryTests
{
    private static Publication Pending() => new(
        Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
        RegistryKind.Npm, "billing-sdk", "2.0.0");

    [Fact]
    public void A_retry_points_the_record_at_the_run_retrying_it()
    {
        var publication = Pending();
        publication.Fail("the credential could not be decrypted");
        var retryingRun = Guid.CreateVersion7();

        publication.Retry(retryingRun);

        publication.PipelineRunId.ShouldBe(retryingRun);
        publication.Status.ShouldBe(PublicationStatus.Pending);
    }

    [Fact]
    public void A_retry_clears_what_the_last_attempt_concluded()
    {
        // Otherwise a record that succeeded once would keep its registry URL through an
        // attempt that has not reached the registry yet.
        var publication = Pending();
        publication.Succeed("https://registry.npmjs.org/billing-sdk");

        publication.Retry(Guid.CreateVersion7());

        publication.RegistryUrl.ShouldBeNull();
        publication.FailureReason.ShouldBeNull();
        publication.CompletedAt.ShouldBeNull();
    }
}
