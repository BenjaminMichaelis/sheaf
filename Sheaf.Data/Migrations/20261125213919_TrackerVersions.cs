using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sheaf.Data.Migrations;

/// <inheritdoc />
public partial class _20261125213919_TrackerVersions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "Version",
            table: "People",
            type: "uniqueidentifier",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

        migrationBuilder.AddColumn<Guid>(
            name: "Version",
            table: "Households",
            type: "uniqueidentifier",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Version",
            table: "People");

        migrationBuilder.DropColumn(
            name: "Version",
            table: "Households");
    }
}
