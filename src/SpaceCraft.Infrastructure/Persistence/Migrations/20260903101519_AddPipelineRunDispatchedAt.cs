using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpaceCraft.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20260903101519_AddPipelineRunDispatchedAt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "dispatched_at",
            schema: "spacecraft",
            table: "pipeline_runs",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "dispatched_at",
            schema: "spacecraft",
            table: "pipeline_runs");
    }
}
