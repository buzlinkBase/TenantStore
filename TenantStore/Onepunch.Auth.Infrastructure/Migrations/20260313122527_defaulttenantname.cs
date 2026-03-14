using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Onepunch.Auth.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class defaulttenantname : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultTenantName",
                table: "AspNetUsers",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultTenantName",
                table: "AspNetUsers");
        }
    }
}
