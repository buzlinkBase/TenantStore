using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TenantStoreApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class gated_product : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "PlanEntitlements",
                columns: new[] { "Id", "CreatedAt", "DeletedAt", "Key", "PlanId", "Status", "UpdatedAt", "Value" },
                values: new object[,]
                {
                    { new Guid("14589e8f-5668-12a4-3081-d87e183398e5"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "hrms.core", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e03"), "Active", null, "true" },
                    { new Guid("849db7e2-8ecf-2777-e12e-faac06162d64"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "hrms.core", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e02"), "Active", null, "true" },
                    { new Guid("c930cadc-6b65-f7d4-f770-37283cc4452a"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "hrms.core", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e01"), "Active", null, "true" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumn: "Id",
                keyValue: new Guid("14589e8f-5668-12a4-3081-d87e183398e5"));

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumn: "Id",
                keyValue: new Guid("849db7e2-8ecf-2777-e12e-faac06162d64"));

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumn: "Id",
                keyValue: new Guid("c930cadc-6b65-f7d4-f770-37283cc4452a"));
        }
    }
}
