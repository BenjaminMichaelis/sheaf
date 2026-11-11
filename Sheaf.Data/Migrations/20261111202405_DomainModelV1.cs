using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sheaf.Data.Migrations
{
    /// <inheritdoc />
    public partial class DomainModelV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => new { x.ChurchId, x.Id });
                    table.ForeignKey(
                        name: "FK_AuditEntries_Churches_ChurchId",
                        column: x => x.ChurchId,
                        principalTable: "Churches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Captures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RawText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Transcript = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExtractionJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Captures", x => new { x.ChurchId, x.Id });
                    table.ForeignKey(
                        name: "FK_Captures_Churches_ChurchId",
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
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    CustomFieldSchema = table.Column<string>(type: "json", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contexts", x => new { x.ChurchId, x.Id });
                    table.ForeignKey(
                        name: "FK_Contexts_Churches_ChurchId",
                        column: x => x.ChurchId,
                        principalTable: "Churches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaderTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaderTokens", x => new { x.ChurchId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeaderTokens_ChurchMemberships_ChurchId_LeaderId",
                        columns: x => new { x.ChurchId, x.LeaderId },
                        principalTable: "ChurchMemberships",
                        principalColumns: new[] { "ChurchId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaderTokens_Churches_ChurchId",
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
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConsentSource = table.Column<int>(type: "int", nullable: false),
                    StoppedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsMinor = table.Column<bool>(type: "bit", nullable: false),
                    GuardianPersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Visibility = table.Column<int>(type: "int", nullable: false)
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
                        name: "FK_People_People_ChurchId_GuardianPersonId",
                        columns: x => new { x.ChurchId, x.GuardianPersonId },
                        principalTable: "People",
                        principalColumns: new[] { "ChurchId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Proposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Proposals", x => new { x.ChurchId, x.Id });
                    table.ForeignKey(
                        name: "FK_Proposals_Churches_ChurchId",
                        column: x => x.ChurchId,
                        principalTable: "Churches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContextLeaders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContextId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContextLeaders", x => new { x.ChurchId, x.Id });
                    table.ForeignKey(
                        name: "FK_ContextLeaders_ChurchMemberships_ChurchId_LeaderId",
                        columns: x => new { x.ChurchId, x.LeaderId },
                        principalTable: "ChurchMemberships",
                        principalColumns: new[] { "ChurchId", "Id" },
                        onDelete: ReferentialAction.Restrict);
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
                });

            migrationBuilder.CreateTable(
                name: "Attendances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContextId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedByLeaderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attendances", x => new { x.ChurchId, x.Id });
                    table.ForeignKey(
                        name: "FK_Attendances_ChurchMemberships_ChurchId_RecordedByLeaderId",
                        columns: x => new { x.ChurchId, x.RecordedByLeaderId },
                        principalTable: "ChurchMemberships",
                        principalColumns: new[] { "ChurchId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Attendances_Churches_ChurchId",
                        column: x => x.ChurchId,
                        principalTable: "Churches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Attendances_Contexts_ChurchId_ContextId",
                        columns: x => new { x.ChurchId, x.ContextId },
                        principalTable: "Contexts",
                        principalColumns: new[] { "ChurchId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Attendances_People_ChurchId_PersonId",
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
                    StartedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContextMembers", x => new { x.ChurchId, x.Id });
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

            migrationBuilder.CreateTable(
                name: "PersonExternalRefs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    System = table.Column<int>(type: "int", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonExternalRefs", x => new { x.ChurchId, x.Id });
                    table.ForeignKey(
                        name: "FK_PersonExternalRefs_Churches_ChurchId",
                        column: x => x.ChurchId,
                        principalTable: "Churches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PersonExternalRefs_People_ChurchId_PersonId",
                        columns: x => new { x.ChurchId, x.PersonId },
                        principalTable: "People",
                        principalColumns: new[] { "ChurchId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Relationships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CloseReason = table.Column<int>(type: "int", nullable: true),
                    StartedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClosedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Relationships", x => new { x.ChurchId, x.Id });
                    table.ForeignKey(
                        name: "FK_Relationships_ChurchMemberships_ChurchId_LeaderId",
                        columns: x => new { x.ChurchId, x.LeaderId },
                        principalTable: "ChurchMemberships",
                        principalColumns: new[] { "ChurchId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Relationships_Churches_ChurchId",
                        column: x => x.ChurchId,
                        principalTable: "Churches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Relationships_People_ChurchId_PersonId",
                        columns: x => new { x.ChurchId, x.PersonId },
                        principalTable: "People",
                        principalColumns: new[] { "ChurchId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Touchpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorLeaderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContextId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NoteVisibility = table.Column<int>(type: "int", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Touchpoints", x => new { x.ChurchId, x.Id });
                    table.ForeignKey(
                        name: "FK_Touchpoints_ChurchMemberships_ChurchId_AuthorLeaderId",
                        columns: x => new { x.ChurchId, x.AuthorLeaderId },
                        principalTable: "ChurchMemberships",
                        principalColumns: new[] { "ChurchId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Touchpoints_Churches_ChurchId",
                        column: x => x.ChurchId,
                        principalTable: "Churches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Touchpoints_Contexts_ChurchId_ContextId",
                        columns: x => new { x.ChurchId, x.ContextId },
                        principalTable: "Contexts",
                        principalColumns: new[] { "ChurchId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Touchpoints_People_ChurchId_PersonId",
                        columns: x => new { x.ChurchId, x.PersonId },
                        principalTable: "People",
                        principalColumns: new[] { "ChurchId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Attendances_ChurchId_ContextId",
                table: "Attendances",
                columns: new[] { "ChurchId", "ContextId" });

            migrationBuilder.CreateIndex(
                name: "IX_Attendances_ChurchId_PersonId",
                table: "Attendances",
                columns: new[] { "ChurchId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_Attendances_ChurchId_RecordedByLeaderId",
                table: "Attendances",
                columns: new[] { "ChurchId", "RecordedByLeaderId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContextLeaders_ChurchId_ContextId",
                table: "ContextLeaders",
                columns: new[] { "ChurchId", "ContextId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContextLeaders_ChurchId_LeaderId",
                table: "ContextLeaders",
                columns: new[] { "ChurchId", "LeaderId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContextMembers_ChurchId_ContextId",
                table: "ContextMembers",
                columns: new[] { "ChurchId", "ContextId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContextMembers_ChurchId_PersonId",
                table: "ContextMembers",
                columns: new[] { "ChurchId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaderTokens_ChurchId_LeaderId",
                table: "LeaderTokens",
                columns: new[] { "ChurchId", "LeaderId" });

            migrationBuilder.CreateIndex(
                name: "IX_People_ChurchId_GuardianPersonId",
                table: "People",
                columns: new[] { "ChurchId", "GuardianPersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonExternalRefs_ChurchId_PersonId",
                table: "PersonExternalRefs",
                columns: new[] { "ChurchId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonExternalRefs_ChurchId_System_ExternalId",
                table: "PersonExternalRefs",
                columns: new[] { "ChurchId", "System", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Relationships_ChurchId_LeaderId",
                table: "Relationships",
                columns: new[] { "ChurchId", "LeaderId" });

            migrationBuilder.CreateIndex(
                name: "IX_Relationships_ChurchId_PersonId",
                table: "Relationships",
                columns: new[] { "ChurchId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_Touchpoints_ChurchId_AuthorLeaderId",
                table: "Touchpoints",
                columns: new[] { "ChurchId", "AuthorLeaderId" });

            migrationBuilder.CreateIndex(
                name: "IX_Touchpoints_ChurchId_ContextId",
                table: "Touchpoints",
                columns: new[] { "ChurchId", "ContextId" });

            migrationBuilder.CreateIndex(
                name: "IX_Touchpoints_ChurchId_PersonId",
                table: "Touchpoints",
                columns: new[] { "ChurchId", "PersonId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Attendances");

            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "Captures");

            migrationBuilder.DropTable(
                name: "ContextLeaders");

            migrationBuilder.DropTable(
                name: "ContextMembers");

            migrationBuilder.DropTable(
                name: "LeaderTokens");

            migrationBuilder.DropTable(
                name: "PersonExternalRefs");

            migrationBuilder.DropTable(
                name: "Proposals");

            migrationBuilder.DropTable(
                name: "Relationships");

            migrationBuilder.DropTable(
                name: "Touchpoints");

            migrationBuilder.DropTable(
                name: "Contexts");

            migrationBuilder.DropTable(
                name: "People");
        }
    }
}
