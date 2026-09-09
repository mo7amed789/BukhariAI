namespace BukhariAI.Domain.Entities;

public enum UserRole
{
    Student = 1,
    Teacher = 2,
    Administrator = 3
}

/// <summary>
/// Represents an authenticated user in BukhariAI, enabling multi-tenancy and data ownership.
/// </summary>
public sealed class User
{
    /// <summary>
    /// Well-known default student ID used for initial migrations and unauthenticated fallback.
    /// </summary>
    public static readonly Guid DefaultUserId = new("11111111-1111-1111-1111-111111111111");

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Username { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.Student;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public string? PasswordResetToken { get; set; }

    public DateTime? PasswordResetExpiresUtc { get; set; }

    // Navigation properties
    public ICollection<Book> Books { get; set; } = [];

    public ICollection<ChatSession> ChatSessions { get; set; } = [];

    public ICollection<LessonProgress> LessonProgresses { get; set; } = [];

    public ICollection<StudentConceptMastery> ConceptMasteries { get; set; } = [];

    public ICollection<StudentAnswer> Answers { get; set; } = [];
}
