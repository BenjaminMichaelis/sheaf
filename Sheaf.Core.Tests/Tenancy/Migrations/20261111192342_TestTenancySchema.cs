using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sheaf.Core.Tests.Tenancy.Migrations;

public partial class TestTenancySchema : Migration
{
    private static readonly string[] ChurchAndEntityKeyColumns = ["ChurchId", "Id"];
    private static readonly string[] PersonEmailIndexColumns = ["ChurchId", "Email"];
    private static readonly string[] PersonLeaderIndexColumns = ["ChurchId", "LeaderId"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Churches",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Slug = table.Column<string>(type: "nvarchar(450)", nullable: false),
                Name = table.Column<string>(type: "nvarchar(max)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Churches", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "GuardRecord",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                Details = table.Column<string>(type: "json", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GuardRecord", x => new { x.ChurchId, x.Id });
                table.ForeignKey(
                    name: "FK_GuardRecord_Churches_ChurchId",
                    column: x => x.ChurchId,
                    principalTable: "Churches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Leaders",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(max)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Leaders", x => new { x.ChurchId, x.Id });
                table.ForeignKey(
                    name: "FK_Leaders_Churches_ChurchId",
                    column: x => x.ChurchId,
                    principalTable: "Churches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "People",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                Email = table.Column<string>(type: "nvarchar(450)", nullable: false),
                LeaderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_People", x => new { x.ChurchId, x.Id });
                table.ForeignKey(
                    name: "FK_People_Churches_ChurchId",
                    column: x => x.ChurchId,
                    principalTable: "Churches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_People_Leaders_ChurchId_LeaderId",
                    columns: x => new { x.ChurchId, x.LeaderId },
                    principalTable: "Leaders",
                    principalColumns: ChurchAndEntityKeyColumns);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Churches_Slug",
            table: "Churches",
            column: "Slug",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_People_ChurchId_Email",
            table: "People",
            columns: PersonEmailIndexColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_People_ChurchId_LeaderId",
            table: "People",
            columns: PersonLeaderIndexColumns);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "GuardRecord");

        migrationBuilder.DropTable(
            name: "People");

        migrationBuilder.DropTable(
            name: "Leaders");

        migrationBuilder.DropTable(
            name: "Churches");
    }
}
