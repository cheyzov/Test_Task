using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Test_Task.Migrations
{
    /// <inheritdoc />
    public partial class TrackIgnoredReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProviderReceipts_ProviderPaymentId",
                table: "ProviderReceipts");

            migrationBuilder.AddColumn<bool>(
                name: "Ignored",
                table: "ProviderReceipts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderReceipts_ProviderPaymentId_Result",
                table: "ProviderReceipts",
                columns: new[] { "ProviderPaymentId", "Result" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProviderReceipts_ProviderPaymentId_Result",
                table: "ProviderReceipts");

            migrationBuilder.DropColumn(
                name: "Ignored",
                table: "ProviderReceipts");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderReceipts_ProviderPaymentId",
                table: "ProviderReceipts",
                column: "ProviderPaymentId",
                unique: true);
        }
    }
}
