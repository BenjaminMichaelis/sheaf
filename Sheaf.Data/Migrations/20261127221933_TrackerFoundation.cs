using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sheaf.Data.Migrations;

/// <inheritdoc />
public partial class TrackerFoundation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "TimeZoneId",
            table: "Churches",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: false,
            // Existing churches start on UTC (Etc/UTC, the IANA name); an Admin sets the real zone with PUT /api/church/settings.
            defaultValue: "Etc/UTC");

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
            name: "Groups",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                EndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                GroupTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Groups", x => new { x.ChurchId, x.Id });
                table.CheckConstraint("CK_Groups_Dates", "[EndsAt] IS NULL OR [EndsAt] >= [StartsAt]");
                table.CheckConstraint("CK_Groups_Name", "TRIM([Name]) <> ''");
                table.ForeignKey(
                    name: "FK_Groups_Churches_ChurchId",
                    column: x => x.ChurchId,
                    principalTable: "Churches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Groups_GroupTypes_ChurchId_GroupTypeId",
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
            name: "GroupMeetings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                RecordedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GroupMeetings", x => new { x.ChurchId, x.Id });
                table.ForeignKey(
                    name: "FK_GroupMeetings_Churches_ChurchId",
                    column: x => x.ChurchId,
                    principalTable: "Churches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_GroupMeetings_Groups_ChurchId_GroupId",
                    columns: x => new { x.ChurchId, x.GroupId },
                    principalTable: "Groups",
                    principalColumns: new[] { "ChurchId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "GroupAssignments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Role = table.Column<int>(type: "int", nullable: false),
                StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                EndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GroupAssignments", x => new { x.ChurchId, x.Id });
                table.CheckConstraint("CK_GroupAssignments_Dates", "[EndsAt] IS NULL OR [EndsAt] >= [StartsAt]");
                table.CheckConstraint("CK_GroupAssignments_Role", "[Role] IN (0, 1)");
                table.ForeignKey(
                    name: "FK_GroupAssignments_Churches_ChurchId",
                    column: x => x.ChurchId,
                    principalTable: "Churches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_GroupAssignments_Groups_ChurchId_GroupId",
                    columns: x => new { x.ChurchId, x.GroupId },
                    principalTable: "Groups",
                    principalColumns: new[] { "ChurchId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_GroupAssignments_People_ChurchId_PersonId",
                    columns: x => new { x.ChurchId, x.PersonId },
                    principalTable: "People",
                    principalColumns: new[] { "ChurchId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Attendances",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                MeetingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                RecordedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                RemovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                RemovedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Attendances", x => new { x.ChurchId, x.Id });
                table.CheckConstraint("CK_Attendances_Removed", "([RemovedAt] IS NULL AND [RemovedByUserId] IS NULL) OR ([RemovedAt] IS NOT NULL AND [RemovedAt] >= [RecordedAt])");
                table.ForeignKey(
                    name: "FK_Attendances_Churches_ChurchId",
                    column: x => x.ChurchId,
                    principalTable: "Churches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Attendances_GroupMeetings_ChurchId_MeetingId",
                    columns: x => new { x.ChurchId, x.MeetingId },
                    principalTable: "GroupMeetings",
                    principalColumns: new[] { "ChurchId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Attendances_People_ChurchId_PersonId",
                    columns: x => new { x.ChurchId, x.PersonId },
                    principalTable: "People",
                    principalColumns: new[] { "ChurchId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddCheckConstraint(
            name: "CK_Churches_TimeZoneId",
            table: "Churches",
            sql: "TRIM([TimeZoneId]) <> ''");

        migrationBuilder.CreateIndex(
            name: "IX_Attendances_ChurchId_MeetingId_PersonId",
            table: "Attendances",
            columns: new[] { "ChurchId", "MeetingId", "PersonId" },
            unique: true,
            filter: "[RemovedAt] IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Attendances_ChurchId_PersonId",
            table: "Attendances",
            columns: new[] { "ChurchId", "PersonId" });

        migrationBuilder.CreateIndex(
            name: "IX_GroupAssignments_ChurchId_GroupId_PersonId",
            table: "GroupAssignments",
            columns: new[] { "ChurchId", "GroupId", "PersonId" },
            unique: true,
            filter: "[EndsAt] IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_GroupAssignments_ChurchId_PersonId",
            table: "GroupAssignments",
            columns: new[] { "ChurchId", "PersonId" });

        migrationBuilder.CreateIndex(
            name: "IX_GroupMeetings_ChurchId_GroupId_OccurredOn",
            table: "GroupMeetings",
            columns: new[] { "ChurchId", "GroupId", "OccurredOn" });

        migrationBuilder.CreateIndex(
            name: "IX_Groups_ChurchId_GroupTypeId",
            table: "Groups",
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
            name: "Attendances");

        migrationBuilder.DropTable(
            name: "GroupAssignments");

        migrationBuilder.DropTable(
            name: "GroupMeetings");

        migrationBuilder.DropTable(
            name: "People");

        migrationBuilder.DropTable(
            name: "Groups");

        migrationBuilder.DropTable(
            name: "Households");

        migrationBuilder.DropTable(
            name: "GroupTypes");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Churches_TimeZoneId",
            table: "Churches");

        migrationBuilder.DropColumn(
            name: "TimeZoneId",
            table: "Churches");
    }
}
