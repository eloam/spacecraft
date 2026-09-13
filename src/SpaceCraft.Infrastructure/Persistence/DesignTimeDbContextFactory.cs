using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SpaceCraft.Infrastructure.Persistence;

/// <summary>
/// How <c>dotnet ef</c> builds a context, so that it never builds the application.
/// </summary>
/// <remarks>
/// Without this, the tooling falls back to constructing the startup project's host to find a
/// registered context — and the API's host cannot be constructed outside Development on a bare
/// checkout. The key-protection and email registrations validate eagerly, at registration
/// rather than at start, and throw on configuration a CI job has no reason to hold: a Key
/// Manager key id, an IAM key, a relay password. Generating a migration bundle would fail asking
/// for credentials it will never use.
/// <para>
/// That eagerness is deliberate elsewhere and worth keeping — a deployment that queues runs
/// nothing will ever start should refuse to boot rather than look healthy. This is the seam that
/// lets both be true.
/// </para>
/// <para>
/// There is no connection string, and that is the whole of the second decision. Building a model
/// needs a provider — something that knows how PostgreSQL spells a column type — and not a
/// database: <c>migrations add</c>, <c>script</c>, <c>bundle</c> and
/// <c>has-pending-model-changes</c> all run without opening one. So this passes none, which the
/// provider supports precisely for this case.
/// </para>
/// <para>
/// A placeholder would have been worse than nothing. It would make <c>dotnet ef database
/// update</c> silently target whatever the placeholder named — a localhost database that is not
/// the developer's — instead of failing. With no connection string it fails on the spot with
/// "The ConnectionString property has not been initialized.", which is the truth: this factory
/// exists to describe a model, and applying one is a different job. The bundle takes the real
/// connection at run time from <c>--connection</c>, which is how the schema-changing credential
/// stays out of everything but the one CI step that applies it.
/// </para>
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SpaceCraftDbContext>
{
    public SpaceCraftDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SpaceCraftDbContext>()
            .UseNpgsql(npgsql => npgsql.MigrationsHistoryTable("__migrations", "spacecraft"))
            .Options;

        return new SpaceCraftDbContext(options);
    }
}
