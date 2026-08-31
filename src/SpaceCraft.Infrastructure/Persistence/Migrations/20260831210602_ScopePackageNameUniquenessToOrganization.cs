using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpaceCraft.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopePackageNameUniquenessToOrganization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sdk_targets_registry_package_name",
                schema: "spacecraft",
                table: "sdk_targets");

            migrationBuilder.CreateIndex(
                name: "ix_sdk_targets_registry_package_name",
                schema: "spacecraft",
                table: "sdk_targets",
                columns: new[] { "registry", "package_name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sdk_targets_registry_package_name",
                schema: "spacecraft",
                table: "sdk_targets");

            migrationBuilder.CreateIndex(
                name: "ix_sdk_targets_registry_package_name",
                schema: "spacecraft",
                table: "sdk_targets",
                columns: new[] { "registry", "package_name" },
                unique: true);
        }
    }
}
