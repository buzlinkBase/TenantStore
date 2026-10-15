using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Onepunch.Auth.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class auth_tenant_update : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Audience",
                table: "RefreshTokens",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Invitations",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Staff")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "ClientEntityId",
                table: "Invitations",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "ClientName",
                table: "Invitations",
                type: "varchar(256)",
                maxLength: 256,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ClientRoleIds",
                table: "Invitations",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "PortalOnly",
                table: "AspNetUsers",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_ClientEntityId_Status",
                table: "Invitations",
                columns: new[] { "ClientEntityId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invitations_ClientEntityId_Status",
                table: "Invitations");

            migrationBuilder.DropColumn(
                name: "Audience",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Invitations");

            migrationBuilder.DropColumn(
                name: "ClientEntityId",
                table: "Invitations");

            migrationBuilder.DropColumn(
                name: "ClientName",
                table: "Invitations");

            migrationBuilder.DropColumn(
                name: "ClientRoleIds",
                table: "Invitations");

            migrationBuilder.DropColumn(
                name: "PortalOnly",
                table: "AspNetUsers");
        }
    }
}
