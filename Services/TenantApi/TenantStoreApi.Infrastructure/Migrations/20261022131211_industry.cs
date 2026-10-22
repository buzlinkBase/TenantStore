using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TenantStoreApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class industry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AcquisitionSource",
                table: "Tenants",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AcquisitionSourceOther",
                table: "Tenants",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "BusinessType",
                table: "Tenants",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "CompanySize",
                table: "Tenants",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "IndustryCode",
                table: "Tenants",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SubIndustryCode",
                table: "Tenants",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Industries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Status = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Industries", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SubIndustries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    IndustryId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Status = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubIndustries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubIndustries_Industries_IndustryId",
                        column: x => x.IndustryId,
                        principalTable: "Industries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "Industries",
                columns: new[] { "Id", "Code", "CreatedAt", "DeletedAt", "IsActive", "Name", "SortOrder", "Status", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("0b19ad66-1d4a-bb69-f6f4-9c50743d4d4e"), "finance", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, true, "Banking, Finance & Insurance", 11, "Active", null },
                    { new Guid("15e76e5d-fbab-e26e-2f53-f105b4a7c1dc"), "bpo", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, true, "BPO & Customer Support", 1, "Active", null },
                    { new Guid("28dfdd3e-a73d-c9a0-f6a4-0e244bd5a89e"), "education", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, true, "Education & Training", 7, "Active", null },
                    { new Guid("48396e3d-e810-e743-4bac-51388eb58ca2"), "manufacturing", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, true, "Manufacturing", 4, "Active", null },
                    { new Guid("48962548-9e47-6268-3b1b-44d5afadfd5e"), "professional-services", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, true, "Professional Services", 12, "Active", null },
                    { new Guid("5637fc5d-96b6-1d03-4c64-8ecec60ab67f"), "security-facilities", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, true, "Security & Facility Services", 9, "Active", null },
                    { new Guid("642a3676-6675-4e65-4ae3-3c535badcdf0"), "government-nonprofit", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, true, "Government & Non-profit", 15, "Active", null },
                    { new Guid("65d09d07-4fde-c160-f89e-5f5b297d3742"), "it-services", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, true, "Information Technology", 10, "Active", null },
                    { new Guid("7c23066f-4054-3e95-e307-32b343823080"), "energy-utilities", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, true, "Energy, Mining & Utilities", 14, "Active", null },
                    { new Guid("8e5403da-fb97-f167-8b2f-55c039cfee27"), "healthcare", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, true, "Healthcare & Wellness", 6, "Active", null },
                    { new Guid("9c229141-46a6-eed9-43a7-6ef63c0df41d"), "logistics", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, true, "Transportation & Logistics", 8, "Active", null },
                    { new Guid("a4cf298f-c8fb-0a83-db4d-9e4655e20b18"), "agriculture", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, true, "Agriculture, Fishery & Forestry", 13, "Active", null },
                    { new Guid("affc10d8-f373-1035-0654-bc86a966dbbf"), "other", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, true, "Other", 16, "Active", null },
                    { new Guid("c175c8cc-8f6e-3b5c-01a7-71e572058fba"), "food-hospitality", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, true, "Food, Hospitality & Tourism", 3, "Active", null },
                    { new Guid("c4c7711f-5d86-b31d-15c6-d901efcba93f"), "retail", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, true, "Retail & Wholesale", 2, "Active", null },
                    { new Guid("e1529023-7092-ee74-a2a8-9363adce9924"), "construction-real-estate", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, true, "Construction & Real Estate", 5, "Active", null }
                });

            migrationBuilder.InsertData(
                table: "SubIndustries",
                columns: new[] { "Id", "Code", "CreatedAt", "DeletedAt", "IndustryId", "IsActive", "Name", "SortOrder", "Status", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("018277a1-5c65-7da5-0247-a64530aa0fbc"), "food-other", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("c175c8cc-8f6e-3b5c-01a7-71e572058fba"), true, "Other", 6, "Active", null },
                    { new Guid("04a6db9e-0f80-b0c6-b620-dea731c4c564"), "bpo-kpo", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("15e76e5d-fbab-e26e-2f53-f105b4a7c1dc"), true, "Knowledge Process Outsourcing", 3, "Active", null },
                    { new Guid("0680e269-b80c-78a9-7334-193700d9a0a5"), "finance-accounting", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("0b19ad66-1d4a-bb69-f6f4-9c50743d4d4e"), true, "Accounting & Auditing", 4, "Active", null },
                    { new Guid("09013d90-e7bc-3ce9-4b37-e34e390e9450"), "construction-property-dev", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("e1529023-7092-ee74-a2a8-9363adce9924"), true, "Property Development", 3, "Active", null },
                    { new Guid("0cd210da-cdac-4c49-4acd-f5b01c91ad7d"), "prof-other", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("48962548-9e47-6268-3b1b-44d5afadfd5e"), true, "Other", 5, "Active", null },
                    { new Guid("0fa0b4d2-8d12-356f-37e8-27875dcc0b47"), "it-infrastructure", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("65d09d07-4fde-c160-f89e-5f5b297d3742"), true, "IT Infrastructure & Support", 2, "Active", null },
                    { new Guid("0fba2836-7e26-b93e-e967-79b5a03da1f9"), "finance-insurance", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("0b19ad66-1d4a-bb69-f6f4-9c50743d4d4e"), true, "Insurance", 3, "Active", null },
                    { new Guid("1687a3d5-6bc2-3704-f5db-094c49d1bd6c"), "food-hotel", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("c175c8cc-8f6e-3b5c-01a7-71e572058fba"), true, "Hotel & Resort", 3, "Active", null },
                    { new Guid("171f918a-ac68-79bc-3746-93c4db675e44"), "logistics-freight", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("9c229141-46a6-eed9-43a7-6ef63c0df41d"), true, "Freight & Courier", 1, "Active", null },
                    { new Guid("1b3a2db4-1ea9-4408-9d11-3391360b38f2"), "security-manpower", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("5637fc5d-96b6-1d03-4c64-8ecec60ab67f"), true, "Manpower Agency", 3, "Active", null },
                    { new Guid("1cd116dc-751d-df06-ff1e-9c9577b7bd8b"), "logistics-warehousing", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("9c229141-46a6-eed9-43a7-6ef63c0df41d"), true, "Warehousing", 2, "Active", null },
                    { new Guid("1d7944ba-c2e1-26ff-8ab3-6498087f2e8d"), "agri-farming", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("a4cf298f-c8fb-0a83-db4d-9e4655e20b18"), true, "Farming & Crops", 1, "Active", null },
                    { new Guid("23565991-947a-0da0-eefd-95b7174c107b"), "food-catering", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("c175c8cc-8f6e-3b5c-01a7-71e572058fba"), true, "Catering", 4, "Active", null },
                    { new Guid("23c172d9-98a2-48b2-028e-e1ecb973e33c"), "finance-bank", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("0b19ad66-1d4a-bb69-f6f4-9c50743d4d4e"), true, "Banking", 1, "Active", null },
                    { new Guid("26982717-c0ae-ff72-9f01-3aa362f612b3"), "energy-mining", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("7c23066f-4054-3e95-e307-32b343823080"), true, "Mining & Quarrying", 2, "Active", null },
                    { new Guid("2d4450c4-02d5-ad30-5578-6c5451a9ad50"), "retail-hardware", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("c4c7711f-5d86-b31d-15c6-d901efcba93f"), true, "Hardware & Home Improvement", 4, "Active", null },
                    { new Guid("2d4b18b6-b106-e524-7061-2d0a06a07292"), "gov-other", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("642a3676-6675-4e65-4ae3-3c535badcdf0"), true, "Other", 4, "Active", null },
                    { new Guid("31d096de-62a6-3ecc-0560-6592c2631284"), "finance-lending", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("0b19ad66-1d4a-bb69-f6f4-9c50743d4d4e"), true, "Lending & Financing", 2, "Active", null },
                    { new Guid("3560bfb0-8e1e-27cb-f91c-7b900fd1a316"), "energy-water", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("7c23066f-4054-3e95-e307-32b343823080"), true, "Water & Waste", 3, "Active", null },
                    { new Guid("36d1ca47-e8c5-2b6e-7897-20a3aaef75a7"), "mfg-garments", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("48396e3d-e810-e743-4bac-51388eb58ca2"), true, "Garments & Textile", 2, "Active", null },
                    { new Guid("3f05e9c0-92fe-d9b9-7136-cb9bba3fa8c0"), "health-clinic", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("8e5403da-fb97-f167-8b2f-55c039cfee27"), true, "Clinic & Diagnostics", 2, "Active", null },
                    { new Guid("410e1950-1bd4-92c7-bc01-7a29f4671d75"), "it-other", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("65d09d07-4fde-c160-f89e-5f5b297d3742"), true, "Other", 4, "Active", null },
                    { new Guid("43d49d3c-e344-d43f-9e6c-9be144f8255a"), "logistics-shipping", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("9c229141-46a6-eed9-43a7-6ef63c0df41d"), true, "Shipping & Maritime", 4, "Active", null },
                    { new Guid("483477f9-809b-5a05-318f-b7002011ab8d"), "construction-engineering", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("e1529023-7092-ee74-a2a8-9363adce9924"), true, "Engineering & Architecture", 2, "Active", null },
                    { new Guid("484145f3-f8b4-7f5e-20d9-4b0ca1369987"), "logistics-other", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("9c229141-46a6-eed9-43a7-6ef63c0df41d"), true, "Other", 5, "Active", null },
                    { new Guid("5180733c-89b2-cfe6-7744-11d7e23ef690"), "edu-training", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("28dfdd3e-a73d-c9a0-f6a4-0e244bd5a89e"), true, "Training & Review Center", 3, "Active", null },
                    { new Guid("5526ec6b-8625-0766-0b00-7688f40d2391"), "health-hospital", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("8e5403da-fb97-f167-8b2f-55c039cfee27"), true, "Hospital", 1, "Active", null },
                    { new Guid("5919222b-78b4-b1c9-0976-550fd836d83b"), "edu-college", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("28dfdd3e-a73d-c9a0-f6a4-0e244bd5a89e"), true, "College & University", 2, "Active", null },
                    { new Guid("596856fb-f7de-f285-6ce1-b8cbedf70749"), "construction-other", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("e1529023-7092-ee74-a2a8-9363adce9924"), true, "Other", 5, "Active", null },
                    { new Guid("5a2e86a2-d7f3-bfff-5d77-03273d96c5bd"), "it-telecom", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("65d09d07-4fde-c160-f89e-5f5b297d3742"), true, "Telecommunications", 3, "Active", null },
                    { new Guid("5e32fb25-ad6e-8cd9-4cf9-627bae3541ae"), "food-travel", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("c175c8cc-8f6e-3b5c-01a7-71e572058fba"), true, "Travel & Tours", 5, "Active", null },
                    { new Guid("5fc71c85-92a1-37e1-0018-bb4208e09bb5"), "security-guard", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("5637fc5d-96b6-1d03-4c64-8ecec60ab67f"), true, "Security Agency", 1, "Active", null },
                    { new Guid("6168cee7-8332-e0c3-3ec0-06fffc328eba"), "edu-school", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("28dfdd3e-a73d-c9a0-f6a4-0e244bd5a89e"), true, "School (Basic Education)", 1, "Active", null },
                    { new Guid("6291a3ad-43aa-29f4-700c-dc78be6f39ba"), "prof-marketing", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("48962548-9e47-6268-3b1b-44d5afadfd5e"), true, "Marketing & Advertising", 3, "Active", null },
                    { new Guid("643a6a71-fb0b-5794-747d-39aa9b153b32"), "mfg-food-beverage", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("48396e3d-e810-e743-4bac-51388eb58ca2"), true, "Food & Beverage", 1, "Active", null },
                    { new Guid("6e55c4f6-9813-e274-4b31-0ee45b5386b1"), "construction-property-mgmt", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("e1529023-7092-ee74-a2a8-9363adce9924"), true, "Property Management", 4, "Active", null },
                    { new Guid("74460cdb-3c8a-5b7d-e5ca-4f2e3245b949"), "mfg-other", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("48396e3d-e810-e743-4bac-51388eb58ca2"), true, "Other", 7, "Active", null },
                    { new Guid("7601cb1c-4cfc-317c-8d93-201cb5606100"), "mfg-automotive", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("48396e3d-e810-e743-4bac-51388eb58ca2"), true, "Automotive & Parts", 4, "Active", null },
                    { new Guid("784f6842-cd5f-738f-9c0c-14c830fc2ea5"), "bpo-non-voice", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("15e76e5d-fbab-e26e-2f53-f105b4a7c1dc"), true, "Non-Voice / Back Office", 2, "Active", null },
                    { new Guid("79b6e199-05fb-054a-3e15-be25a84e3899"), "gov-ngo", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("642a3676-6675-4e65-4ae3-3c535badcdf0"), true, "NGO & Foundation", 2, "Active", null },
                    { new Guid("7b8f3e6c-6a6b-aec3-cec1-7e4a5b0c89e1"), "agri-livestock", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("a4cf298f-c8fb-0a83-db4d-9e4655e20b18"), true, "Livestock & Poultry", 2, "Active", null },
                    { new Guid("810b58e1-975f-f4a5-d752-4aea4e891cd2"), "mfg-furniture", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("48396e3d-e810-e743-4bac-51388eb58ca2"), true, "Furniture & Wood", 5, "Active", null },
                    { new Guid("84918b2a-de30-344d-6385-50ff2df68fbc"), "other-other", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("affc10d8-f373-1035-0654-bc86a966dbbf"), true, "Other", 1, "Active", null },
                    { new Guid("84d2386c-a8d8-5d73-e088-2ea26dcbd68a"), "food-fastfood", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("c175c8cc-8f6e-3b5c-01a7-71e572058fba"), true, "Fast Food & Franchise", 2, "Active", null },
                    { new Guid("8576bee9-5ca3-cc78-ca8b-8f3cb40a90da"), "retail-grocery", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("c4c7711f-5d86-b31d-15c6-d901efcba93f"), true, "Grocery & Supermarket", 1, "Active", null },
                    { new Guid("8c3c3920-808c-b684-3184-d5694d5c0d62"), "energy-other", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("7c23066f-4054-3e95-e307-32b343823080"), true, "Other", 4, "Active", null },
                    { new Guid("8e5ab79e-92a4-f907-8e2b-5edcf483030e"), "mfg-electronics", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("48396e3d-e810-e743-4bac-51388eb58ca2"), true, "Electronics & Semiconductors", 3, "Active", null },
                    { new Guid("8fb088fa-eef7-1738-7d64-54a59c836787"), "health-wellness", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("8e5403da-fb97-f167-8b2f-55c039cfee27"), true, "Wellness, Salon & Spa", 4, "Active", null },
                    { new Guid("8fe6619e-2a0e-cd47-cab9-423fcc457544"), "logistics-passenger", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("9c229141-46a6-eed9-43a7-6ef63c0df41d"), true, "Passenger Transport", 3, "Active", null },
                    { new Guid("8ff954be-8b45-c358-c609-58b2875a0b31"), "bpo-other", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("15e76e5d-fbab-e26e-2f53-f105b4a7c1dc"), true, "Other", 4, "Active", null },
                    { new Guid("905908eb-17ca-e505-ada3-3524046297bc"), "retail-other", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("c4c7711f-5d86-b31d-15c6-d901efcba93f"), true, "Other", 7, "Active", null },
                    { new Guid("94002a1f-8695-eb81-5b9a-6f88faffdce6"), "security-janitorial", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("5637fc5d-96b6-1d03-4c64-8ecec60ab67f"), true, "Janitorial & Housekeeping", 2, "Active", null },
                    { new Guid("993a16c5-74e0-23ee-1375-37d08c72ce5c"), "bpo-voice", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("15e76e5d-fbab-e26e-2f53-f105b4a7c1dc"), true, "Voice / Call Center", 1, "Active", null },
                    { new Guid("9b099686-6592-c2ce-a068-b1097aa62b9a"), "security-other", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("5637fc5d-96b6-1d03-4c64-8ecec60ab67f"), true, "Other", 4, "Active", null },
                    { new Guid("9eb190a5-91f5-755b-22cf-1bfacf9137a0"), "gov-religious", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("642a3676-6675-4e65-4ae3-3c535badcdf0"), true, "Religious Organization", 3, "Active", null },
                    { new Guid("aa4aea3c-8ff1-668f-cca5-ff813ff772c2"), "finance-other", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("0b19ad66-1d4a-bb69-f6f4-9c50743d4d4e"), true, "Other", 5, "Active", null },
                    { new Guid("aab1d381-c018-f76b-0db2-9a3ad11d5ae8"), "retail-wholesale", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("c4c7711f-5d86-b31d-15c6-d901efcba93f"), true, "Wholesale & Distribution", 5, "Active", null },
                    { new Guid("b2c7f3c0-195c-93f9-bcf2-da73449a272c"), "prof-consulting", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("48962548-9e47-6268-3b1b-44d5afadfd5e"), true, "Consulting", 2, "Active", null },
                    { new Guid("b90ea2aa-3c7b-1054-1231-822762d96cb0"), "edu-other", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("28dfdd3e-a73d-c9a0-f6a4-0e244bd5a89e"), true, "Other", 4, "Active", null },
                    { new Guid("bacd03d5-aa22-4f72-ebb2-a428147d6471"), "food-restaurant", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("c175c8cc-8f6e-3b5c-01a7-71e572058fba"), true, "Restaurant & Cafe", 1, "Active", null },
                    { new Guid("d37dc12f-0f80-072a-0265-2feff569659a"), "agri-fishery", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("a4cf298f-c8fb-0a83-db4d-9e4655e20b18"), true, "Fishery & Aquaculture", 3, "Active", null },
                    { new Guid("da29420d-4596-f717-7e87-f8d0d05dafa2"), "it-software", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("65d09d07-4fde-c160-f89e-5f5b297d3742"), true, "Software & Development", 1, "Active", null },
                    { new Guid("e22e96ff-2289-e33a-92fb-5d191c68890b"), "energy-power", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("7c23066f-4054-3e95-e307-32b343823080"), true, "Power & Energy", 1, "Active", null },
                    { new Guid("e519bc95-8734-a300-baaf-72525918e704"), "construction-general", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("e1529023-7092-ee74-a2a8-9363adce9924"), true, "General Contractor", 1, "Active", null },
                    { new Guid("e9441c83-90f4-94e6-a195-8844da9a0af2"), "retail-ecommerce", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("c4c7711f-5d86-b31d-15c6-d901efcba93f"), true, "E-commerce", 6, "Active", null },
                    { new Guid("ea30ccfb-c59d-4ee6-eb74-5bcfb66e3a8f"), "gov-government", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("642a3676-6675-4e65-4ae3-3c535badcdf0"), true, "Government Agency / LGU", 1, "Active", null },
                    { new Guid("eaefe255-0b43-71cc-5277-65c19f8ad3ff"), "health-other", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("8e5403da-fb97-f167-8b2f-55c039cfee27"), true, "Other", 5, "Active", null },
                    { new Guid("f58740ef-2aa5-c176-e0a0-e083b1ffdd7d"), "prof-recruitment", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("48962548-9e47-6268-3b1b-44d5afadfd5e"), true, "Recruitment & Staffing", 4, "Active", null },
                    { new Guid("f7600542-3f77-1187-0c42-c3b033e66d54"), "health-dental", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("8e5403da-fb97-f167-8b2f-55c039cfee27"), true, "Dental", 3, "Active", null },
                    { new Guid("f86d4520-56a7-33cd-8bbc-73718288d2c3"), "prof-legal", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("48962548-9e47-6268-3b1b-44d5afadfd5e"), true, "Legal", 1, "Active", null },
                    { new Guid("f90a44c6-82a3-995c-4874-138fce170663"), "mfg-chemicals-plastics", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("48396e3d-e810-e743-4bac-51388eb58ca2"), true, "Chemicals & Plastics", 6, "Active", null },
                    { new Guid("f99783c5-370b-22be-02d5-63f6b75c654e"), "retail-pharmacy", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("c4c7711f-5d86-b31d-15c6-d901efcba93f"), true, "Pharmacy & Drugstore", 3, "Active", null },
                    { new Guid("fb148887-84f2-7168-389b-7dca7413c303"), "agri-other", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("a4cf298f-c8fb-0a83-db4d-9e4655e20b18"), true, "Other", 4, "Active", null },
                    { new Guid("ffb45d35-49d8-e8bf-6892-beb85ed75b4f"), "retail-fashion", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, new Guid("c4c7711f-5d86-b31d-15c6-d901efcba93f"), true, "Fashion & Apparel", 2, "Active", null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_IndustryCode",
                table: "Tenants",
                column: "IndustryCode");

            migrationBuilder.CreateIndex(
                name: "IX_Industries_Code",
                table: "Industries",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubIndustries_Code",
                table: "SubIndustries",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubIndustries_IndustryId",
                table: "SubIndustries",
                column: "IndustryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubIndustries");

            migrationBuilder.DropTable(
                name: "Industries");

            migrationBuilder.DropIndex(
                name: "IX_Tenants_IndustryCode",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "AcquisitionSource",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "AcquisitionSourceOther",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "BusinessType",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "CompanySize",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "IndustryCode",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SubIndustryCode",
                table: "Tenants");
        }
    }
}
