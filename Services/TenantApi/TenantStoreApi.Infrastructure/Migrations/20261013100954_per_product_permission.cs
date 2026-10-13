using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TenantStoreApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class per_product_permission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Product",
                table: "Roles",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Product",
                table: "Permissions",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "PlanEntitlements",
                columns: new[] { "Id", "CreatedAt", "DeletedAt", "Key", "PlanId", "Status", "UpdatedAt", "Value" },
                values: new object[,]
                {
                    { new Guid("39234f97-ab2c-7cd5-bf4f-63f79ae05985"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "procurement.core", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e01"), "Active", null, "false" },
                    { new Guid("3cfca504-a7af-4c34-6a1a-00cb3498b892"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "accounting.core", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e03"), "Active", null, "false" },
                    { new Guid("50699d44-f46f-dad3-211d-f28fc42fc0a1"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "accounting.core", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e02"), "Active", null, "false" },
                    { new Guid("5879d012-6b21-3c30-37a8-3c3bbb2685d4"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "billing.core", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e03"), "Active", null, "false" },
                    { new Guid("68b0c1cb-39e6-de26-1923-344252e3740c"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "procurement.core", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e02"), "Active", null, "false" },
                    { new Guid("6c5a4763-72e4-7472-81b6-404812d6aea7"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "billing.core", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e01"), "Active", null, "false" },
                    { new Guid("9097a18a-166d-c350-2d3e-dabb3ce28874"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "clientportal.core", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e02"), "Active", null, "false" },
                    { new Guid("a919429c-92cd-c8de-cb62-e06e21f792d1"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "clientportal.core", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e03"), "Active", null, "false" },
                    { new Guid("a936e203-573d-00c7-7bfb-f42ad8996ccc"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "billing.core", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e02"), "Active", null, "false" },
                    { new Guid("bd1d1ebf-b040-b2f6-28c7-6a3c01953561"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "clientportal.core", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e01"), "Active", null, "false" },
                    { new Guid("c06d1022-45cc-d10c-107d-261cb0e3d1b1"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "accounting.core", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e01"), "Active", null, "false" },
                    { new Guid("f59ba6d7-0c06-4299-f709-41932839ff12"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "procurement.core", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e03"), "Active", null, "false" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_Product",
                table: "Permissions",
                column: "Product");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Permissions_Product",
                table: "Permissions");

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumn: "Id",
                keyValue: new Guid("39234f97-ab2c-7cd5-bf4f-63f79ae05985"));

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumn: "Id",
                keyValue: new Guid("3cfca504-a7af-4c34-6a1a-00cb3498b892"));

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumn: "Id",
                keyValue: new Guid("50699d44-f46f-dad3-211d-f28fc42fc0a1"));

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumn: "Id",
                keyValue: new Guid("5879d012-6b21-3c30-37a8-3c3bbb2685d4"));

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumn: "Id",
                keyValue: new Guid("68b0c1cb-39e6-de26-1923-344252e3740c"));

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumn: "Id",
                keyValue: new Guid("6c5a4763-72e4-7472-81b6-404812d6aea7"));

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumn: "Id",
                keyValue: new Guid("9097a18a-166d-c350-2d3e-dabb3ce28874"));

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumn: "Id",
                keyValue: new Guid("a919429c-92cd-c8de-cb62-e06e21f792d1"));

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumn: "Id",
                keyValue: new Guid("a936e203-573d-00c7-7bfb-f42ad8996ccc"));

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumn: "Id",
                keyValue: new Guid("bd1d1ebf-b040-b2f6-28c7-6a3c01953561"));

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumn: "Id",
                keyValue: new Guid("c06d1022-45cc-d10c-107d-261cb0e3d1b1"));

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumn: "Id",
                keyValue: new Guid("f59ba6d7-0c06-4299-f709-41932839ff12"));

            migrationBuilder.DropColumn(
                name: "Product",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "Product",
                table: "Permissions");
        }
    }
}
