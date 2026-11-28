using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sheaf.Data.Migrations;

/// <inheritdoc />
public partial class _20261128055430_PocDemo : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PocAuditEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                Action = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                AiInvolved = table.Column<bool>(type: "bit", nullable: false),
                HumanEdited = table.Column<bool>(type: "bit", nullable: false),
                ProposalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PocAuditEntries", x => new { x.ChurchId, x.Id });
                table.ForeignKey(
                    name: "FK_PocAuditEntries_Churches_ChurchId",
                    column: x => x.ChurchId,
                    principalTable: "Churches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PocCaptures",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                Text = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                Redacted = table.Column<bool>(type: "bit", nullable: false),
                AiLive = table.Column<bool>(type: "bit", nullable: false),
                Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PocCaptures", x => new { x.ChurchId, x.Id });
                table.ForeignKey(
                    name: "FK_PocCaptures_Churches_ChurchId",
                    column: x => x.ChurchId,
                    principalTable: "Churches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PocPersonProfiles",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Dorm = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                ClassYear = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                HowWeMet = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                Source = table.Column<int>(type: "int", nullable: false),
                IsMinor = table.Column<bool>(type: "bit", nullable: false),
                GuardianPersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                DoNotContactAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                DoNotContactByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                NextStep = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PocPersonProfiles", x => new { x.ChurchId, x.Id });
                table.ForeignKey(
                    name: "FK_PocPersonProfiles_Churches_ChurchId",
                    column: x => x.ChurchId,
                    principalTable: "Churches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PocPersonProfiles_People_ChurchId_GuardianPersonId",
                    columns: x => new { x.ChurchId, x.GuardianPersonId },
                    principalTable: "People",
                    principalColumns: new[] { "ChurchId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PocPersonProfiles_People_ChurchId_PersonId",
                    columns: x => new { x.ChurchId, x.PersonId },
                    principalTable: "People",
                    principalColumns: new[] { "ChurchId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PocTouchpoints",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                AiDrafted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PocTouchpoints", x => new { x.ChurchId, x.Id });
                table.ForeignKey(
                    name: "FK_PocTouchpoints_Churches_ChurchId",
                    column: x => x.ChurchId,
                    principalTable: "Churches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PocTouchpoints_People_ChurchId_PersonId",
                    columns: x => new { x.ChurchId, x.PersonId },
                    principalTable: "People",
                    principalColumns: new[] { "ChurchId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PocProposals",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CaptureId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                OwnerUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Payload = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                Reasons = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                SourceExcerpt = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                TargetPersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                DuplicatePersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                AiGenerated = table.Column<bool>(type: "bit", nullable: false),
                DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                DecidedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                ResultPersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PocProposals", x => new { x.ChurchId, x.Id });
                table.ForeignKey(
                    name: "FK_PocProposals_Churches_ChurchId",
                    column: x => x.ChurchId,
                    principalTable: "Churches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PocProposals_PocCaptures_ChurchId_CaptureId",
                    columns: x => new { x.ChurchId, x.CaptureId },
                    principalTable: "PocCaptures",
                    principalColumns: new[] { "ChurchId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PocSensitiveNotes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CaptureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                OriginalText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                Categories = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                ReviewedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PocSensitiveNotes", x => new { x.ChurchId, x.Id });
                table.ForeignKey(
                    name: "FK_PocSensitiveNotes_Churches_ChurchId",
                    column: x => x.ChurchId,
                    principalTable: "Churches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PocSensitiveNotes_PocCaptures_ChurchId_CaptureId",
                    columns: x => new { x.ChurchId, x.CaptureId },
                    principalTable: "PocCaptures",
                    principalColumns: new[] { "ChurchId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PocAuditEntries_ChurchId_At",
            table: "PocAuditEntries",
            columns: new[] { "ChurchId", "At" });

        migrationBuilder.CreateIndex(
            name: "IX_PocCaptures_ChurchId_CreatedByUserId_CreatedAt",
            table: "PocCaptures",
            columns: new[] { "ChurchId", "CreatedByUserId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_PocPersonProfiles_ChurchId_GuardianPersonId",
            table: "PocPersonProfiles",
            columns: new[] { "ChurchId", "GuardianPersonId" });

        migrationBuilder.CreateIndex(
            name: "IX_PocPersonProfiles_ChurchId_PersonId",
            table: "PocPersonProfiles",
            columns: new[] { "ChurchId", "PersonId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PocProposals_ChurchId_CaptureId",
            table: "PocProposals",
            columns: new[] { "ChurchId", "CaptureId" });

        migrationBuilder.CreateIndex(
            name: "IX_PocProposals_ChurchId_OwnerUserId_Status",
            table: "PocProposals",
            columns: new[] { "ChurchId", "OwnerUserId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_PocSensitiveNotes_ChurchId_CaptureId",
            table: "PocSensitiveNotes",
            columns: new[] { "ChurchId", "CaptureId" });

        migrationBuilder.CreateIndex(
            name: "IX_PocTouchpoints_ChurchId_PersonId_At",
            table: "PocTouchpoints",
            columns: new[] { "ChurchId", "PersonId", "At" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PocAuditEntries");

        migrationBuilder.DropTable(
            name: "PocPersonProfiles");

        migrationBuilder.DropTable(
            name: "PocProposals");

        migrationBuilder.DropTable(
            name: "PocSensitiveNotes");

        migrationBuilder.DropTable(
            name: "PocTouchpoints");

        migrationBuilder.DropTable(
            name: "PocCaptures");
    }
}
