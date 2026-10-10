using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TenantStoreApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class storage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "PaymentCheckouts",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Plan")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "StorageAddOnPaymentId",
                table: "PaymentCheckouts",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<int>(
                name: "StorageBlocks",
                table: "PaymentCheckouts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StorageOfferingId",
                table: "PaymentCheckouts",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateTable(
                name: "StorageAddOnOfferings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    BlockMb = table.Column<int>(type: "int", nullable: false),
                    MonthlyPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AnnualPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PricesIncludeVat = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsPublic = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Status = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageAddOnOfferings", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StorageAddOnPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SubscriptionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AddOnId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Blocks = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Method = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reference = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PaidAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    PeriodStartUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    PeriodEndUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RecordedBy = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Status = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageAddOnPayments", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StorageAddOnOffers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    OfferingId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    OwnerUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Status = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageAddOnOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StorageAddOnOffers_StorageAddOnOfferings_OfferingId",
                        column: x => x.OfferingId,
                        principalTable: "StorageAddOnOfferings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StorageAddOns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SubscriptionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    OfferingId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Blocks = table.Column<int>(type: "int", nullable: false),
                    BlockMb = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitAnnualPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsComplimentary = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PendingBlocks = table.Column<int>(type: "int", nullable: true),
                    PendingEffectiveAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Reason = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedBy = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Status = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageAddOns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StorageAddOns_AccountSubscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "AccountSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StorageAddOns_StorageAddOnOfferings_OfferingId",
                        column: x => x.OfferingId,
                        principalTable: "StorageAddOnOfferings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "PlanEntitlements",
                columns: new[] { "Id", "CreatedAt", "DeletedAt", "Key", "PlanId", "Status", "UpdatedAt", "Value" },
                values: new object[,]
                {
                    { new Guid("790d3739-0bf3-f39f-6126-92a8f112348d"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "storage.max_mb", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e03"), "Active", null, "102400" },
                    { new Guid("b1281ba5-d358-96ab-f7ce-206097e6a1b5"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "storage.max_mb", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e02"), "Active", null, "20480" },
                    { new Guid("f28f01be-7f41-7a21-a06b-85df12915e76"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "storage.max_mb", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e01"), "Active", null, "5120" }
                });

            migrationBuilder.InsertData(
                table: "StorageAddOnOfferings",
                columns: new[] { "Id", "AnnualPrice", "BlockMb", "Code", "CreatedAt", "Currency", "DeletedAt", "Description", "IsActive", "IsPublic", "MonthlyPrice", "Name", "PricesIncludeVat", "SortOrder", "Status", "UpdatedAt" },
                values: new object[] { new Guid("5a0e1c3d-2b4f-4a6c-8d7e-0f1a2b3c4d01"), 4000m, 51200, "storage-50gb", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "PHP", null, "Adds 50 GB of file storage to your account, shared by all your companies and billed with your plan.", true, true, 400m, "50 GB storage block", true, 1, "Active", null });

            migrationBuilder.CreateIndex(
                name: "IX_StorageAddOnOfferings_Code",
                table: "StorageAddOnOfferings",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorageAddOnOffers_OfferingId_OwnerUserId",
                table: "StorageAddOnOffers",
                columns: new[] { "OfferingId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorageAddOnOffers_OwnerUserId",
                table: "StorageAddOnOffers",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StorageAddOnPayments_SubscriptionId_PaidAtUtc",
                table: "StorageAddOnPayments",
                columns: new[] { "SubscriptionId", "PaidAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StorageAddOns_OfferingId",
                table: "StorageAddOns",
                column: "OfferingId");

            migrationBuilder.CreateIndex(
                name: "IX_StorageAddOns_SubscriptionId",
                table: "StorageAddOns",
                column: "SubscriptionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StorageAddOnOffers");

            migrationBuilder.DropTable(
                name: "StorageAddOnPayments");

            migrationBuilder.DropTable(
                name: "StorageAddOns");

            migrationBuilder.DropTable(
                name: "StorageAddOnOfferings");

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumn: "Id",
                keyValue: new Guid("790d3739-0bf3-f39f-6126-92a8f112348d"));

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumn: "Id",
                keyValue: new Guid("b1281ba5-d358-96ab-f7ce-206097e6a1b5"));

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumn: "Id",
                keyValue: new Guid("f28f01be-7f41-7a21-a06b-85df12915e76"));

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "PaymentCheckouts");

            migrationBuilder.DropColumn(
                name: "StorageAddOnPaymentId",
                table: "PaymentCheckouts");

            migrationBuilder.DropColumn(
                name: "StorageBlocks",
                table: "PaymentCheckouts");

            migrationBuilder.DropColumn(
                name: "StorageOfferingId",
                table: "PaymentCheckouts");
        }
    }
}
