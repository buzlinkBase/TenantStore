using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantStoreApi.Domain.Entities;

namespace TenantStoreApi.Infrastructure.EntityConfigs;

/// <summary>
/// The starting industry list (Philippines-oriented). Like the plan seed, these are only initial
/// values: a change here reaches the database through a migration. Every industry ends with an
/// "Other" sub-industry so a company never has to pick a wrong one.
/// </summary>
public static class IndustrySeed
{
    private sealed record Def(string Code, string Name, (string Code, string Name)[] Subs);

    private static readonly Def[] Definitions =
    [
        new("bpo", "BPO & Customer Support",
        [
            ("bpo-voice", "Voice / Call Center"),
            ("bpo-non-voice", "Non-Voice / Back Office"),
            ("bpo-kpo", "Knowledge Process Outsourcing"),
            ("bpo-other", "Other"),
        ]),
        new("retail", "Retail & Wholesale",
        [
            ("retail-grocery", "Grocery & Supermarket"),
            ("retail-fashion", "Fashion & Apparel"),
            ("retail-pharmacy", "Pharmacy & Drugstore"),
            ("retail-hardware", "Hardware & Home Improvement"),
            ("retail-wholesale", "Wholesale & Distribution"),
            ("retail-ecommerce", "E-commerce"),
            ("retail-other", "Other"),
        ]),
        new("food-hospitality", "Food, Hospitality & Tourism",
        [
            ("food-restaurant", "Restaurant & Cafe"),
            ("food-fastfood", "Fast Food & Franchise"),
            ("food-hotel", "Hotel & Resort"),
            ("food-catering", "Catering"),
            ("food-travel", "Travel & Tours"),
            ("food-other", "Other"),
        ]),
        new("manufacturing", "Manufacturing",
        [
            ("mfg-food-beverage", "Food & Beverage"),
            ("mfg-garments", "Garments & Textile"),
            ("mfg-electronics", "Electronics & Semiconductors"),
            ("mfg-automotive", "Automotive & Parts"),
            ("mfg-furniture", "Furniture & Wood"),
            ("mfg-chemicals-plastics", "Chemicals & Plastics"),
            ("mfg-other", "Other"),
        ]),
        new("construction-real-estate", "Construction & Real Estate",
        [
            ("construction-general", "General Contractor"),
            ("construction-engineering", "Engineering & Architecture"),
            ("construction-property-dev", "Property Development"),
            ("construction-property-mgmt", "Property Management"),
            ("construction-other", "Other"),
        ]),
        new("healthcare", "Healthcare & Wellness",
        [
            ("health-hospital", "Hospital"),
            ("health-clinic", "Clinic & Diagnostics"),
            ("health-dental", "Dental"),
            ("health-wellness", "Wellness, Salon & Spa"),
            ("health-other", "Other"),
        ]),
        new("education", "Education & Training",
        [
            ("edu-school", "School (Basic Education)"),
            ("edu-college", "College & University"),
            ("edu-training", "Training & Review Center"),
            ("edu-other", "Other"),
        ]),
        new("logistics", "Transportation & Logistics",
        [
            ("logistics-freight", "Freight & Courier"),
            ("logistics-warehousing", "Warehousing"),
            ("logistics-passenger", "Passenger Transport"),
            ("logistics-shipping", "Shipping & Maritime"),
            ("logistics-other", "Other"),
        ]),
        new("security-facilities", "Security & Facility Services",
        [
            ("security-guard", "Security Agency"),
            ("security-janitorial", "Janitorial & Housekeeping"),
            ("security-manpower", "Manpower Agency"),
            ("security-other", "Other"),
        ]),
        new("it-services", "Information Technology",
        [
            ("it-software", "Software & Development"),
            ("it-infrastructure", "IT Infrastructure & Support"),
            ("it-telecom", "Telecommunications"),
            ("it-other", "Other"),
        ]),
        new("finance", "Banking, Finance & Insurance",
        [
            ("finance-bank", "Banking"),
            ("finance-lending", "Lending & Financing"),
            ("finance-insurance", "Insurance"),
            ("finance-accounting", "Accounting & Auditing"),
            ("finance-other", "Other"),
        ]),
        new("professional-services", "Professional Services",
        [
            ("prof-legal", "Legal"),
            ("prof-consulting", "Consulting"),
            ("prof-marketing", "Marketing & Advertising"),
            ("prof-recruitment", "Recruitment & Staffing"),
            ("prof-other", "Other"),
        ]),
        new("agriculture", "Agriculture, Fishery & Forestry",
        [
            ("agri-farming", "Farming & Crops"),
            ("agri-livestock", "Livestock & Poultry"),
            ("agri-fishery", "Fishery & Aquaculture"),
            ("agri-other", "Other"),
        ]),
        new("energy-utilities", "Energy, Mining & Utilities",
        [
            ("energy-power", "Power & Energy"),
            ("energy-mining", "Mining & Quarrying"),
            ("energy-water", "Water & Waste"),
            ("energy-other", "Other"),
        ]),
        new("government-nonprofit", "Government & Non-profit",
        [
            ("gov-government", "Government Agency / LGU"),
            ("gov-ngo", "NGO & Foundation"),
            ("gov-religious", "Religious Organization"),
            ("gov-other", "Other"),
        ]),
        new("other", "Other", [("other-other", "Other")]),
    ];

    public static readonly Industry[] Industries = Definitions
        .Select((d, index) => new Industry
        {
            Id = StableId($"industry:{d.Code}"),
            Code = d.Code,
            Name = d.Name,
            SortOrder = index + 1,
            IsActive = true,
        })
        .ToArray();

    public static readonly SubIndustry[] SubIndustries = Definitions
        .SelectMany(d => d.Subs.Select((s, index) => new SubIndustry
        {
            Id = StableId($"sub-industry:{s.Code}"),
            IndustryId = StableId($"industry:{d.Code}"),
            Code = s.Code,
            Name = s.Name,
            SortOrder = index + 1,
            IsActive = true,
        }))
        .ToArray();

    // Seed rows need the same Id on every build, or each migration would delete and re-insert them.
    private static Guid StableId(string seed) => new(MD5.HashData(Encoding.UTF8.GetBytes(seed)));
}

public class IndustryConfig : IEntityTypeConfiguration<Industry>
{
    public void Configure(EntityTypeBuilder<Industry> builder)
    {
        builder.Property(x => x.Code).HasMaxLength(64);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.Property(x => x.Name).HasMaxLength(128);
        builder.HasData(IndustrySeed.Industries);
    }
}

public class SubIndustryConfig : IEntityTypeConfiguration<SubIndustry>
{
    public void Configure(EntityTypeBuilder<SubIndustry> builder)
    {
        builder.Property(x => x.Code).HasMaxLength(64);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.Property(x => x.Name).HasMaxLength(128);
        builder.HasOne(x => x.Industry)
            .WithMany(x => x.SubIndustries)
            .HasForeignKey(x => x.IndustryId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasData(IndustrySeed.SubIndustries);
    }
}
