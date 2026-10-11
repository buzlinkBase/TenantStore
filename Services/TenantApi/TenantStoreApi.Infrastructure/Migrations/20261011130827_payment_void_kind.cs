using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantStoreApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class payment_void_kind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VoidKind",
                table: "SubscriptionPayments",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VoidKind",
                table: "SubscriptionPayments");
        }
    }
}
