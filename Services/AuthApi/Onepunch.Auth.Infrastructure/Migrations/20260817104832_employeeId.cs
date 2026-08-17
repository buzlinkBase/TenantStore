using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Onepunch.Auth.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class employeeId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EmployeeId",
                table: "Invitations",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmployeeId",
                table: "Invitations");
        }
    }
}
