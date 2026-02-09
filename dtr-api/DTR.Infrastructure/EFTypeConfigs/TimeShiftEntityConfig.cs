using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DTR.Infrastructure;

public class TimeShiftEntityConfig : IEntityTypeConfiguration<TimeShift>
{
    public void Configure(EntityTypeBuilder<TimeShift> builder)
    {
        //builder.Property(x => x.PunchMode)
        // .HasConversion(
        //     v => (int)v,
        //     v => (PunchMode)v
        // );

        builder.Property(x => x.WithLunchBreak)
         .HasConversion(
             v => (int)v,
             v => (BreakMode)v
         );
        builder.Property(x => x.WithAMBreak)
         .HasConversion(
             v => (int)v,
             v => (BreakMode)v
         );
        builder.Property(x => x.WithPMBreak)
         .HasConversion(
             v => (int)v,
             v => (BreakMode)v
         );
    }
}
