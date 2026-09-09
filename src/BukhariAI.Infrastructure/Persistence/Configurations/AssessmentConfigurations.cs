using BukhariAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BukhariAI.Infrastructure.Persistence.Configurations;

public sealed class AssessmentConfiguration : IEntityTypeConfiguration<Assessment>
{
    public void Configure(EntityTypeBuilder<Assessment> builder)
    {
        builder.ToTable("Assessments");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Title)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(a => a.CreatedAtUtc)
            .IsRequired();

        builder.HasOne(a => a.Book)
            .WithMany(b => b.Assessments)
            .HasForeignKey(a => a.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Lesson)
            .WithMany(l => l.Assessments)
            .HasForeignKey(a => a.LessonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(a => a.Questions)
            .WithOne(q => q.Assessment)
            .HasForeignKey(q => q.AssessmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => a.BookId);
        builder.HasIndex(a => a.LessonId);
    }
}

public sealed class AssessmentQuestionConfiguration : IEntityTypeConfiguration<AssessmentQuestion>
{
    public void Configure(EntityTypeBuilder<AssessmentQuestion> builder)
    {
        builder.ToTable("AssessmentQuestions");

        builder.HasKey(q => q.Id);

        builder.Property(q => q.Question)
            .IsRequired();

        builder.Property(q => q.QuestionType)
            .IsRequired()
            .HasMaxLength(100)
            .HasDefaultValue("ConceptualExplanation");

        builder.Property(q => q.Difficulty)
            .IsRequired()
            .HasMaxLength(50)
            .HasDefaultValue("Intermediate");

        builder.Property(q => q.ExpectedConceptsCsv)
            .IsRequired()
            .HasMaxLength(2000)
            .HasDefaultValue(string.Empty);

        builder.Property(q => q.SourcePagesCsv)
            .IsRequired()
            .HasMaxLength(200)
            .HasDefaultValue(string.Empty);

        builder.Property(q => q.EvaluationGuidance)
            .IsRequired()
            .HasMaxLength(4000)
            .HasDefaultValue(string.Empty);

        builder.Property(q => q.CreatedAtUtc)
            .IsRequired();

        builder.HasOne(q => q.SourceLesson)
            .WithMany(l => l.AssessmentQuestions)
            .HasForeignKey(q => q.SourceLessonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(q => q.StudentAnswers)
            .WithOne(sa => sa.AssessmentQuestion)
            .HasForeignKey(sa => sa.AssessmentQuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(q => q.AssessmentId);
        builder.HasIndex(q => q.SourceLessonId);
    }
}

public sealed class StudentAnswerConfiguration : IEntityTypeConfiguration<StudentAnswer>
{
    public void Configure(EntityTypeBuilder<StudentAnswer> builder)
    {
        builder.ToTable("StudentAnswers");

        builder.HasKey(sa => sa.Id);

        builder.Property(sa => sa.AnswerText)
            .IsRequired();

        builder.Property(sa => sa.SubmissionId)
            .HasMaxLength(100);

        builder.Property(sa => sa.SubmittedAtUtc)
            .IsRequired();

        builder.Property(sa => sa.UserId)
            .IsRequired()
            .HasDefaultValue(User.DefaultUserId);

        builder.HasOne(sa => sa.User)
            .WithMany(u => u.Answers)
            .HasForeignKey(sa => sa.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(sa => sa.LifecycleStatus)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(EvaluationLifecycleStatus.Completed)
            .HasSentinel(EvaluationLifecycleStatus.Unknown);

        builder.Property(sa => sa.ErrorMessage)
            .HasMaxLength(2000);

        builder.Property(sa => sa.RowVersion)
            .IsRowVersion();

        builder.HasOne(sa => sa.AssessmentResult)
            .WithOne(ar => ar.StudentAnswer)
            .HasForeignKey<AssessmentResult>(ar => ar.StudentAnswerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(sa => sa.UserId);
        builder.HasIndex(sa => sa.AssessmentQuestionId);
        builder.HasIndex(sa => new { sa.UserId, sa.AssessmentQuestionId, sa.SubmissionId })
            .IsUnique()
            .HasFilter("[SubmissionId] IS NOT NULL");
    }
}

public sealed class AssessmentResultConfiguration : IEntityTypeConfiguration<AssessmentResult>
{
    public void Configure(EntityTypeBuilder<AssessmentResult> builder)
    {
        builder.ToTable("AssessmentResults");

        builder.HasKey(ar => ar.Id);

        builder.Property(ar => ar.Score)
            .IsRequired()
            .HasPrecision(5, 4);

        builder.Property(ar => ar.AnswerStatus)
            .IsRequired()
            .HasConversion<int>()
            .HasSentinel(AssessmentAnswerStatus.InsufficientEvidence)
            .HasDefaultValue(AssessmentAnswerStatus.InsufficientEvidence);

        builder.Property(ar => ar.Level)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(LearningLevel.Unknown);

        builder.Property(ar => ar.UnderstoodConceptsJson)
            .IsRequired()
            .HasMaxLength(4000)
            .HasDefaultValue("[]");

        builder.Property(ar => ar.MissingConceptsJson)
            .IsRequired()
            .HasMaxLength(4000)
            .HasDefaultValue("[]");

        builder.Property(ar => ar.MisconceptionsJson)
            .IsRequired()
            .HasMaxLength(4000)
            .HasDefaultValue("[]");

        builder.Property(ar => ar.Feedback)
            .IsRequired()
            .HasMaxLength(4000)
            .HasDefaultValue(string.Empty);

        builder.Property(ar => ar.EvaluatedAtUtc)
            .IsRequired();

        builder.HasIndex(ar => ar.StudentAnswerId)
            .IsUnique();
    }
}
