using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantStoreApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class addtoken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Connections",
                keyColumn: "Id",
                keyValue: new Guid("c1b8aaaf-6bff-4f68-97c7-626f16ea9197"));

            migrationBuilder.DeleteData(
                table: "Tenants",
                keyColumn: "Id",
                keyValue: new Guid("c1b8aaaf-6bff-4f68-97c7-626f16ea9197"));

            migrationBuilder.AddColumn<string>(
                name: "Token",
                table: "Tenants",
                type: "varchar(255)",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Token",
                table: "Tenants",
                column: "Token");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tenants_Token",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Token",
                table: "Tenants");

            migrationBuilder.InsertData(
                table: "Connections",
                columns: new[] { "Id", "ConnetionString", "TenantId" },
                values: new object[] { new Guid("c1b8aaaf-6bff-4f68-97c7-626f16ea9197"), "server=127.0.0.1;port=3316;database=tenantstore;user=oneuser;password=Pokemon67584321", new Guid("c1b8aaaf-6bff-4f68-97c7-626f16ea9197") });

            migrationBuilder.InsertData(
                table: "Tenants",
                columns: new[] { "Id", "CompanyName", "CreatedAt", "DeletedAt", "Email", "Status", "TenantId", "UpdatedAt" },
                values: new object[] { new Guid("c1b8aaaf-6bff-4f68-97c7-626f16ea9197"), "Tenant 1", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "test@gmail.com", "Active", new Guid("00000000-0000-0000-0000-000000000000"), null });
        }
    }
}
