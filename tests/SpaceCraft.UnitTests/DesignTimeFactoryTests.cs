using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SpaceCraft.Infrastructure.Persistence;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// What <c>dotnet ef</c> gets when it asks this repository for a context.
/// </summary>
/// <remarks>
/// Two properties, and one of them is an absence — which is why it is written down. A factory
/// that carries no connection string looks unfinished, and the obvious repair is to put one back;
/// this is here to say that the emptiness is the feature and to fail the moment somebody fills it.
/// </remarks>
public class DesignTimeFactoryTests
{
    [Fact]
    public void The_factory_carries_no_connection_string()
    {
        using var context = new DesignTimeDbContextFactory().CreateDbContext([]);

        // A placeholder would be worse than nothing. Every command this repository actually runs
        // — script, bundle, has-pending-model-changes — builds a model without opening a
        // connection, so the only command a placeholder would change is `database update`, and it
        // would change it from failing to silently applying migrations against whatever the
        // placeholder named instead of the developer's own database.
        //
        // Empty, it fails with "The ConnectionString property has not been initialized." That is
        // the honest answer: this factory describes a model, and applying one is a different job
        // with a different credential — the bundle takes it from --connection at run time.
        context.Database.GetConnectionString().ShouldBeNullOrEmpty();
    }

    [Fact]
    public void It_still_configures_the_provider_and_the_history_table()
    {
        using var context = new DesignTimeDbContextFactory().CreateDbContext([]);

        // Building a model needs a provider even when it needs no database: something has to know
        // how PostgreSQL spells a column type.
        context.Database.ProviderName.ShouldBe("Npgsql.EntityFrameworkCore.PostgreSQL");

        // The history table is not the default, and it has to be restated here. A bundle built
        // against __EFMigrationsHistory in the default schema would find no record of anything and
        // try to apply every migration again, to a database that already has the tables.
        var relational = RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>());

        relational.MigrationsHistoryTableName.ShouldBe("__migrations");
        relational.MigrationsHistoryTableSchema.ShouldBe("spacecraft");
    }
}
