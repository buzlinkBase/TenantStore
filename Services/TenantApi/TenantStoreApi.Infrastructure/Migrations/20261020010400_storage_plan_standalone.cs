using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantStoreApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class storage_plan_standalone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BillingCycle",
                table: "StorageAddOnPayments",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "StorageAddOnPayments",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Purchase")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "LineItemsJson",
                table: "StorageAddOnPayments",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "VoidKind",
                table: "StorageAddOnPayments",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "StorageAddOnPayments",
                type: "varchar(512)",
                maxLength: 512,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAtUtc",
                table: "StorageAddOnPayments",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoidedBy",
                table: "StorageAddOnPayments",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "MandateId",
                table: "PaymentCheckouts",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "ProviderIntentId",
                table: "PaymentCheckouts",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AutoRenewMandates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SubscriptionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    OwnerUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Target = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Provider = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProviderCustomerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProviderSubscriptionId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProviderPlanId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BillingCycle = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PaymentMethodLabel = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SetupCheckoutId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ActivatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    NextChargeAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CanceledAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CancelReason = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CanceledBy = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastFailureAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastFailureReason = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedBy = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutoRenewMandates", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StorageSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SubscriptionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BillingCycle = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PendingBillingCycle = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CurrentPeriodStartUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CurrentPeriodEndUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    GraceEndsAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CancelAtPeriodEnd = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StorageSubscriptions_AccountSubscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "AccountSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "StorageAddOnOfferings",
                keyColumn: "Id",
                keyValue: new Guid("5a0e1c3d-2b4f-4a6c-8d7e-0f1a2b3c4d01"),
                column: "Description",
                value: "Adds 50 GB of file storage to your account, shared by all your companies and billed on its own cycle.");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCheckouts_MandateId",
                table: "PaymentCheckouts",
                column: "MandateId");

            migrationBuilder.CreateIndex(
                name: "IX_AutoRenewMandates_OwnerUserId",
                table: "AutoRenewMandates",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AutoRenewMandates_Provider_ProviderSubscriptionId",
                table: "AutoRenewMandates",
                columns: new[] { "Provider", "ProviderSubscriptionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AutoRenewMandates_SubscriptionId_Target_Status",
                table: "AutoRenewMandates",
                columns: new[] { "SubscriptionId", "Target", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StorageSubscriptions_Status",
                table: "StorageSubscriptions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_StorageSubscriptions_SubscriptionId",
                table: "StorageSubscriptions",
                column: "SubscriptionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutoRenewMandates");

            migrationBuilder.DropTable(
                name: "StorageSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_PaymentCheckouts_MandateId",
                table: "PaymentCheckouts");

            migrationBuilder.DropColumn(
                name: "BillingCycle",
                table: "StorageAddOnPayments");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "StorageAddOnPayments");

            migrationBuilder.DropColumn(
                name: "LineItemsJson",
                table: "StorageAddOnPayments");

            migrationBuilder.DropColumn(
                name: "VoidKind",
                table: "StorageAddOnPayments");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "StorageAddOnPayments");

            migrationBuilder.DropColumn(
                name: "VoidedAtUtc",
                table: "StorageAddOnPayments");

            migrationBuilder.DropColumn(
                name: "VoidedBy",
                table: "StorageAddOnPayments");

            migrationBuilder.DropColumn(
                name: "MandateId",
                table: "PaymentCheckouts");

            migrationBuilder.DropColumn(
                name: "ProviderIntentId",
                table: "PaymentCheckouts");

            migrationBuilder.UpdateData(
                table: "StorageAddOnOfferings",
                keyColumn: "Id",
                keyValue: new Guid("5a0e1c3d-2b4f-4a6c-8d7e-0f1a2b3c4d01"),
                column: "Description",
                value: "Adds 50 GB of file storage to your account, shared by all your companies and billed with your plan.");
        }
    }
}
