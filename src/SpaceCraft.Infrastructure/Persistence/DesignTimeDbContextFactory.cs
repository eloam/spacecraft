using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SpaceCraft.Infrastructure.Persistence;

/// <summary>
/// How <c>dotnet ef</c> builds a context, so that it never builds the application.
/// </summary>
/// <remarks>
/// Without this, the tooling falls back to constructing the startup project's host to find a
/// registered context — and the API's host cannot be constructed outside Development on a bare
/// checkout. <c>AddSpaceCraftJobs</c> and the key-protection registration validate eagerly, at
/// registration rather than at start, and throw on configuration a CI job has no reason to hold:
/// a job definition id, an IAM key, a Key Manager key id. Generating a migration bundle would
/// fail asking for credentials it will never use.
/// <para>
/// That eagerness is deliberate elsewhere and worth keeping — a deployment that queues runs
/// nothing will ever start should refuse to boot rather than look healthy. This is the seam that
/// lets both be true.
/// </para>
/// <para>
/// The connection string here is a design-time placeholder. Neither <c>migrations add</c> nor
/// <c>migrations bundle</c> opens a connection; they need a provider to know how to translate the
/// model, not a database. The bundle takes the real connection at run time, from
/// <c>--connection</c>, which is how the schema-changing credential stays out of everything but
/// the one CI step that applies it.
/// </para>
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SpaceCraftDbContext>
{
    public SpaceCraftDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SpaceCraftDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=spacecraft;Username=design-time;Password=design-time",
                npgsql => npgsql.MigrationsHistoryTable("__migrations", "spacecraft"))
            .Options;

        return new SpaceCraftDbContext(options);
    }
}
