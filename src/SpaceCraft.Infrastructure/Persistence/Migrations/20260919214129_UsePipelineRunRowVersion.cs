using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpaceCraft.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20260919214129_UsePipelineRunRowVersion : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<uint>(
            name: "xmin",
            schema: "spacecraft",
            table: "pipeline_runs",
            type: "xid",
            rowVersion: true,
            nullable: false,
            defaultValue: 0u);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "xmin",
            schema: "spacecraft",
            table: "pipeline_runs");
    }
}
