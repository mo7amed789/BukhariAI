using BukhariAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BukhariAI.Infrastructure.Persistence.Configurations;

public sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("People");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(p => p.Description)
            .IsRequired();

        builder.Property(p => p.DetailedBiographyJson)
            .IsRequired(false);

        builder.Property(p => p.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(p => p.Name)
            .IsUnique();
    }
}

public sealed class LessonPersonConfiguration : IEntityTypeConfiguration<LessonPerson>
{
    public void Configure(EntityTypeBuilder<LessonPerson> builder)
    {
        builder.ToTable("LessonPeople");

        builder.HasKey(lp => lp.Id);

        builder.Property(lp => lp.ContextDescription)
            .IsRequired()
            .HasMaxLength(2000);

        builder.HasOne(lp => lp.Lesson)
            .WithMany(l => l.LessonPeople)
            .HasForeignKey(lp => lp.LessonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(lp => lp.Person)
            .WithMany(p => p.LessonPeople)
            .HasForeignKey(lp => lp.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(lp => new { lp.LessonId, lp.PersonId })
            .IsUnique();

        builder.HasIndex(lp => lp.PersonId);
    }
}

public sealed class HadithPersonConfiguration : IEntityTypeConfiguration<HadithPerson>
{
    public void Configure(EntityTypeBuilder<HadithPerson> builder)
    {
        builder.ToTable("HadithPeople");

        builder.HasKey(hp => hp.Id);

        builder.HasOne(hp => hp.Hadith)
            .WithMany(h => h.HadithPeople)
            .HasForeignKey(hp => hp.HadithId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(hp => hp.Person)
            .WithMany(p => p.HadithPeople)
            .HasForeignKey(hp => hp.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(hp => new { hp.HadithId, hp.PersonId })
            .IsUnique();

        builder.HasIndex(hp => hp.PersonId);
    }
}
