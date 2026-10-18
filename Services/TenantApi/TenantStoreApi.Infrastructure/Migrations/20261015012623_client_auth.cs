using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantStoreApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class client_auth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Roles_TenantId_Name",
                table: "Roles");

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Tenants",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Contact",
                table: "Tenants",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Audience",
                table: "Roles",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Staff")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "ClientEntityId",
                table: "Roles",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Memberships",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "InternalEmployee")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientEntities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ExternalId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Status = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientEntities", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientMembershipRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserMembershipId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientEntityId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RoleId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Status = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientMembershipRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientMembershipRoles_ClientEntities_ClientEntityId",
                        column: x => x.ClientEntityId,
                        principalTable: "ClientEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClientMembershipRoles_Memberships_UserMembershipId",
                        column: x => x.UserMembershipId,
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClientMembershipRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_ClientEntityId",
                table: "Roles",
                column: "ClientEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_TenantId_ClientEntityId_Name",
                table: "Roles",
                columns: new[] { "TenantId", "ClientEntityId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_TenantId_Category",
                table: "Memberships",
                columns: new[] { "TenantId", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientEntities_TenantId",
                table: "ClientEntities",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientEntities_TenantId_ExternalId",
                table: "ClientEntities",
                columns: new[] { "TenantId", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientMembershipRoles_ClientEntityId",
                table: "ClientMembershipRoles",
                column: "ClientEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientMembershipRoles_RoleId",
                table: "ClientMembershipRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientMembershipRoles_UserMembershipId_ClientEntityId_RoleId",
                table: "ClientMembershipRoles",
                columns: new[] { "UserMembershipId", "ClientEntityId", "RoleId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientMembershipRoles");

            migrationBuilder.DropTable(
                name: "ClientEntities");

            migrationBuilder.DropIndex(
                name: "IX_Roles_ClientEntityId",
                table: "Roles");

            migrationBuilder.DropIndex(
                name: "IX_Roles_TenantId_ClientEntityId_Name",
                table: "Roles");

            migrationBuilder.DropIndex(
                name: "IX_Memberships_TenantId_Category",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Contact",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Audience",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "ClientEntityId",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Memberships");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_TenantId_Name",
                table: "Roles",
                columns: new[] { "TenantId", "Name" },
                unique: true);
        }
    }
}
