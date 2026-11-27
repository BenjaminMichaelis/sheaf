using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sheaf.Data.Migrations;

/// <inheritdoc />
public partial class _20261127192232_TrackerRequirements : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsRetired",
            table: "GroupTypes",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "Description",
            table: "ContextSessions",
            type: "nvarchar(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "GroupLabel",
            table: "Churches",
            type: "nvarchar(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "GroupLabelPlural",
            table: "Churches",
            type: "nvarchar(50)",
            maxLength: 50,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IsRetired",
            table: "GroupTypes");

        migrationBuilder.DropColumn(
            name: "Description",
            table: "ContextSessions");

        migrationBuilder.DropColumn(
            name: "GroupLabel",
            table: "Churches");

        migrationBuilder.DropColumn(
            name: "GroupLabelPlural",
            table: "Churches");
    }
}
