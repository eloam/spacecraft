using System.Text;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Infrastructure.Persistence;

/// <summary>
/// Rewrites every table, column, key and index name to snake_case.
/// <para>
/// Postgres folds unquoted identifiers to lower case, so PascalCase names force every
/// hand-written query to quote them ("SpecVersionId"). Anyone debugging a run at 3am should
/// be able to type the query without reaching for quotes.
/// </para>
/// </summary>
internal static class SnakeCaseNaming
{
    public static void Apply(ModelBuilder builder)
    {
        foreach (var entity in builder.Model.GetEntityTypes())
        {
            if (entity.GetTableName() is { } table)
            {
                entity.SetTableName(ToSnakeCase(table));
            }

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.GetColumnName()));
            }

            foreach (var key in entity.GetKeys())
            {
                if (key.GetName() is { } name)
                {
                    key.SetName(ToSnakeCase(name));
                }
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                if (foreignKey.GetConstraintName() is { } name)
                {
                    foreignKey.SetConstraintName(ToSnakeCase(name));
                }
            }

            foreach (var index in entity.GetIndexes())
            {
                if (index.GetDatabaseName() is { } name)
                {
                    index.SetDatabaseName(ToSnakeCase(name));
                }
            }
        }
    }

    /// <summary>
    /// "SpecVersionId" -> "spec_version_id", "GitHubRepository" -> "github_repository".
    /// </summary>
    internal static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        // Treat these as single words so they do not shatter into "git_hub" / "p_y_pi".
        name = name
            .Replace("GitHub", "Github", StringComparison.Ordinal)
            .Replace("PyPI", "Pypi", StringComparison.Ordinal)
            .Replace("NuGet", "Nuget", StringComparison.Ordinal)
            .Replace("OpenAPI", "Openapi", StringComparison.Ordinal);

        var builder = new StringBuilder(name.Length + 8);

        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];

            if (current == '_')
            {
                builder.Append('_');
                continue;
            }

            if (char.IsUpper(current))
            {
                var isBoundary = i > 0
                    && (char.IsLower(name[i - 1])
                        || char.IsDigit(name[i - 1])
                        || (i + 1 < name.Length && char.IsLower(name[i + 1])));

                if (isBoundary && builder.Length > 0 && builder[^1] != '_')
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(current));
                continue;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }
}
