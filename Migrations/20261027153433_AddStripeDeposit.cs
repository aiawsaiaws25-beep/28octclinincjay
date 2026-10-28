using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LakesideMedical.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeDeposit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_Doctor_AppointmentDateTime",
                table: "Appointments");

            migrationBuilder.Sql("""
                IF COL_LENGTH(N'dbo.Appointments', N'stripeSessionId') IS NULL
                    ALTER TABLE [dbo].[Appointments] ADD [stripeSessionId] nvarchar(255) NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_Doctor_AppointmentDateTime",
                table: "Appointments",
                columns: new[] { "Doctor", "AppointmentDateTime" },
                unique: true,
                filter: "[Status] IN (N'Booked', N'Pending')");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_stripeSessionId",
                table: "Appointments",
                column: "stripeSessionId",
                unique: true,
                filter: "[stripeSessionId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_Doctor_AppointmentDateTime",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_stripeSessionId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "stripeSessionId",
                table: "Appointments");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_Doctor_AppointmentDateTime",
                table: "Appointments",
                columns: new[] { "Doctor", "AppointmentDateTime" },
                unique: true,
                filter: "[Status] = N'Booked'");
        }
    }
}
