using BukhariAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BukhariAI.Infrastructure.Persistence.Configurations;

public sealed class HadithEvidenceConfiguration : IEntityTypeConfiguration<HadithEvidence>
{
    public void Configure(EntityTypeBuilder<HadithEvidence> builder)
    {
        builder.ToTable("HadithEvidences");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Text)
            .IsRequired();

        builder.Property(e => e.Role)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.SourcePagesCsv)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(e => e.HadithId);
    }
}
