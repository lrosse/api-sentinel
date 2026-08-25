using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiSentinel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConsecutiveFailuresThreshold",
                table: "Monitors",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.CreateTable(
                name: "Incidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MonitorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecoveredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TriggerReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RootCause = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Incidents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Incidents_Monitors_MonitorId",
                        column: x => x.MonitorId,
                        principalTable: "Monitors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IncidentEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    RelatedCheckRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RelatedContractChangeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncidentEvents_CheckRuns_RelatedCheckRunId",
                        column: x => x.RelatedCheckRunId,
                        principalTable: "CheckRuns",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IncidentEvents_ContractChanges_RelatedContractChangeId",
                        column: x => x.RelatedContractChangeId,
                        principalTable: "ContractChanges",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IncidentEvents_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IncidentEvents_IncidentId_OccurredAt",
                table: "IncidentEvents",
                columns: new[] { "IncidentId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IncidentEvents_RelatedCheckRunId",
                table: "IncidentEvents",
                column: "RelatedCheckRunId");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentEvents_RelatedContractChangeId",
                table: "IncidentEvents",
                column: "RelatedContractChangeId");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_MonitorId_Status",
                table: "Incidents",
                columns: new[] { "MonitorId", "Status" },
                unique: true,
                filter: "[Status] = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_OpenedAt",
                table: "Incidents",
                column: "OpenedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IncidentEvents");

            migrationBuilder.DropTable(
                name: "Incidents");

            migrationBuilder.DropColumn(
                name: "ConsecutiveFailuresThreshold",
                table: "Monitors");
        }
    }
}
