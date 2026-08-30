using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpaceCraft.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SnapshotTargetOnRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "target_auto_publish",
                schema: "spacecraft",
                table: "pipeline_runs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "target_language",
                schema: "spacecraft",
                table: "pipeline_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "target_package_name",
                schema: "spacecraft",
                table: "pipeline_runs",
                type: "character varying(214)",
                maxLength: 214,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "target_registry",
                schema: "spacecraft",
                table: "pipeline_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "target_registry_url",
                schema: "spacecraft",
                table: "pipeline_runs",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            // Runs queued before this migration have no snapshot, and the target they name is
            // the only evidence left of what they did. Copied once, best effort: it is not
            // guaranteed to be what the target said back then, but it is closer than a
            // language of 0 and an empty package name, and every run queued from here on
            // records its own. Rows whose target has since been deleted keep the defaults.
            migrationBuilder.Sql("""
                UPDATE spacecraft.pipeline_runs AS r
                SET target_language = t.language,
                    target_package_name = t.package_name,
                    target_registry = t.registry,
                    target_auto_publish = t.auto_publish,
                    target_registry_url = c.registry_url
                FROM spacecraft.sdk_targets AS t
                LEFT JOIN spacecraft.registry_credentials AS c ON c.id = t.registry_credential_id
                WHERE t.id = r.sdk_target_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "target_auto_publish",
                schema: "spacecraft",
                table: "pipeline_runs");

            migrationBuilder.DropColumn(
                name: "target_language",
                schema: "spacecraft",
                table: "pipeline_runs");

            migrationBuilder.DropColumn(
                name: "target_package_name",
                schema: "spacecraft",
                table: "pipeline_runs");

            migrationBuilder.DropColumn(
                name: "target_registry",
                schema: "spacecraft",
                table: "pipeline_runs");

            migrationBuilder.DropColumn(
                name: "target_registry_url",
                schema: "spacecraft",
                table: "pipeline_runs");
        }
    }
}
