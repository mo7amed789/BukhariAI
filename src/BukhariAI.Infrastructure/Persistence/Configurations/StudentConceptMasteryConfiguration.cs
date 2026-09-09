using BukhariAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BukhariAI.Infrastructure.Persistence.Configurations;

public sealed class StudentConceptMasteryConfiguration : IEntityTypeConfiguration<StudentConceptMastery>
{
    public void Configure(EntityTypeBuilder<StudentConceptMastery> builder)
    {
        builder.ToTable("StudentConceptMasteries");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.ConceptKey)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(m => m.LearningLevel)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(LearningLevel.Introduced)
            .HasSentinel(LearningLevel.Unknown);

        builder.Property(m => m.ExposureCount)
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(m => m.AssessmentCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(m => m.CorrectAnswerCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(m => m.DemonstratedContextCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(m => m.MasteryScore)
            .IsRequired()
            .HasPrecision(5, 4)
            .HasDefaultValue(0.0);

        builder.Property(m => m.CreatedAtUtc)
            .IsRequired();

        builder.Property(m => m.UpdatedAtUtc)
            .IsRequired();

        builder.HasOne(m => m.Book)
            .WithMany(b => b.ConceptMasteries)
            .HasForeignKey(m => m.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.FirstIntroducedLesson)
            .WithMany(l => l.IntroducedMasteries)
            .HasForeignKey(m => m.FirstIntroducedLessonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.LastAssessedLesson)
            .WithMany(l => l.AssessedMasteries)
            .HasForeignKey(m => m.LastAssessedLessonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(m => m.UserId)
            .IsRequired()
            .HasDefaultValue(User.DefaultUserId);

        builder.HasOne(m => m.User)
            .WithMany(u => u.ConceptMasteries)
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.UserId, m.BookId, m.ConceptKey })
            .IsUnique();

        builder.HasIndex(m => new { m.BookId, m.ConceptKey });
        builder.HasIndex(m => m.UserId);
        builder.HasIndex(m => m.LearningLevel);
        builder.HasIndex(m => m.MasteryScore);
    }
}
