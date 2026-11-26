using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sheaf.Data.Migrations;

/// <inheritdoc />
public partial class _20261126001848_TrackerFoundation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "GroupTypes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GroupTypes", x => new { x.ChurchId, x.Id });
                table.CheckConstraint("CK_GroupTypes_Name", "TRIM([Name]) <> ''");
                table.ForeignKey(
                    name: "FK_GroupTypes_Churches_ChurchId",
                    column: x => x.ChurchId,
                    principalTable: "Churches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Households",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Households", x => new { x.ChurchId, x.Id });
                table.ForeignKey(
                    name: "FK_Households_Churches_ChurchId",
                    column: x => x.ChurchId,
                    principalTable: "Churches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Contexts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                EndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                GroupTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Contexts", x => new { x.ChurchId, x.Id });
                table.CheckConstraint("CK_Contexts_Dates", "[EndsAt] IS NULL OR [EndsAt] >= [StartsAt]");
                table.CheckConstraint("CK_Contexts_Kind", "[Kind] = 0");
                table.CheckConstraint("CK_Contexts_Name", "TRIM([Name]) <> ''");
                table.ForeignKey(
                    name: "FK_Contexts_Churches_ChurchId",
                    column: x => x.ChurchId,
                    principalTable: "Churches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Contexts_GroupTypes_ChurchId_GroupTypeId",
                    columns: x => new { x.ChurchId, x.GroupTypeId },
                    principalTable: "GroupTypes",
                    principalColumns: new[] { "ChurchId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "People",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                HouseholdId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_People", x => new { x.ChurchId, x.Id });
                table.CheckConstraint("CK_People_FirstName", "TRIM([FirstName]) <> ''");
                table.ForeignKey(
                    name: "FK_People_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_People_Churches_ChurchId",
                    column: x => x.ChurchId,
                    principalTable: "Churches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_People_Households_ChurchId_HouseholdId",
                    columns: x => new { x.ChurchId, x.HouseholdId },
                    principalTable: "Households",
                    principalColumns: new[] { "ChurchId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ContextLeaders",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ContextId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                EndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ContextLeaders", x => new { x.ChurchId, x.Id });
                table.CheckConstraint("CK_ContextLeaders_Dates", "[EndsAt] IS NULL OR [EndsAt] >= [StartsAt]");
                table.ForeignKey(
                    name: "FK_ContextLeaders_Churches_ChurchId",
                    column: x => x.ChurchId,
                    principalTable: "Churches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ContextLeaders_Contexts_ChurchId_ContextId",
                    columns: x => new { x.ChurchId, x.ContextId },
                    principalTable: "Contexts",
                    principalColumns: new[] { "ChurchId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ContextLeaders_People_ChurchId_PersonId",
                    columns: x => new { x.ChurchId, x.PersonId },
                    principalTable: "People",
                    principalColumns: new[] { "ChurchId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ContextMembers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ContextId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                JoinedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                LeftAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ContextMembers", x => new { x.ChurchId, x.Id });
                table.CheckConstraint("CK_ContextMembers_Dates", "[LeftAt] IS NULL OR [LeftAt] >= [JoinedAt]");
                table.ForeignKey(
                    name: "FK_ContextMembers_Churches_ChurchId",
                    column: x => x.ChurchId,
                    principalTable: "Churches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ContextMembers_Contexts_ChurchId_ContextId",
                    columns: x => new { x.ChurchId, x.ContextId },
                    principalTable: "Contexts",
                    principalColumns: new[] { "ChurchId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ContextMembers_People_ChurchId_PersonId",
                    columns: x => new { x.ChurchId, x.PersonId },
                    principalTable: "People",
                    principalColumns: new[] { "ChurchId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ContextLeaders_ChurchId_ContextId",
            table: "ContextLeaders",
            columns: new[] { "ChurchId", "ContextId" });

        migrationBuilder.CreateIndex(
            name: "IX_ContextLeaders_ChurchId_PersonId",
            table: "ContextLeaders",
            columns: new[] { "ChurchId", "PersonId" });

        migrationBuilder.CreateIndex(
            name: "IX_ContextMembers_ChurchId_ContextId",
            table: "ContextMembers",
            columns: new[] { "ChurchId", "ContextId" });

        migrationBuilder.CreateIndex(
            name: "IX_ContextMembers_ChurchId_PersonId",
            table: "ContextMembers",
            columns: new[] { "ChurchId", "PersonId" });

        migrationBuilder.CreateIndex(
            name: "IX_Contexts_ChurchId_GroupTypeId",
            table: "Contexts",
            columns: new[] { "ChurchId", "GroupTypeId" });

        migrationBuilder.CreateIndex(
            name: "IX_People_ChurchId_HouseholdId",
            table: "People",
            columns: new[] { "ChurchId", "HouseholdId" });

        migrationBuilder.CreateIndex(
            name: "IX_People_ChurchId_UserId",
            table: "People",
            columns: new[] { "ChurchId", "UserId" },
            unique: true,
            filter: "[UserId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_People_UserId",
            table: "People",
            column: "UserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ContextLeaders");

        migrationBuilder.DropTable(
            name: "ContextMembers");

        migrationBuilder.DropTable(
            name: "Contexts");

        migrationBuilder.DropTable(
            name: "People");

        migrationBuilder.DropTable(
            name: "GroupTypes");

        migrationBuilder.DropTable(
            name: "Households");
    }
}
