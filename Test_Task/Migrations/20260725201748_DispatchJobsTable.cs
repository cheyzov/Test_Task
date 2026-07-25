using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Test_Task.Migrations
{
    /// <inheritdoc />
    public partial class DispatchJobsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentDispatches_Operations_OperationId",
                table: "PaymentDispatches");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PaymentDispatches",
                table: "PaymentDispatches");

            migrationBuilder.RenameTable(
                name: "PaymentDispatches",
                newName: "DispatchJobs");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DispatchJobs",
                table: "DispatchJobs",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchJobs_OperationId",
                table: "DispatchJobs",
                column: "OperationId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DispatchJobs_Operations_OperationId",
                table: "DispatchJobs",
                column: "OperationId",
                principalTable: "Operations",
                principalColumn: "OperationId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DispatchJobs_Operations_OperationId",
                table: "DispatchJobs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_DispatchJobs",
                table: "DispatchJobs");

            migrationBuilder.DropIndex(
                name: "IX_DispatchJobs_OperationId",
                table: "DispatchJobs");

            migrationBuilder.RenameTable(
                name: "DispatchJobs",
                newName: "PaymentDispatches");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PaymentDispatches",
                table: "PaymentDispatches",
                column: "OperationId");

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentDispatches_Operations_OperationId",
                table: "PaymentDispatches",
                column: "OperationId",
                principalTable: "Operations",
                principalColumn: "OperationId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
