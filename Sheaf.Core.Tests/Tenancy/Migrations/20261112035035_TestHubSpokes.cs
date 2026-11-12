using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sheaf.Core.Tests.Tenancy.Migrations;

/// <inheritdoc />
public partial class TestHubSpokes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Hubs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(max)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Hubs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Spokes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                HubId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Spokes", x => new { x.ChurchId, x.Id });
                table.ForeignKey(
                    name: "FK_Spokes_Churches_ChurchId",
                    column: x => x.ChurchId,
                    principalTable: "Churches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Spokes_Hubs_HubId",
                    column: x => x.HubId,
                    principalTable: "Hubs",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Spokes_HubId",
            table: "Spokes",
            column: "HubId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Spokes");

        migrationBuilder.DropTable(
            name: "Hubs");
    }
}
