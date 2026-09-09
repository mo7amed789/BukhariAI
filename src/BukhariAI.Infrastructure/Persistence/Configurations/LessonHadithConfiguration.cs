using BukhariAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BukhariAI.Infrastructure.Persistence.Configurations;

public sealed class LessonHadithConfiguration : IEntityTypeConfiguration<LessonHadith>
{
    public void Configure(EntityTypeBuilder<LessonHadith> builder)
    {
        builder.ToTable("LessonHadiths");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.Reference)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(h => h.Summary)
            .IsRequired();

        builder.Property(h => h.Problem)
            .IsRequired();

        builder.Property(h => h.Reasoning)
            .IsRequired();

        builder.Property(h => h.ScholarlyDiscussion)
            .IsRequired();

        builder.Property(h => h.Conclusion)
            .IsRequired();

        builder.Property(h => h.EasyExplanation)
            .IsRequired();

        builder.Property(h => h.HistoricalContext)
            .IsRequired();

        builder.HasIndex(h => h.LessonId);

        // Child collections
        builder.HasMany(h => h.Evidences)
            .WithOne(e => e.Hadith)
            .HasForeignKey(e => e.HadithId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(h => h.LessonPoints)
            .WithOne(lp => lp.Hadith)
            .HasForeignKey(lp => lp.HadithId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(h => h.HadithPeople)
            .WithOne(hp => hp.Hadith)
            .HasForeignKey(hp => hp.HadithId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
