using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Onepunch.Auth.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MultiRoleTenantMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Invitations.Role -> Roles: a straight rename, no data migration needed — the
            // existing single-role string (e.g. "Member") is already valid input for the
            // List<string> comma-delimited converter (a value with no commas is a one-item list).
            migrationBuilder.RenameColumn(
                name: "Role",
                table: "Invitations",
                newName: "Roles");

            // AspNetUsers.DefaultTenantRole -> DefaultTenantRoles: different column types force an
            // add-then-drop rather than a rename, so carry the existing value over explicitly
            // before dropping the old column.
            migrationBuilder.AddColumn<string>(
                name: "DefaultTenantRoles",
                table: "AspNetUsers",
                type: "longtext",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql(
                "UPDATE AspNetUsers SET DefaultTenantRoles = DefaultTenantRole " +
                "WHERE DefaultTenantRole IS NOT NULL AND DefaultTenantRole <> '';");

            migrationBuilder.DropColumn(
                name: "DefaultTenantRole",
                table: "AspNetUsers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultTenantRole",
                table: "AspNetUsers",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            // Best-effort: collapse back to a single role (first entry before any comma).
            migrationBuilder.Sql(
                "UPDATE AspNetUsers SET DefaultTenantRole = " +
                "SUBSTRING_INDEX(DefaultTenantRoles, ',', 1) " +
                "WHERE DefaultTenantRoles IS NOT NULL AND DefaultTenantRoles <> '';");

            migrationBuilder.DropColumn(
                name: "DefaultTenantRoles",
                table: "AspNetUsers");

            migrationBuilder.RenameColumn(
                name: "Roles",
                table: "Invitations",
                newName: "Role");
        }
    }
}
