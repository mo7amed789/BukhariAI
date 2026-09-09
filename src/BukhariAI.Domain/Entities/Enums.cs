namespace BukhariAI.Domain.Entities;

public enum ReadingSessionStatus
{
    NotStarted = 0,
    InProgress = 1,
    Completed = 2
}

public enum LessonProgressStatus
{
    NotStarted = 0,
    InProgress = 1,
    Completed = 2,
    NeedsReview = 3
}

/// <summary>
/// The evidence-based outcome of a submitted assessment answer.  This is kept
/// separate from <see cref="LearningLevel"/>: an answer can be correct without
/// establishing long-term mastery.
/// </summary>
public enum AssessmentAnswerStatus
{
    Correct = 0,
    PartiallyCorrect = 1,
    Incorrect = 2,
    InsufficientEvidence = 3
}

public enum LearningIntent
{
    NewLesson = 0,
    ContinueLearning = 1,
    ReinforceWeakConcepts = 2,
    ReviewDueConcepts = 3,
    VerifyMastery = 4
}

public enum AdaptiveLearningAction
{
    Introduce = 0,
    Explain = 1,
    Reinforce = 2,
    AskQuestion = 3,
    Review = 4,
    VerifyMastery = 5,
    NoAction = 6
}

public enum LearningLevel
{
    Unknown = 0,
    Introduced = 1,
    Familiar = 2,
    Understood = 3,
    Mastered = 4
}

public enum EvaluationLifecycleStatus
{
    Unknown = 0,
    Pending = 1,
    Completed = 2,
    Failed = 3
}

