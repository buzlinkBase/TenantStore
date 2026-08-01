using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantStoreApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMembershipRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MembershipRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserMembershipId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Role = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Status = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MembershipRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MembershipRoles_Memberships_UserMembershipId",
                        column: x => x.UserMembershipId,
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // Carry each membership's existing single Role value over into the new table before
            // the old column is dropped, so no existing role assignment is lost.
            migrationBuilder.Sql(
                "INSERT INTO MembershipRoles (Id, UserMembershipId, Role, Status, CreatedAt) " +
                "SELECT UUID(), Id, Role, 'Active', CreatedAt FROM Memberships " +
                "WHERE Role IS NOT NULL AND Role <> '';");

            migrationBuilder.DropIndex(
                name: "IX_Memberships_Role",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "Memberships");

            migrationBuilder.CreateIndex(
                name: "IX_MembershipRoles_UserMembershipId_Role",
                table: "MembershipRoles",
                columns: new[] { "UserMembershipId", "Role" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "Memberships",
                type: "varchar(255)",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            // Best-effort: collapse back to a single role per membership (first alphabetically).
            migrationBuilder.Sql(
                "UPDATE Memberships m SET Role = (" +
                "SELECT mr.Role FROM MembershipRoles mr WHERE mr.UserMembershipId = m.Id " +
                "ORDER BY mr.Role LIMIT 1);");

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_Role",
                table: "Memberships",
                column: "Role");

            migrationBuilder.DropTable(
                name: "MembershipRoles");
        }
    }
}
