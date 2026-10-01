using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TenantStoreApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class plan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Plans",
                type: "varchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(255)")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Plans",
                type: "varchar(512)",
                maxLength: 512,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "AnnualPrice",
                table: "Plans",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Plans",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Plans",
                type: "varchar(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "Plans",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsRecommended",
                table: "Plans",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyPrice",
                table: "Plans",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "PricesIncludeVat",
                table: "Plans",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Plans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "AccountSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    OwnerUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PlanId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SubStatus = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BillingCycle = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TrialEndsAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    TrialExtended = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CurrentPeriodStartUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CurrentPeriodEndUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    GraceEndsAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CanceledAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PurgeAfterUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CancelAtPeriodEnd = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PendingPlanId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    PendingBillingCycle = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PendingChangeTiming = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PendingChangeEffectiveAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LockedMonthlyPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    LockedAnnualPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Status = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountSubscriptions_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "BillingEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SubscriptionId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    OwnerUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Type = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PayloadJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DedupeKey = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Actor = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Status = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingEvents", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PlanEntitlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PlanId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Status = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanEntitlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanEntitlements_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SubscriptionDiscounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SubscriptionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Percent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    FixedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Reason = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ValidUntilUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
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
                    table.PrimaryKey("PK_SubscriptionDiscounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionDiscounts_AccountSubscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "AccountSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SubscriptionOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SubscriptionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reason = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
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
                    table.PrimaryKey("PK_SubscriptionOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionOverrides_AccountSubscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "AccountSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SubscriptionPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SubscriptionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PlanId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    BillingCycle = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
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
                    Notes = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Status = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionPayments_AccountSubscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "AccountSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: new Guid("d8cb5f8b-8e4e-4352-b41c-c311f73b5ed5"),
                columns: new[] { "AnnualPrice", "Code", "Currency", "IsPublic", "IsRecommended", "MonthlyPrice", "PricesIncludeVat", "SortOrder" },
                values: new object[] { 0m, "legacy-free-trial", "PHP", false, false, 0m, true, 0 });

            migrationBuilder.InsertData(
                table: "Plans",
                columns: new[] { "Id", "AnnualPrice", "Code", "CreatedAt", "Currency", "Days", "DeletedAt", "Description", "IsPublic", "IsRecommended", "MonthlyPrice", "Name", "PricesIncludeVat", "SortOrder", "Status", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e01"), 19990m, "timekeeping", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "PHP", 0, null, "Biometrics, attendance, schedules, DTR and leave/overtime applications.", true, false, 1999m, "Timekeeping", true, 1, "Active", null },
                    { new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e02"), 49990m, "payroll", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "PHP", 0, null, "Everything in Timekeeping plus payroll, payslips, loans and government reports.", true, true, 4999m, "Payroll", true, 2, "Active", null },
                    { new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e03"), 99990m, "premium", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "PHP", 0, null, "Everything in Payroll plus multiple companies, analytics, multi-level approvals and the audit trail.", true, false, 9999m, "Premium", true, 3, "Active", null }
                });

            migrationBuilder.InsertData(
                table: "PlanEntitlements",
                columns: new[] { "Id", "CreatedAt", "DeletedAt", "Key", "PlanId", "Status", "UpdatedAt", "Value" },
                values: new object[,]
                {
                    { new Guid("012138b0-e42e-c926-34dc-79b55698b05c"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "payroll.core", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e03"), "Active", null, "true" },
                    { new Guid("0bea1711-f052-99db-d418-a107d616ae97"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "payroll.loans_income", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e03"), "Active", null, "true" },
                    { new Guid("26276651-9e19-be21-66bb-3217ea02d704"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "biometric_devices.max", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e03"), "Active", null, "-1" },
                    { new Guid("26ea238b-30be-16ea-9410-59c4562cd67d"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "analytics", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e01"), "Active", null, "false" },
                    { new Guid("31999e0f-3aaf-8105-5ec8-a2479b747aec"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "audit.trail", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e03"), "Active", null, "true" },
                    { new Guid("381ed073-c67b-4c58-a878-c404bf7fdcb3"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "timekeeping.core", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e03"), "Active", null, "true" },
                    { new Guid("3dd7539d-10cd-5775-14ea-f1930cc7c33b"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "timekeeping.core", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e02"), "Active", null, "true" },
                    { new Guid("44c8a494-6392-d2a5-529d-c7271cc1191b"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "support.tier", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e01"), "Active", null, "email" },
                    { new Guid("486daefd-7a41-7ab4-96d2-d69ebfd2a661"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "companies.max", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e02"), "Active", null, "1" },
                    { new Guid("48e77ec6-c2d0-d891-9ab2-c022fb60dbd7"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "announcements", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e01"), "Active", null, "false" },
                    { new Guid("491512dc-11ec-e81f-e6ac-e9af0c478bd8"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "clients.agency", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e03"), "Active", null, "true" },
                    { new Guid("4a52078f-18e9-285f-c704-f83a3b54e81d"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "admins.max", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e03"), "Active", null, "-1" },
                    { new Guid("4bb612ab-be08-f664-2404-026257de69de"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "admins.max", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e02"), "Active", null, "5" },
                    { new Guid("4ec8498b-3f1c-9b89-ae5c-39d73f3f13e4"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "clients.agency", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e01"), "Active", null, "false" },
                    { new Guid("53b07df3-016b-e6e3-ba82-aa5492f581e1"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "analytics", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e03"), "Active", null, "true" },
                    { new Guid("5facb37e-f38b-d8f5-11d9-8fc2a1d683a2"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "support.tier", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e03"), "Active", null, "priority" },
                    { new Guid("5fdee1db-5fe8-414a-915f-e60175b4f3e1"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "applications.core", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e01"), "Active", null, "true" },
                    { new Guid("610c82de-413f-fded-86c1-5c8d42236678"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "payroll.loans_income", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e02"), "Active", null, "true" },
                    { new Guid("672ebe2c-276c-7fa9-e5b3-85060a5c2405"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "timekeeping.biometric", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e03"), "Active", null, "true" },
                    { new Guid("6f61cdf9-e6f3-6ad4-7a72-45421e447374"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "announcements", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e03"), "Active", null, "true" },
                    { new Guid("6feec5d3-b5a3-d5e9-ff54-b6944b01d25d"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "announcements", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e02"), "Active", null, "false" },
                    { new Guid("75940847-3327-80bd-ccac-493b355cfa84"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "portal.self_service", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e02"), "Active", null, "true" },
                    { new Guid("7917db7c-1205-1b5a-4617-fb4f40c37ab8"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "biometric_devices.max", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e02"), "Active", null, "3" },
                    { new Guid("7ed1599e-f9dc-b50c-08de-ae734eecaab0"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "payroll.loans_income", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e01"), "Active", null, "false" },
                    { new Guid("7f4795f8-862c-561b-c222-6cb8c08cbcbb"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "approvals.multilevel", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e02"), "Active", null, "false" },
                    { new Guid("9761a7cb-2167-6519-af11-4cf2790c1f24"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "timekeeping.biometric", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e02"), "Active", null, "true" },
                    { new Guid("9dabcc7d-9db4-8117-a166-0847dee7de22"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "support.tier", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e02"), "Active", null, "email" },
                    { new Guid("a2a69162-7cb3-8c12-b82b-ea43053ea5ef"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "approvals.multilevel", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e01"), "Active", null, "false" },
                    { new Guid("a409fe0d-e7aa-1d5d-81b1-6357e92437f0"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "timekeeping.biometric", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e01"), "Active", null, "true" },
                    { new Guid("a4a4ed4c-338a-eee7-a949-582b5c7b8977"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "portal.self_service", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e03"), "Active", null, "true" },
                    { new Guid("a5b138ab-4c7a-451a-313a-c520433d705d"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "reports.statutory", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e03"), "Active", null, "true" },
                    { new Guid("a8a3f74b-9139-5543-0644-4620b342d2da"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "timekeeping.core", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e01"), "Active", null, "true" },
                    { new Guid("a8e382b8-1494-a3cf-c63e-8184bd5dd6d4"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "biometric_devices.max", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e01"), "Active", null, "1" },
                    { new Guid("afdf92e6-9f1e-61c1-eec5-aeb597adaf3d"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "applications.core", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e03"), "Active", null, "true" },
                    { new Guid("b663793f-212b-c445-7399-da2dca9459a7"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "clients.agency", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e02"), "Active", null, "false" },
                    { new Guid("bcfb8cfb-311d-b635-0b85-e14b9b173de5"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "audit.trail", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e02"), "Active", null, "false" },
                    { new Guid("bf5b72a9-9685-e136-e70b-a0b103bdc7a6"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "companies.max", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e03"), "Active", null, "5" },
                    { new Guid("c0ad7b68-b5a3-6f0d-98d5-d99187bd1cd0"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "payroll.core", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e01"), "Active", null, "false" },
                    { new Guid("c1114bd6-0138-f31e-220d-515ad6060960"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "employees.max", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e01"), "Active", null, "100" },
                    { new Guid("c42cfb6f-45b9-6a88-ff51-5dd9694358c6"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "applications.core", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e02"), "Active", null, "true" },
                    { new Guid("c7bf93bc-3644-cfd1-3a63-f296e756ccfc"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "audit.trail", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e01"), "Active", null, "false" },
                    { new Guid("c85c10ca-9df6-cf16-52d0-957637aa3336"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "analytics", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e02"), "Active", null, "false" },
                    { new Guid("d14bf202-11b9-bf09-8645-14989ad214de"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "portal.self_service", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e01"), "Active", null, "true" },
                    { new Guid("d505271a-05a1-105f-ee8e-9171ddc2a1fd"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "employees.max", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e03"), "Active", null, "-1" },
                    { new Guid("dd14ef65-9c9d-13fd-ee89-bcfa9a8e7157"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "companies.max", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e01"), "Active", null, "1" },
                    { new Guid("e444878f-d6d2-92be-b4e9-5c44d6fb568b"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "employees.max", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e02"), "Active", null, "2000" },
                    { new Guid("e7ad60e7-4715-2cb5-a17d-744ffa666154"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "reports.statutory", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e01"), "Active", null, "false" },
                    { new Guid("ef17cd12-1c1a-fafe-edb6-4f6bb94f6904"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "payroll.core", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e02"), "Active", null, "true" },
                    { new Guid("ef87a178-e46f-6047-1581-b0912d90aef8"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "approvals.multilevel", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e03"), "Active", null, "true" },
                    { new Guid("f296b266-bde5-f9d4-5e06-8a889fdece73"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "reports.statutory", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e02"), "Active", null, "true" },
                    { new Guid("f513c005-fd94-db38-7a8f-63ee8a354a6c"), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "admins.max", new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e01"), "Active", null, "2" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Plans_Code",
                table: "Plans",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountSubscriptions_OwnerUserId",
                table: "AccountSubscriptions",
                column: "OwnerUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountSubscriptions_PlanId",
                table: "AccountSubscriptions",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountSubscriptions_SubStatus",
                table: "AccountSubscriptions",
                column: "SubStatus");

            migrationBuilder.CreateIndex(
                name: "IX_BillingEvents_DedupeKey",
                table: "BillingEvents",
                column: "DedupeKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingEvents_OwnerUserId",
                table: "BillingEvents",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingEvents_SubscriptionId_OccurredAtUtc",
                table: "BillingEvents",
                columns: new[] { "SubscriptionId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PlanEntitlements_PlanId_Key",
                table: "PlanEntitlements",
                columns: new[] { "PlanId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionDiscounts_SubscriptionId",
                table: "SubscriptionDiscounts",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionOverrides_SubscriptionId_TenantId_Key",
                table: "SubscriptionOverrides",
                columns: new[] { "SubscriptionId", "TenantId", "Key" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_SubscriptionId_PaidAtUtc",
                table: "SubscriptionPayments",
                columns: new[] { "SubscriptionId", "PaidAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingEvents");

            migrationBuilder.DropTable(
                name: "PlanEntitlements");

            migrationBuilder.DropTable(
                name: "SubscriptionDiscounts");

            migrationBuilder.DropTable(
                name: "SubscriptionOverrides");

            migrationBuilder.DropTable(
                name: "SubscriptionPayments");

            migrationBuilder.DropTable(
                name: "AccountSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_Plans_Code",
                table: "Plans");

            migrationBuilder.DeleteData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e01"));

            migrationBuilder.DeleteData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e02"));

            migrationBuilder.DeleteData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: new Guid("4e0b7a52-7c1d-4f3a-9c61-1a2b3c4d5e03"));

            migrationBuilder.DropColumn(
                name: "AnnualPrice",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "IsRecommended",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "MonthlyPrice",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "PricesIncludeVat",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Plans");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Plans",
                type: "varchar(255)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(128)",
                oldMaxLength: 128)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Plans",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(512)",
                oldMaxLength: 512)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
