using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Adms.api.Migrations
{
    /// <inheritdoc />
    public partial class init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Attendances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    BioId = table.Column<int>(type: "int", nullable: false),
                    WorkDateTime = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SN = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IP = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DeviceName = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Workstate = table.Column<int>(type: "int", nullable: false),
                    Verifycode = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BatchCode = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Status = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attendances", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "Attendances",
                columns: new[] { "Id", "BatchCode", "BioId", "CreatedAt", "DeletedAt", "DeviceName", "IP", "SN", "Status", "TenantId", "UpdatedAt", "Verifycode", "WorkDateTime", "Workstate" },
                values: new object[,]
                {
                    { new Guid("09da0f2d-e5de-43cb-a3cc-2de8e06e8c49"), "", 1, new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "", "127.0.0.1", "", "Active", new Guid("f2a115bb-c35f-48d7-b42a-0d5dc5f181a4"), null, "", new DateTime(2025, 3, 22, 5, 44, 56, 0, DateTimeKind.Unspecified), 1 },
                    { new Guid("3a2ab99e-602d-46b9-8554-809bc8f0bbdb"), "", 1, new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "", "127.0.0.1", "", "Active", new Guid("f2a115bb-c35f-48d7-b42a-0d5dc5f181a4"), null, "", new DateTime(2025, 11, 14, 5, 12, 47, 0, DateTimeKind.Unspecified), 1 },
                    { new Guid("6ae557ea-703d-41ee-ad15-f3aaa97260a1"), "", 1, new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "", "127.0.0.1", "", "Active", new Guid("f2a115bb-c35f-48d7-b42a-0d5dc5f181a4"), null, "", new DateTime(2025, 11, 24, 17, 53, 17, 0, DateTimeKind.Unspecified), 1 },
                    { new Guid("71f2fce5-05dd-46af-bcbb-f39521ac2fa3"), "", 1, new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "", "127.0.0.1", "", "Active", new Guid("f2a115bb-c35f-48d7-b42a-0d5dc5f181a4"), null, "", new DateTime(2025, 7, 31, 18, 22, 49, 0, DateTimeKind.Unspecified), 1 },
                    { new Guid("952ed55c-8312-444a-aced-9960597f7fdf"), "", 1, new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "", "127.0.0.1", "", "Active", new Guid("f2a115bb-c35f-48d7-b42a-0d5dc5f181a4"), null, "", new DateTime(2025, 12, 21, 18, 3, 25, 0, DateTimeKind.Unspecified), 1 },
                    { new Guid("b2f7371c-747f-4435-b5f2-ae76779bbdeb"), "", 1, new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "", "127.0.0.1", "", "Active", new Guid("f2a115bb-c35f-48d7-b42a-0d5dc5f181a4"), null, "", new DateTime(2025, 8, 10, 5, 0, 2, 0, DateTimeKind.Unspecified), 1 },
                    { new Guid("b6f3b4a3-61b3-4d78-8792-4d72d2c01bc8"), "", 1, new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "", "127.0.0.1", "", "Active", new Guid("f2a115bb-c35f-48d7-b42a-0d5dc5f181a4"), null, "", new DateTime(2025, 12, 19, 18, 57, 40, 0, DateTimeKind.Unspecified), 1 },
                    { new Guid("cdb2dc7d-d1b1-4494-9afb-1dd95efb2116"), "", 1, new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "", "127.0.0.1", "", "Active", new Guid("f2a115bb-c35f-48d7-b42a-0d5dc5f181a4"), null, "", new DateTime(2025, 4, 25, 17, 23, 16, 0, DateTimeKind.Unspecified), 1 },
                    { new Guid("eafaba07-33ae-4ded-aa7b-1adb274d54dc"), "", 1, new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "", "127.0.0.1", "", "Active", new Guid("f2a115bb-c35f-48d7-b42a-0d5dc5f181a4"), null, "", new DateTime(2025, 7, 24, 5, 14, 56, 0, DateTimeKind.Unspecified), 1 },
                    { new Guid("fa0f0948-f426-401d-a017-15bd976d15e5"), "", 1, new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "", "127.0.0.1", "", "Active", new Guid("f2a115bb-c35f-48d7-b42a-0d5dc5f181a4"), null, "", new DateTime(2025, 12, 17, 18, 12, 18, 0, DateTimeKind.Unspecified), 1 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Attendances");
        }
    }
}
