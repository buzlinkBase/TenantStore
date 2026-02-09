using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantStoreApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class changetenantmodel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Description",
                table: "Tenants",
                newName: "Email");

            migrationBuilder.RenameColumn(
                name: "Code",
                table: "Tenants",
                newName: "CompanyName");

            migrationBuilder.UpdateData(
                table: "Tenants",
                keyColumn: "Id",
                keyValue: new Guid("c1b8aaaf-6bff-4f68-97c7-626f16ea9197"),
                columns: new[] { "CompanyName", "Email" },
                values: new object[] { "Tenant 1", "test@gmail.com" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Email",
                table: "Tenants",
                newName: "Description");

            migrationBuilder.RenameColumn(
                name: "CompanyName",
                table: "Tenants",
                newName: "Code");

            migrationBuilder.UpdateData(
                table: "Tenants",
                keyColumn: "Id",
                keyValue: new Guid("c1b8aaaf-6bff-4f68-97c7-626f16ea9197"),
                columns: new[] { "Code", "Description" },
                values: new object[] { "0001", "Tenant 1" });
        }
    }
}
