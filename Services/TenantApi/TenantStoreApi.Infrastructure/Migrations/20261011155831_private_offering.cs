using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TenantStoreApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class private_offering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowPublicOffering",
                table: "AccountSubscriptions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.InsertData(
                table: "PlanEntitlements",
                columns: new[] { "Id", "CreatedAt", "DeletedAt", "Key", "PlanId", "Status", "UpdatedAt", "Value" },
                values: new object[,]
                {
                    { new Guid("0b748695-57c2-8d9e-83e8-cf19ae016274"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "timekeeping.mobile", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e03"), "Active", null, "true" },
                    { new Guid("434f9566-3b0e-0947-d1e8-e6e6c9114e70"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "patrol.app", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e02"), "Active", null, "false" },
                    { new Guid("9ab8414c-585b-bc2b-ea82-09cf0a5937e2"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "timekeeping.mobile", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e02"), "Active", null, "false" },
                    { new Guid("bf9ccf43-0286-bde1-b35b-7d6a649d3122"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "timekeeping.mobile", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e01"), "Active", null, "false" },
                    { new Guid("df0c2e61-d9ad-c76c-a2c7-6780931a1658"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "patrol.app", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e03"), "Active", null, "true" },
                    { new Guid("ec76c76b-2986-af18-72b3-e74d83764b7d"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "patrol.app", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e01"), "Active", null, "false" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumn: "Id",
                keyValue: new Guid("0b748695-57c2-8d9e-83e8-cf19ae016274"));

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumn: "Id",
                keyValue: new Guid("434f9566-3b0e-0947-d1e8-e6e6c9114e70"));

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumn: "Id",
                keyValue: new Guid("9ab8414c-585b-bc2b-ea82-09cf0a5937e2"));

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumn: "Id",
                keyValue: new Guid("bf9ccf43-0286-bde1-b35b-7d6a649d3122"));

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumn: "Id",
                keyValue: new Guid("df0c2e61-d9ad-c76c-a2c7-6780931a1658"));

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumn: "Id",
                keyValue: new Guid("ec76c76b-2986-af18-72b3-e74d83764b7d"));

            migrationBuilder.DropColumn(
                name: "ShowPublicOffering",
                table: "AccountSubscriptions");
        }
    }
}
