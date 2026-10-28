using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LakesideMedical.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Appointments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Specialty = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Doctor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AppointmentDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PatientName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PatientPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PatientEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Booked"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_AppointmentDateTime",
                table: "Appointments",
                column: "AppointmentDateTime");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_Doctor_AppointmentDateTime",
                table: "Appointments",
                columns: new[] { "Doctor", "AppointmentDateTime" },
                unique: true,
                filter: "[Status] = N'Booked'");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_PatientEmail",
                table: "Appointments",
                column: "PatientEmail");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Appointments");
        }
    }
}
