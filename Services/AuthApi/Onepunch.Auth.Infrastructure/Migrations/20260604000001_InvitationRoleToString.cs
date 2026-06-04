using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Onepunch.Auth.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InvitationRoleToString : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Role",
                table: "Invitations",
                type: "longtext",
                nullable: false,
                defaultValue: "Member",
                oldClrType: typeof(Guid),
                oldType: "char(36)")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("Relational:Collation", "ascii_general_ci");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "Role",
                table: "Invitations",
                type: "char(36)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("Relational:Collation", "ascii_general_ci")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
