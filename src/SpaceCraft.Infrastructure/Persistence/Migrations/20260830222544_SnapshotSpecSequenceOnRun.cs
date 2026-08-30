using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpaceCraft.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SnapshotSpecSequenceOnRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "spec_sequence",
                schema: "spacecraft",
                table: "pipeline_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Recovered from the spec version each run still names. Unlike the target's
            // settings, a version's number never changes, so this backfill is exact wherever
            // the row survives — and 0 where it was deleted, which is the truth for those.
            migrationBuilder.Sql("""
                UPDATE spacecraft.pipeline_runs AS r
                SET spec_sequence = s.sequence
                FROM spacecraft.spec_versions AS s
                WHERE s.id = r.spec_version_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "spec_sequence",
                schema: "spacecraft",
                table: "pipeline_runs");
        }
    }
}
