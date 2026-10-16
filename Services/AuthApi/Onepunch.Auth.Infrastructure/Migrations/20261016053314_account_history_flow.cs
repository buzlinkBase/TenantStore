using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Onepunch.Auth.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class account_history_flow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "AspNetUsers",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailConfirmedAtUtc",
                table: "AspNetUsers",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstHrisSignInAtUtc",
                table: "AspNetUsers",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstPortalSignInAtUtc",
                table: "AspNetUsers",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "AspNetUsers",
                type: "varchar(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Unknown")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "OriginDetail",
                table: "AspNetUsers",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_CreatedAtUtc",
                table: "AspNetUsers",
                column: "CreatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_CreatedAtUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "EmailConfirmedAtUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "FirstHrisSignInAtUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "FirstPortalSignInAtUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "OriginDetail",
                table: "AspNetUsers");
        }
    }
}
