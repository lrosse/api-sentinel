using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiSentinel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitorScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Enabled",
                table: "Monitors",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "IntervalSeconds",
                table: "Monitors",
                type: "int",
                nullable: false,
                defaultValue: 300);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Enabled",
                table: "Monitors");

            migrationBuilder.DropColumn(
                name: "IntervalSeconds",
                table: "Monitors");
        }
    }
}
