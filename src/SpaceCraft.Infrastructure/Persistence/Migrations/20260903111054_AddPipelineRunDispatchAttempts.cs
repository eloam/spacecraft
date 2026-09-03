using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpaceCraft.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20260903111054_AddPipelineRunDispatchAttempts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "dispatch_attempts",
            schema: "spacecraft",
            table: "pipeline_runs",
            type: "integer",
            nullable: false,
            defaultValue: 0);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "dispatch_attempts",
            schema: "spacecraft",
            table: "pipeline_runs");
    }
}
