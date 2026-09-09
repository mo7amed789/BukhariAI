using BukhariAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BukhariAI.Infrastructure.Persistence.Configurations;

public sealed class BookPageConfiguration : IEntityTypeConfiguration<BookPage>
{
    public void Configure(EntityTypeBuilder<BookPage> builder)
    {
        builder.ToTable("BookPages");

        builder.HasKey(bp => bp.Id);

        builder.Property(bp => bp.PageNumber)
            .IsRequired();

        builder.Property(bp => bp.ExtractedText)
            .IsRequired();

        builder.Property(bp => bp.UsedOcr)
            .IsRequired();

        builder.Property(bp => bp.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(bp => new { bp.BookId, bp.PageNumber })
            .IsUnique();
    }
}
