namespace BukhariAI.Domain.Entities;

public sealed class KnownTerm
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LearningContextId { get; set; }

    public LessonLearningContext LearningContext { get; set; } = null!;

    public string Term { get; set; } = string.Empty;

    public string Explanation { get; set; } = string.Empty;

    public Guid? FirstIntroducedLessonId { get; set; }

    public Lesson? FirstIntroducedLesson { get; set; }

    public Guid? LastReferencedLessonId { get; set; }

    public Lesson? LastReferencedLesson { get; set; }

    public int TimesSeen { get; set; } = 1;

    public LearningLevel LearningLevel { get; set; } = LearningLevel.Introduced;
}
