using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpaceCraft.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20260919214615_IndexFilteredAndSortedColumns : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_publications_organization_id",
            schema: "spacecraft",
            table: "publications");

        migrationBuilder.DropIndex(
            name: "ix_pipeline_runs_organization_id",
            schema: "spacecraft",
            table: "pipeline_runs");

        migrationBuilder.CreateIndex(
            name: "ix_publications_organization_id_created_at",
            schema: "spacecraft",
            table: "publications",
            columns: new[] { "organization_id", "created_at" });

        migrationBuilder.CreateIndex(
            name: "ix_pipeline_runs_organization_id_queued_at",
            schema: "spacecraft",
            table: "pipeline_runs",
            columns: new[] { "organization_id", "queued_at" });

        migrationBuilder.CreateIndex(
            name: "ix_pipeline_runs_sdk_target_id_status",
            schema: "spacecraft",
            table: "pipeline_runs",
            columns: new[] { "sdk_target_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_pipeline_runs_spec_version_id",
            schema: "spacecraft",
            table: "pipeline_runs",
            column: "spec_version_id");

        migrationBuilder.CreateIndex(
            name: "ix_notifications_created_at",
            schema: "spacecraft",
            table: "notifications",
            column: "created_at");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_publications_organization_id_created_at",
            schema: "spacecraft",
            table: "publications");

        migrationBuilder.DropIndex(
            name: "ix_pipeline_runs_organization_id_queued_at",
            schema: "spacecraft",
            table: "pipeline_runs");

        migrationBuilder.DropIndex(
            name: "ix_pipeline_runs_sdk_target_id_status",
            schema: "spacecraft",
            table: "pipeline_runs");

        migrationBuilder.DropIndex(
            name: "ix_pipeline_runs_spec_version_id",
            schema: "spacecraft",
            table: "pipeline_runs");

        migrationBuilder.DropIndex(
            name: "ix_notifications_created_at",
            schema: "spacecraft",
            table: "notifications");

        migrationBuilder.CreateIndex(
            name: "ix_publications_organization_id",
            schema: "spacecraft",
            table: "publications",
            column: "organization_id");

        migrationBuilder.CreateIndex(
            name: "ix_pipeline_runs_organization_id",
            schema: "spacecraft",
            table: "pipeline_runs",
            column: "organization_id");
    }
}
