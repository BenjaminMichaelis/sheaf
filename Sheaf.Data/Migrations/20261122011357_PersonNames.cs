using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sheaf.Data.Migrations;

/// <inheritdoc />
public partial class _20261122011357_PersonNames : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [People]) THROW 51000, 'PersonNames requires an empty People table; explicit name conversion is required for existing records.', 1;");

        migrationBuilder.DropCheckConstraint(
            name: "CK_People_Name",
            table: "People");

        migrationBuilder.DropColumn(
            name: "Name",
            table: "People");

        migrationBuilder.DropColumn(
            name: "GivenName",
            table: "People");

        migrationBuilder.AddColumn<string>(
            name: "LastName",
            table: "People",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "FirstName",
            table: "People",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: false);

        migrationBuilder.AddCheckConstraint(
            name: "CK_People_FirstName",
            table: "People",
            sql: "TRIM([FirstName]) <> ''");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [People]) THROW 51000, 'PersonNames rollback requires an empty People table; existing names must be preserved explicitly.', 1;");

        migrationBuilder.DropCheckConstraint(
            name: "CK_People_FirstName",
            table: "People");

        migrationBuilder.DropColumn(
            name: "FirstName",
            table: "People");

        migrationBuilder.DropColumn(
            name: "LastName",
            table: "People");

        migrationBuilder.AddColumn<string>(
            name: "GivenName",
            table: "People",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Name",
            table: "People",
            type: "nvarchar(200)",
            maxLength: 200,
            nullable: false);

        migrationBuilder.AddCheckConstraint(
            name: "CK_People_Name",
            table: "People",
            sql: "TRIM([Name]) <> ''");
    }
}
