using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DTR.Infrastructure;

internal class AttendanceEFConfig : IEntityTypeConfiguration<Attendance>
{
    public void Configure(EntityTypeBuilder<Attendance> builder)
    {
    }
}
