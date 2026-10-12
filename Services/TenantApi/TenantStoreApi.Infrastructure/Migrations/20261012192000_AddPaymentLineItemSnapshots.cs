using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TenantStoreApi.Infrastructure;

#nullable disable

namespace TenantStoreApi.Infrastructure.Migrations
{
    [DbContext(typeof(TenantContext))]
    [Migration("20261012192000_AddPaymentLineItemSnapshots")]
    public partial class AddPaymentLineItemSnapshots : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LineItemsJson",
                table: "PaymentCheckouts",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "LineItemsJson",
                table: "SubscriptionPayments",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LineItemsJson",
                table: "PaymentCheckouts");

            migrationBuilder.DropColumn(
                name: "LineItemsJson",
                table: "SubscriptionPayments");
        }
    }
}
