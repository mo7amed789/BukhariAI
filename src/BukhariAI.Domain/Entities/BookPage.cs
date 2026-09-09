namespace BukhariAI.Domain.Entities;

public sealed class BookPage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BookId { get; set; }

    public Book Book { get; set; } = null!;

    public int PageNumber { get; set; }

    public string ExtractedText { get; set; } = string.Empty;

    public bool UsedOcr { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ICollection<LessonPage> LessonPages { get; set; } = [];
}
