namespace BukhariAI.Domain.Entities;

public sealed class KnownTopic
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LearningContextId { get; set; }

    public LessonLearningContext LearningContext { get; set; } = null!;

    public string Topic { get; set; } = string.Empty;

    public Guid? FirstIntroducedLessonId { get; set; }

    public Lesson? FirstIntroducedLesson { get; set; }

    public Guid? LastReferencedLessonId { get; set; }

    public Lesson? LastReferencedLesson { get; set; }
}
