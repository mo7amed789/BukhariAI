using BukhariAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BukhariAI.Infrastructure.Persistence.Configurations;

public sealed class BookConfiguration : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> builder)
    {
        builder.ToTable("Books");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Title)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(b => b.Author)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(b => b.Description)
            .IsRequired();

        builder.Property(b => b.CreatedAtUtc)
            .IsRequired();

        builder.Property(b => b.UserId)
            .IsRequired()
            .HasDefaultValue(User.DefaultUserId);

        builder.HasIndex(b => b.Title);
        builder.HasIndex(b => new { b.UserId, b.Title });

        builder.HasMany(b => b.BookPages)
            .WithOne(bp => bp.Book)
            .HasForeignKey(bp => bp.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(b => b.Lessons)
            .WithOne(l => l.Book)
            .HasForeignKey(l => l.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(b => b.ReadingSessions)
            .WithOne(rs => rs.Book)
            .HasForeignKey(rs => rs.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(b => b.LearningContexts)
            .WithOne(lc => lc.Book)
            .HasForeignKey(lc => lc.BookId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
