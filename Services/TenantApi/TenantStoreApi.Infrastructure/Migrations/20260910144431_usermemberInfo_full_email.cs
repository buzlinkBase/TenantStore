using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantStoreApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class usermemberInfo_full_email : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FullName",
                table: "Memberships",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                table: "Memberships",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: new Guid("d8cb5f8b-8e4e-4352-b41c-c311f73b5ed5"),
                column: "Days",
                value: 15);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FullName",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "IsHidden",
                table: "Memberships");

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: new Guid("d8cb5f8b-8e4e-4352-b41c-c311f73b5ed5"),
                column: "Days",
                value: 0);
        }
    }
}
