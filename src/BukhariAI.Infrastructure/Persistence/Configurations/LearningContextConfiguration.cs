using BukhariAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BukhariAI.Infrastructure.Persistence.Configurations;

public sealed class LessonLearningContextConfiguration : IEntityTypeConfiguration<LessonLearningContext>
{
    public void Configure(EntityTypeBuilder<LessonLearningContext> builder)
    {
        builder.ToTable("LessonLearningContexts");

        builder.HasKey(lc => lc.Id);

        builder.Property(lc => lc.CreatedAtUtc)
            .IsRequired();

        builder.Property(lc => lc.UpdatedAtUtc)
            .IsRequired();

        builder.HasIndex(lc => lc.BookId);

        builder.HasMany(lc => lc.KnownPeople)
            .WithOne(kp => kp.LearningContext)
            .HasForeignKey(kp => kp.LearningContextId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(lc => lc.KnownTerms)
            .WithOne(kt => kt.LearningContext)
            .HasForeignKey(kt => kt.LearningContextId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(lc => lc.KnownTopics)
            .WithOne(kt => kt.LearningContext)
            .HasForeignKey(kt => kt.LearningContextId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class KnownPersonConfiguration : IEntityTypeConfiguration<KnownPerson>
{
    public void Configure(EntityTypeBuilder<KnownPerson> builder)
    {
        builder.ToTable("KnownPeople");

        builder.HasKey(kp => kp.Id);

        builder.Property(kp => kp.ShortBiography)
            .IsRequired()
            .HasMaxLength(2000)
            .HasDefaultValue(string.Empty);

        builder.Property(kp => kp.TimesSeen)
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(kp => kp.LearningLevel)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(LearningLevel.Introduced)
            .HasSentinel(LearningLevel.Unknown);

        builder.HasOne(kp => kp.Person)
            .WithMany(p => p.KnownPersons)
            .HasForeignKey(kp => kp.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(kp => kp.FirstIntroducedLesson)
            .WithMany()
            .HasForeignKey(kp => kp.FirstIntroducedLessonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(kp => kp.LastReferencedLesson)
            .WithMany()
            .HasForeignKey(kp => kp.LastReferencedLessonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(kp => new { kp.LearningContextId, kp.PersonId })
            .IsUnique();
    }
}

public sealed class KnownTopicConfiguration : IEntityTypeConfiguration<KnownTopic>
{
    public void Configure(EntityTypeBuilder<KnownTopic> builder)
    {
        builder.ToTable("KnownTopics");

        builder.HasKey(kt => kt.Id);

        builder.Property(kt => kt.Topic)
            .IsRequired()
            .HasMaxLength(500);

        builder.HasOne(kt => kt.FirstIntroducedLesson)
            .WithMany()
            .HasForeignKey(kt => kt.FirstIntroducedLessonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(kt => kt.LastReferencedLesson)
            .WithMany()
            .HasForeignKey(kt => kt.LastReferencedLessonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(kt => new { kt.LearningContextId, kt.Topic });
    }
}

public sealed class KnownTermConfiguration : IEntityTypeConfiguration<KnownTerm>
{
    public void Configure(EntityTypeBuilder<KnownTerm> builder)
    {
        builder.ToTable("KnownTerms");

        builder.HasKey(kt => kt.Id);

        builder.Property(kt => kt.Term)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(kt => kt.Explanation)
            .IsRequired();

        builder.Property(kt => kt.TimesSeen)
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(kt => kt.LearningLevel)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(LearningLevel.Introduced)
            .HasSentinel(LearningLevel.Unknown);

        builder.HasOne(kt => kt.FirstIntroducedLesson)
            .WithMany()
            .HasForeignKey(kt => kt.FirstIntroducedLessonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(kt => kt.LastReferencedLesson)
            .WithMany()
            .HasForeignKey(kt => kt.LastReferencedLessonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(kt => new { kt.LearningContextId, kt.Term });
    }
}
