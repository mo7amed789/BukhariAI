using BukhariAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BukhariAI.Infrastructure.Persistence.Configurations;

public sealed class LessonConfiguration : IEntityTypeConfiguration<Lesson>
{
    public void Configure(EntityTypeBuilder<Lesson> builder)
    {
        builder.ToTable("Lessons");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Title)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(l => l.Overview)
            .IsRequired();

        builder.Property(l => l.HistoricalContext)
            .IsRequired();

        builder.Property(l => l.StartPage)
            .IsRequired();

        builder.Property(l => l.EndPage)
            .IsRequired();

        builder.Property(l => l.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(l => l.CreatedAtUtc);
        builder.HasIndex(l => new { l.BookId, l.StartPage, l.EndPage });

        // Relationships
        builder.HasMany(l => l.LessonPages)
            .WithOne(lp => lp.Lesson)
            .HasForeignKey(lp => lp.LessonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(l => l.Hadiths)
            .WithOne(h => h.Lesson)
            .HasForeignKey(h => h.LessonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(l => l.Connections)
            .WithOne(c => c.Lesson)
            .HasForeignKey(c => c.LessonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(l => l.ReviewQuestions)
            .WithOne(q => q.Lesson)
            .HasForeignKey(q => q.LessonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(l => l.LessonPeople)
            .WithOne(lp => lp.Lesson)
            .HasForeignKey(lp => lp.LessonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(l => l.LessonProgresses)
            .WithOne(lp => lp.Lesson)
            .HasForeignKey(lp => lp.LessonId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
