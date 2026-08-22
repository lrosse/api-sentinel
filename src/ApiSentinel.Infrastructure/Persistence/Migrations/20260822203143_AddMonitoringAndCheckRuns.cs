using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiSentinel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitoringAndCheckRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Monitors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EndpointId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TimeoutMs = table.Column<int>(type: "int", nullable: false),
                    ExpectedStatusCode = table.Column<int>(type: "int", nullable: false),
                    MaxLatencyMs = table.Column<int>(type: "int", nullable: true),
                    IgnoredPaths = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Monitors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Monitors_Endpoints_EndpointId",
                        column: x => x.EndpointId,
                        principalTable: "Endpoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CheckRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MonitorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    HttpStatusCode = table.Column<int>(type: "int", nullable: true),
                    LatencyMs = table.Column<long>(type: "bigint", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ResponseBodySnippet = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckRuns_Monitors_MonitorId",
                        column: x => x.MonitorId,
                        principalTable: "Monitors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckRuns_MonitorId_StartedAt",
                table: "CheckRuns",
                columns: new[] { "MonitorId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Monitors_EndpointId",
                table: "Monitors",
                column: "EndpointId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckRuns");

            migrationBuilder.DropTable(
                name: "Monitors");
        }
    }
}
