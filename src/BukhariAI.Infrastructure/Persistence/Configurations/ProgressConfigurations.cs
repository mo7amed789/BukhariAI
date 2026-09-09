using BukhariAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BukhariAI.Infrastructure.Persistence.Configurations;

public sealed class ReadingSessionConfiguration : IEntityTypeConfiguration<ReadingSession>
{
    public void Configure(EntityTypeBuilder<ReadingSession> builder)
    {
        builder.ToTable("ReadingSessions");

        builder.HasKey(rs => rs.Id);

        builder.Property(rs => rs.StartPage)
            .IsRequired();

        builder.Property(rs => rs.EndPage)
            .IsRequired();

        builder.Property(rs => rs.StartedAtUtc)
            .IsRequired();

        builder.Property(rs => rs.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.HasIndex(rs => rs.BookId);
        builder.HasIndex(rs => rs.StartedAtUtc);

        builder.HasOne(rs => rs.Lesson)
            .WithMany(l => l.ReadingSessions)
            .HasForeignKey(rs => rs.LessonId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class LessonProgressConfiguration : IEntityTypeConfiguration<LessonProgress>
{
    public void Configure(EntityTypeBuilder<LessonProgress> builder)
    {
        builder.ToTable("LessonProgresses");

        builder.HasKey(lp => lp.Id);

        builder.Property(lp => lp.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(lp => lp.UserId)
            .IsRequired()
            .HasDefaultValue(User.DefaultUserId);

        builder.HasOne(lp => lp.User)
            .WithMany(u => u.LessonProgresses)
            .HasForeignKey(lp => lp.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(lp => new { lp.UserId, lp.LessonId })
            .IsUnique();

        builder.HasIndex(lp => lp.LessonId);

        builder.HasOne(lp => lp.LastAssessment)
            .WithMany()
            .HasForeignKey(lp => lp.LastAssessmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ReviewRecommendationConfiguration : IEntityTypeConfiguration<ReviewRecommendation>
{
    public void Configure(EntityTypeBuilder<ReviewRecommendation> builder)
    {
        builder.ToTable("ReviewRecommendations");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ConceptKey).IsRequired().HasMaxLength(300);
        builder.Property(r => r.Reason).IsRequired().HasMaxLength(1000);
        builder.Property(r => r.ReasonCode).IsRequired().HasMaxLength(100);
        builder.Property(r => r.CurrentLearningLevel).HasConversion<int>();
        builder.Property(r => r.RecommendedAction).HasConversion<int>();
        builder.Property(r => r.SuggestedQuestionType).IsRequired().HasMaxLength(100);
        builder.Property(r => r.SourcePagesCsv).IsRequired().HasMaxLength(200);
        builder.Property(r => r.Priority).IsRequired();
        builder.Property(r => r.GeneratedAtUtc).IsRequired();
        builder.HasOne(r => r.Book).WithMany(b => b.ReviewRecommendations)
            .HasForeignKey(r => r.BookId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(r => r.Lesson).WithMany(l => l.ReviewRecommendations)
            .HasForeignKey(r => r.LessonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => new { r.BookId, r.ConceptKey, r.ReviewedAtUtc });
    }
}
