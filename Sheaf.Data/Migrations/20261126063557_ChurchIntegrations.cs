using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sheaf.Data.Migrations;

/// <inheritdoc />
public partial class _20261126063557_ChurchIntegrations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ChurchIntegrations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                ExternalTenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                ExternalTenantName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                ConnectedAccountId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                ConnectedAccountName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                ConnectedByMembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ConnectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                Scopes = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                ProtectedCredentials = table.Column<string>(type: "nvarchar(max)", nullable: true),
                Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ChurchIntegrations", x => new { x.ChurchId, x.Id });
                table.ForeignKey(
                    name: "FK_ChurchIntegrations_Churches_ChurchId",
                    column: x => x.ChurchId,
                    principalTable: "Churches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "IntegrationTenantLinks",
            columns: table => new
            {
                Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                ExternalTenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ChurchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IntegrationTenantLinks", x => new { x.Provider, x.ExternalTenantId });
                table.ForeignKey(
                    name: "FK_IntegrationTenantLinks_Churches_ChurchId",
                    column: x => x.ChurchId,
                    principalTable: "Churches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ChurchIntegrations_ChurchId_Provider",
            table: "ChurchIntegrations",
            columns: new[] { "ChurchId", "Provider" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_IntegrationTenantLinks_ChurchId",
            table: "IntegrationTenantLinks",
            column: "ChurchId");

        migrationBuilder.CreateIndex(
            name: "IX_IntegrationTenantLinks_Provider_ChurchId",
            table: "IntegrationTenantLinks",
            columns: new[] { "Provider", "ChurchId" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ChurchIntegrations");

        migrationBuilder.DropTable(
            name: "IntegrationTenantLinks");
    }
}
