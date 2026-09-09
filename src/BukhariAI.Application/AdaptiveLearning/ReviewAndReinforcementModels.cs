using BukhariAI.Domain.Entities;

namespace BukhariAI.Application.AdaptiveLearning;

/// <summary>
/// Structured AI Reinforcement Session for a weak or due concept.
/// </summary>
public sealed class ConceptReinforcementSessionDto
{
    public Guid BookId { get; set; }
    public string ConceptKey { get; set; } = string.Empty;
    public LearningLevel CurrentLevel { get; set; }
    public double Confidence { get; set; }
    public string ConceptTitle { get; set; } = string.Empty;
    public string SimplifiedExplanation { get; set; } = string.Empty;
    public string? KeyEvidenceText { get; set; }
    public string? KeyEvidenceRole { get; set; }
    public string CoreMisconceptionClarification { get; set; } = string.Empty;
    public string MnemonicOrMemoryAnchor { get; set; } = string.Empty;
    public string VerificationQuestion { get; set; } = string.Empty;
    public string QuestionGuidance { get; set; } = string.Empty;
    public string QuestionType { get; set; } = "Understanding";
    public Guid? SourceLessonId { get; set; }
    public string? LessonTitle { get; set; }
    public List<int> SourcePages { get; set; } = [];
}

/// <summary>
/// Request to generate AI reinforcement for a concept.
/// </summary>
public sealed class GenerateReinforcementRequest
{
    public string ConceptKey { get; set; } = string.Empty;
    public Guid? LessonId { get; set; }
}

/// <summary>
/// Request to submit an answer to the reinforcement question.
/// </summary>
public sealed class SubmitReinforcementAnswerRequest
{
    public string ConceptKey { get; set; } = string.Empty;
    public string StudentAnswer { get; set; } = string.Empty;
    public Guid? LessonId { get; set; }
}

/// <summary>
/// Result of evaluating a reinforcement verification answer.
/// </summary>
public sealed class ReinforcementEvaluationResultDto
{
    public string ConceptKey { get; set; } = string.Empty;
    public double Score { get; set; }
    public LearningLevel NewLevel { get; set; }
    public double NewConfidence { get; set; }
    public bool IsMasteredOrUnderstood { get; set; }
    public string Feedback { get; set; } = string.Empty;
    public List<string> UnderstoodPoints { get; set; } = [];
    public List<string> AdvicePoints { get; set; } = [];
}

/// <summary>
/// Flashcard for active recall & spaced repetition.
/// </summary>
public sealed class ReviewFlashcardDto
{
    public string Id { get; set; } = string.Empty;
    public string ConceptKey { get; set; } = string.Empty;
    public string Category { get; set; } = "مسألة فقهية"; // مسألة فقهية | استدلال ودليل | مصطلح وغريب | علم وراوٍ
    public string FrontText { get; set; } = string.Empty;
    public string FrontSubtitle { get; set; } = string.Empty;
    public string BackTitle { get; set; } = string.Empty;
    public string BackExplanation { get; set; } = string.Empty;
    public string? EvidenceSnippet { get; set; }
    public string? MemoryTip { get; set; }
    public LearningLevel CurrentLevel { get; set; }
    public double Confidence { get; set; }
    public bool IsWeak { get; set; }
    public bool IsReviewDue { get; set; }
    public Guid? SourceLessonId { get; set; }
    public string? LessonTitle { get; set; }
    public List<int> SourcePages { get; set; } = [];
}

/// <summary>
/// Request to rate a flashcard during spaced repetition drill.
/// </summary>
public sealed class FlashcardRatingRequest
{
    public string ConceptKey { get; set; } = string.Empty;
    public string Rating { get; set; } = "Good"; // Again (صعب) | Hard (متوسط) | Good (جيد) | Easy (متقن)
}

/// <summary>
/// Flashcard rating result.
/// </summary>
public sealed class FlashcardRatingResultDto
{
    public string ConceptKey { get; set; } = string.Empty;
    public LearningLevel NewLevel { get; set; }
    public double NewConfidence { get; set; }
    public string NextReviewScheduled { get; set; } = string.Empty;
}

/// <summary>
/// Concept item in the comprehensive Mastery Matrix.
/// </summary>
public sealed class MasteryMatrixItemDto
{
    public Guid Id { get; set; }
    public string ConceptKey { get; set; } = string.Empty;
    public string Category { get; set; } = "مسألة علمية";
    public LearningLevel LearningLevel { get; set; }
    public double Confidence { get; set; }
    public double MasteryScore { get; set; }
    public int ExposureCount { get; set; }
    public int AssessmentCount { get; set; }
    public int CorrectAnswerCount { get; set; }
    public int DemonstratedContextCount { get; set; }
    public bool IsWeak { get; set; }
    public bool IsReviewDue { get; set; }
    public DateTime? LastAssessedAt { get; set; }
    public Guid? LessonId { get; set; }
    public string? LessonTitle { get; set; }
    public List<int> SourcePages { get; set; } = [];
    public string StatusDescription { get; set; } = string.Empty;
}

/// <summary>
/// Multi-concept quick review quiz item.
/// </summary>
public sealed class QuickReviewQuestionDto
{
    public int Index { get; set; }
    public string ConceptKey { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string QuestionType { get; set; } = "Explanation";
    public string Difficulty { get; set; } = "Intermediate";
    public string Guidance { get; set; } = string.Empty;
    public Guid? SourceLessonId { get; set; }
    public string? LessonTitle { get; set; }
}

/// <summary>
/// Complete quick review quiz session.
/// </summary>
public sealed class QuickReviewQuizDto
{
    public Guid BookId { get; set; }
    public string Title { get; set; } = "اختبار التثبيت والمراجعة السريع";
    public List<QuickReviewQuestionDto> Questions { get; set; } = [];
}
