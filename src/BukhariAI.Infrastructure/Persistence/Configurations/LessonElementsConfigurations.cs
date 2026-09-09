using BukhariAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BukhariAI.Infrastructure.Persistence.Configurations;

public sealed class LessonConnectionConfiguration : IEntityTypeConfiguration<LessonConnection>
{
    public void Configure(EntityTypeBuilder<LessonConnection> builder)
    {
        builder.ToTable("LessonConnections");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Description)
            .IsRequired()
            .HasMaxLength(2000);

        builder.HasIndex(c => c.LessonId);
    }
}

public sealed class ReviewQuestionConfiguration : IEntityTypeConfiguration<ReviewQuestion>
{
    public void Configure(EntityTypeBuilder<ReviewQuestion> builder)
    {
        builder.ToTable("ReviewQuestions");

        builder.HasKey(q => q.Id);

        builder.Property(q => q.Question)
            .IsRequired()
            .HasMaxLength(2000);

        builder.HasIndex(q => q.LessonId);
    }
}
