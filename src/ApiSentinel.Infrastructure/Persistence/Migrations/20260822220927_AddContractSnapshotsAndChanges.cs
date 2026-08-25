using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiSentinel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContractSnapshotsAndChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SchemaSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MonitorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StructureHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    StructureJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AnalysisStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchemaSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SchemaSnapshots_Monitors_MonitorId",
                        column: x => x.MonitorId,
                        principalTable: "Monitors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContractChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MonitorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FromSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Classification = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ChangesJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractChanges_Monitors_MonitorId",
                        column: x => x.MonitorId,
                        principalTable: "Monitors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContractChanges_SchemaSnapshots_FromSnapshotId",
                        column: x => x.FromSnapshotId,
                        principalTable: "SchemaSnapshots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ContractChanges_SchemaSnapshots_ToSnapshotId",
                        column: x => x.ToSnapshotId,
                        principalTable: "SchemaSnapshots",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContractChanges_FromSnapshotId",
                table: "ContractChanges",
                column: "FromSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractChanges_MonitorId_DetectedAt",
                table: "ContractChanges",
                columns: new[] { "MonitorId", "DetectedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractChanges_ToSnapshotId",
                table: "ContractChanges",
                column: "ToSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_SchemaSnapshots_MonitorId_CapturedAt",
                table: "SchemaSnapshots",
                columns: new[] { "MonitorId", "CapturedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractChanges");

            migrationBuilder.DropTable(
                name: "SchemaSnapshots");
        }
    }
}
