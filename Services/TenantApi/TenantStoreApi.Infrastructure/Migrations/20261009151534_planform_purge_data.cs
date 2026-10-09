using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantStoreApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class planform_purge_data : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArchiveRetainUntilUtc",
                table: "AccountSubscriptions",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DataPurgedAtUtc",
                table: "AccountSubscriptions",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LegalHold",
                table: "AccountSubscriptions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LegalHoldReason",
                table: "AccountSubscriptions",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "PurgeState",
                table: "AccountSubscriptions",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "None")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AccountSubscriptions_PurgeAfterUtc",
                table: "AccountSubscriptions",
                column: "PurgeAfterUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccountSubscriptions_PurgeAfterUtc",
                table: "AccountSubscriptions");

            migrationBuilder.DropColumn(
                name: "ArchiveRetainUntilUtc",
                table: "AccountSubscriptions");

            migrationBuilder.DropColumn(
                name: "DataPurgedAtUtc",
                table: "AccountSubscriptions");

            migrationBuilder.DropColumn(
                name: "LegalHold",
                table: "AccountSubscriptions");

            migrationBuilder.DropColumn(
                name: "LegalHoldReason",
                table: "AccountSubscriptions");

            migrationBuilder.DropColumn(
                name: "PurgeState",
                table: "AccountSubscriptions");
        }
    }
}
