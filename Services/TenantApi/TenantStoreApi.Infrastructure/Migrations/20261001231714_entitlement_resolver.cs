using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantStoreApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class entitlement_resolver : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ActivatedAtUtc",
                table: "Tenants",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_UserId_ActivatedAtUtc",
                table: "Tenants",
                columns: new[] { "UserId", "ActivatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tenants_UserId_ActivatedAtUtc",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ActivatedAtUtc",
                table: "Tenants");
        }
    }
}
