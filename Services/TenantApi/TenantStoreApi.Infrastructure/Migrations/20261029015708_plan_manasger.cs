using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantStoreApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class plan_manasger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnnualPromoLabel",
                table: "Plans",
                type: "varchar(40)",
                maxLength: 40,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e01"),
                column: "AnnualPromoLabel",
                value: null);

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e02"),
                column: "AnnualPromoLabel",
                value: null);

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e03"),
                column: "AnnualPromoLabel",
                value: null);

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: new Guid("d8cb5f8b-8e4e-4352-b41c-c311f73b5ed5"),
                column: "AnnualPromoLabel",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnnualPromoLabel",
                table: "Plans");
        }
    }
}
