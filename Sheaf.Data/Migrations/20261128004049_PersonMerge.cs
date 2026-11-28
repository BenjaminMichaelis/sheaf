using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sheaf.Data.Migrations;

/// <inheritdoc />
public partial class PersonMerge : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "MergedAt",
            table: "People",
            type: "datetimeoffset",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "MergedByUserId",
            table: "People",
            type: "nvarchar(450)",
            maxLength: 450,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "MergedIntoId",
            table: "People",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "PersonDuplicateDismissals",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OtherPersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DismissedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                DismissedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PersonDuplicateDismissals", x => new { x.ChurchId, x.Id });
                table.CheckConstraint("CK_PersonDuplicateDismissals_Pair", "[PersonId] <> [OtherPersonId]");
                table.ForeignKey(
                    name: "FK_PersonDuplicateDismissals_Churches_ChurchId",
                    column: x => x.ChurchId,
                    principalTable: "Churches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PersonDuplicateDismissals_People_ChurchId_OtherPersonId",
                    columns: x => new { x.ChurchId, x.OtherPersonId },
                    principalTable: "People",
                    principalColumns: new[] { "ChurchId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PersonDuplicateDismissals_People_ChurchId_PersonId",
                    columns: x => new { x.ChurchId, x.PersonId },
                    principalTable: "People",
                    principalColumns: new[] { "ChurchId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_People_ChurchId_MergedIntoId",
            table: "People",
            columns: new[] { "ChurchId", "MergedIntoId" });

        migrationBuilder.AddCheckConstraint(
            name: "CK_People_Merged",
            table: "People",
            sql: "([MergedIntoId] IS NULL AND [MergedAt] IS NULL AND [MergedByUserId] IS NULL) OR ([MergedIntoId] IS NOT NULL AND [MergedAt] IS NOT NULL AND [MergedIntoId] <> [Id])");

        migrationBuilder.CreateIndex(
            name: "IX_PersonDuplicateDismissals_ChurchId_OtherPersonId",
            table: "PersonDuplicateDismissals",
            columns: new[] { "ChurchId", "OtherPersonId" });

        migrationBuilder.CreateIndex(
            name: "IX_PersonDuplicateDismissals_ChurchId_PersonId_OtherPersonId",
            table: "PersonDuplicateDismissals",
            columns: new[] { "ChurchId", "PersonId", "OtherPersonId" },
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_People_People_ChurchId_MergedIntoId",
            table: "People",
            columns: new[] { "ChurchId", "MergedIntoId" },
            principalTable: "People",
            principalColumns: new[] { "ChurchId", "Id" },
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_People_People_ChurchId_MergedIntoId",
            table: "People");

        migrationBuilder.DropTable(
            name: "PersonDuplicateDismissals");

        migrationBuilder.DropIndex(
            name: "IX_People_ChurchId_MergedIntoId",
            table: "People");

        migrationBuilder.DropCheckConstraint(
            name: "CK_People_Merged",
            table: "People");

        migrationBuilder.DropColumn(
            name: "MergedAt",
            table: "People");

        migrationBuilder.DropColumn(
            name: "MergedByUserId",
            table: "People");

        migrationBuilder.DropColumn(
            name: "MergedIntoId",
            table: "People");
    }
}
