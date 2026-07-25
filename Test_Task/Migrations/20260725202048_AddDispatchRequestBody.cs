using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Test_Task.Migrations
{
    /// <inheritdoc />
    public partial class AddDispatchRequestBody : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequestBody",
                table: "DispatchJobs",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestBody",
                table: "DispatchJobs");
        }
    }
}
