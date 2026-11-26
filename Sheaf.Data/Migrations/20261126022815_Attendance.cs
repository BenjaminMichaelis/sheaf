using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sheaf.Data.Migrations;

/// <inheritdoc />
public partial class _20261126022815_Attendance : Migration
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
            name: "ContextSessions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ContextId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                RecordedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ContextSessions", x => new { x.ChurchId, x.Id });
                table.ForeignKey(
                    name: "FK_ContextSessions_Churches_ChurchId",
                    column: x => x.ChurchId,
                    principalTable: "Churches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ContextSessions_Contexts_ChurchId_ContextId",
                    columns: x => new { x.ChurchId, x.ContextId },
                    principalTable: "Contexts",
                    principalColumns: new[] { "ChurchId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Attendances",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    name: "FK_Attendances_ContextSessions_ChurchId_SessionId",
                    columns: x => new { x.ChurchId, x.SessionId },
                    principalTable: "ContextSessions",
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
            name: "IX_Attendances_ChurchId_PersonId",
            table: "Attendances",
            columns: new[] { "ChurchId", "PersonId" });

        migrationBuilder.CreateIndex(
            name: "IX_Attendances_ChurchId_SessionId_PersonId",
            table: "Attendances",
            columns: new[] { "ChurchId", "SessionId", "PersonId" },
            unique: true,
            filter: "[RemovedAt] IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_ContextSessions_ChurchId_ContextId_OccurredOn",
            table: "ContextSessions",
            columns: new[] { "ChurchId", "ContextId", "OccurredOn" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Attendances");

        migrationBuilder.DropTable(
            name: "ContextSessions");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Churches_TimeZoneId",
            table: "Churches");

        migrationBuilder.DropColumn(
            name: "TimeZoneId",
            table: "Churches");
    }
}
