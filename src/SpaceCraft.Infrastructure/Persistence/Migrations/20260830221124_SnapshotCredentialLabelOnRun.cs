using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpaceCraft.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SnapshotCredentialLabelOnRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "target_credential_label",
                schema: "spacecraft",
                table: "pipeline_runs",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            // Same best-effort backfill as the columns beside it: for runs queued before the
            // snapshot existed, the credential the target names today is the only evidence.
            migrationBuilder.Sql("""
                UPDATE spacecraft.pipeline_runs AS r
                SET target_credential_label = c.label
                FROM spacecraft.sdk_targets AS t
                JOIN spacecraft.registry_credentials AS c ON c.id = t.registry_credential_id
                WHERE t.id = r.sdk_target_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "target_credential_label",
                schema: "spacecraft",
                table: "pipeline_runs");
        }
    }
}
