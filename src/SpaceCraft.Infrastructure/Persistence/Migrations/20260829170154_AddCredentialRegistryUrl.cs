using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpaceCraft.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCredentialRegistryUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "registry_url",
                schema: "spacecraft",
                table: "registry_credentials",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                // npmjs rather than "": every existing row is an npm credential, and a blank
                // endpoint would pass the column constraint only to fail inside a publish,
                // where the person who could fix it is no longer looking.
                defaultValue: "https://registry.npmjs.org");

            // Anything but npm, had there been one, points where its kind lives.
            migrationBuilder.Sql("""
                UPDATE spacecraft.registry_credentials SET registry_url = CASE registry
                    WHEN 2 THEN 'https://api.nuget.org/v3/index.json'
                    WHEN 3 THEN 'https://repo1.maven.org/maven2'
                    WHEN 4 THEN 'https://upload.pypi.org/legacy/'
                    WHEN 5 THEN 'https://packagist.org'
                    ELSE 'https://registry.npmjs.org'
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "registry_url",
                schema: "spacecraft",
                table: "registry_credentials");
        }
    }
}
