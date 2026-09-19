using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpaceCraft.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20260919164037_BoundUserDisplayName : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Registration accepted a display name of any length until its validator bounded it,
        // so a row longer than 100 characters may exist, and Postgres refuses to narrow the
        // column over one. Truncated rather than left to stop the deploy: the display name is
        // a label, and what the profile page has been refusing all along is not worth an
        // outage. Deliberate data loss, on those rows only.
        migrationBuilder.Sql("""
            UPDATE spacecraft.users
            SET display_name = left(display_name, 100)
            WHERE char_length(display_name) > 100;
            """);

        migrationBuilder.AlterColumn<string>(
            name: "display_name",
            schema: "spacecraft",
            table: "users",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "text",
            oldNullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "display_name",
            schema: "spacecraft",
            table: "users",
            type: "text",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(100)",
            oldMaxLength: 100,
            oldNullable: true);
    }
}
