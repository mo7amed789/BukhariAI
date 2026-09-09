using BukhariAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BukhariAI.Infrastructure.Persistence.Configurations;

public sealed class LessonPageConfiguration : IEntityTypeConfiguration<LessonPage>
{
    public void Configure(EntityTypeBuilder<LessonPage> builder)
    {
        builder.ToTable("LessonPages");

        builder.HasKey(lp => lp.Id);

        builder.Property(lp => lp.PageNumber)
            .IsRequired();

        builder.HasOne(lp => lp.Lesson)
            .WithMany(l => l.LessonPages)
            .HasForeignKey(lp => lp.LessonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(lp => lp.BookPage)
            .WithMany(bp => bp.LessonPages)
            .HasForeignKey(lp => lp.BookPageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(lp => new { lp.LessonId, lp.BookPageId })
            .IsUnique();

        builder.HasIndex(lp => lp.BookPageId);
    }
}
