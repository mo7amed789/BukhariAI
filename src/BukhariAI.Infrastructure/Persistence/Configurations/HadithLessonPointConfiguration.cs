using BukhariAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BukhariAI.Infrastructure.Persistence.Configurations;

public sealed class HadithLessonPointConfiguration : IEntityTypeConfiguration<HadithLessonPoint>
{
    public void Configure(EntityTypeBuilder<HadithLessonPoint> builder)
    {
        builder.ToTable("HadithLessonPoints");

        builder.HasKey(lp => lp.Id);

        builder.Property(lp => lp.Point)
            .IsRequired()
            .HasMaxLength(2000);

        builder.HasIndex(lp => lp.HadithId);
    }
}
