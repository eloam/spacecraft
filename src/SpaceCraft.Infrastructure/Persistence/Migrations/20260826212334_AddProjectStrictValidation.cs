using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpaceCraft.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectStrictValidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "strict_validation",
                schema: "spacecraft",
                table: "api_projects",
                type: "boolean",
                nullable: false,
                // true, not the false EF infers from the CLR default: the option is on by
                // default, and projects that already exist are projects too.
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "strict_validation",
                schema: "spacecraft",
                table: "api_projects");
        }
    }
}
