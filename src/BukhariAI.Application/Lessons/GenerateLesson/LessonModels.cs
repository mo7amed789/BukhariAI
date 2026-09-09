namespace BukhariAI.Application.Lessons.GenerateLesson;

using BukhariAI.Application.AdaptiveLearning;
using BukhariAI.Domain.Entities;

public sealed class GenerateLessonResponse
{
    public Guid? LessonId { get; set; }

    public string Title { get; init; } = string.Empty;

    public string Overview { get; init; } = string.Empty;

    public string HistoricalContext { get; init; } = string.Empty;

    public List<int> SourcePages { get; init; } = [];

    public List<HadithExplanation> Hadiths { get; init; } = [];

    public List<string> Connections { get; init; } = [];

    public List<string> ReviewQuestions { get; init; } = [];

    public List<AssessmentQuestionDto> AssessmentQuestions { get; init; } = [];

    public LessonMetadata Metadata { get; init; } = new();
}

/// <summary>
/// Epistemic breakdown of a single hadith, issue, or passage.
/// Each field maps to a layer of scientific reasoning — not just a summary.
/// </summary>
public sealed class HadithExplanation
{
    /// <summary>A citation or heading identifying this hadith or issue.</summary>
    public string Reference { get; init; } = string.Empty;

    public List<int> SourcePages { get; init; } = [];

    /// <summary>A brief 2–3 sentence overview of what this section discusses.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// The scholarly issue or jurisprudential question the author is addressing.
    /// E.g. "هل يجوز الانتفاع بشحوم الميتة في غير الأكل؟"
    /// </summary>
    public string Problem { get; init; } = string.Empty;

    /// <summary>
    /// Textual evidences (ayat, hadiths, principles) the author marshals,
    /// each annotated with its role in the argument.
    /// </summary>
    public List<Evidence> Evidence { get; init; } = [];

    /// <summary>
    /// How the author moves from the evidences to the conclusion —
    /// the chain of inferential steps, strictly as presented in the text.
    /// </summary>
    public string Reasoning { get; init; } = string.Empty;

    /// <summary>
    /// Any scholarly disagreement the author presents:
    /// the dispute, the positions, the grounds, and the author's preference if stated.
    /// </summary>
    public string ScholarlyDiscussion { get; init; } = string.Empty;

    /// <summary>The final ruling or result the author arrives at in this section.</summary>
    public string Conclusion { get; init; } = string.Empty;

    /// <summary>
    /// Beginner-friendly explanation with difficult terms explained inline in parentheses.
    /// </summary>
    public string EasyExplanation { get; init; } = string.Empty;

    public List<string> People { get; init; } = [];

    public List<string> Places { get; init; } = [];

    /// <summary>Key takeaways the student should retain from this section.</summary>
    public List<string> Lessons { get; init; } = [];

    /// <summary>
    /// Connections between this issue and other issues within the same lesson pages.
    /// </summary>
    public List<string> Connections { get; init; } = [];
}

/// <summary>
/// A single piece of evidence cited by the author, annotated with its argumentative role and source pages.
/// </summary>
public sealed class Evidence
{
    /// <summary>The text of the ayah, hadith, or principle as it appears in the source.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// The role this evidence plays in the argument.
    /// E.g. "دليل أصلي" / "تخصيص الحكم" / "استثناء" / "جواب اعتراض" / "قياس"
    /// </summary>
    public string Role { get; init; } = string.Empty;

    /// <summary>The page numbers where this evidence appears.</summary>
    public List<int> SourcePages { get; init; } = [];
}

/// <summary>
/// Accumulative learning context passed from prior lesson sessions.
/// The client holds and sends this; the backend never persists it.
/// </summary>
public sealed class LessonLearningContext
{
    /// <summary>People already introduced in previous lessons (with their brief descriptions).</summary>
    public List<string> People { get; init; } = [];

    /// <summary>Technical terms already explained in previous lessons.</summary>
    public List<string> Terms { get; init; } = [];

    /// <summary>Topics/chapters already covered.</summary>
    public List<string> Topics { get; init; } = [];

    /// <summary>Core ideas or rulings already established.</summary>
    public List<string> KeyIdeas { get; init; } = [];

    /// <summary>Brief summaries of previous scholarly discussions.</summary>
    public List<string> PreviousDiscussions { get; init; } = [];

    /// <summary>Cross-issue connections already drawn in previous lessons.</summary>
    public List<string> EstablishedConnections { get; init; } = [];

    /// <summary>Active student concept mastery profile from previous assessments.</summary>
    public List<StudentConceptMasteryItem> StudentMastery { get; init; } = [];

    public LearningIntent LearningIntent { get; set; } = LearningIntent.NewLesson;

    public List<AdaptiveConceptState> AdaptiveDecisions { get; set; } = [];
}

/// <summary>
/// Assessment question generated for the lesson.
/// </summary>
public sealed class AssessmentQuestionDto
{
    /// <summary>The question text testing conceptual understanding.</summary>
    public string Question { get; init; } = string.Empty;

    /// <summary>Question type (e.g. ConceptualExplanation, EvidenceAnalysis, RulingsAndApplication, ScholarlyDispute).</summary>
    public string QuestionType { get; init; } = "ConceptualExplanation";

    /// <summary>Difficulty level (Beginner, Intermediate, Advanced).</summary>
    public string Difficulty { get; init; } = "Intermediate";

    /// <summary>Key concepts or terms that the student is expected to demonstrate understanding of.</summary>
    public List<string> ExpectedConcepts { get; init; } = [];

    /// <summary>Source pages supporting this question.</summary>
    public List<int> SourcePages { get; init; } = [];

    /// <summary>Rubric/criteria for the AI evaluator to assess understanding.</summary>
    public string EvaluationGuidance { get; init; } = string.Empty;
}

/// <summary>
/// A student concept mastery item passed in educational memory context.
/// </summary>
public sealed class StudentConceptMasteryItem
{
    public string Concept { get; init; } = string.Empty;

    public BukhariAI.Domain.Entities.LearningLevel Level { get; init; } = BukhariAI.Domain.Entities.LearningLevel.Introduced;

    public double Score { get; init; }

    public int ExposureCount { get; init; }

    public int AssessmentCount { get; init; }
}

public sealed class LessonMetadata
{
    public int StartPage { get; init; }

    public int EndPage { get; init; }

    public int TotalPagesProcessed { get; init; }

    /// <summary>Always false: pages are sent directly to the Vision model.</summary>
    public bool UsedOcr { get; init; }

    public DateTime GeneratedAtUtc { get; init; } = DateTime.UtcNow;
}
