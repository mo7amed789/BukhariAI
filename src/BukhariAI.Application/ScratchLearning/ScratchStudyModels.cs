using System.Text.Json.Serialization;

namespace BukhariAI.Application.ScratchLearning;

/// <summary>
/// Request to generate or retrieve a multi-level roadmap for learning a topic from scratch.
/// </summary>
public sealed class ScratchRoadmapRequest
{
    [JsonPropertyName("topic")]
    public string Topic { get; init; } = string.Empty;

    [JsonPropertyName("trackId")]
    public string? TrackId { get; init; }

    [JsonPropertyName("targetAudience")]
    public string TargetAudience { get; init; } = "مبتدئ تماماً (من الصفر)";

    [JsonPropertyName("depthLevel")]
    public string DepthLevel { get; init; } = "شامل متدرج (من التأسيس حتى الإتقان)";
}

/// <summary>
/// Curated pre-built foundational track metadata.
/// </summary>
public sealed class CuratedScratchTrack
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("icon")]
    public string Icon { get; init; } = "🌱";

    [JsonPropertyName("tagline")]
    public string Tagline { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("estimatedMinutes")]
    public int EstimatedMinutes { get; init; } = 30;

    [JsonPropertyName("levelsCount")]
    public int LevelsCount { get; init; } = 4;

    [JsonPropertyName("difficultyBadge")]
    public string DifficultyBadge { get; init; } = "يبدأ من الصفر 🟢";
}

/// <summary>
/// A single milestone / station inside a learning level.
/// </summary>
public sealed class ScratchMilestone
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("order")]
    public int Order { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("shortSummary")]
    public string ShortSummary { get; init; } = string.Empty;

    [JsonPropertyName("icon")]
    public string Icon { get; init; } = "📌";

    [JsonPropertyName("keyTerm")]
    public string KeyTerm { get; init; } = string.Empty;

    [JsonPropertyName("explanation")]
    public ScratchStepExplanation? Explanation { get; set; }
}

/// <summary>
/// A progressive level on the roadmap (e.g. المستوى 1: التأسيس, المستوى 2: البناء...).
/// </summary>
public sealed class ScratchLevel
{
    [JsonPropertyName("levelNumber")]
    public int LevelNumber { get; init; }

    [JsonPropertyName("levelName")]
    public string LevelName { get; init; } = string.Empty;

    [JsonPropertyName("badge")]
    public string Badge { get; init; } = string.Empty;

    [JsonPropertyName("colorTheme")]
    public string ColorTheme { get; init; } = "#10B981"; // Emerald default

    [JsonPropertyName("objective")]
    public string Objective { get; init; } = string.Empty;

    [JsonPropertyName("milestones")]
    public List<ScratchMilestone> Milestones { get; init; } = [];
}

/// <summary>
/// Complete Roadmap representation for a topic.
/// </summary>
public sealed class ScratchRoadmapResponse
{
    [JsonPropertyName("topic")]
    public string Topic { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("introduction")]
    public string Introduction { get; init; } = string.Empty;

    [JsonPropertyName("targetAudience")]
    public string TargetAudience { get; init; } = string.Empty;

    [JsonPropertyName("totalMilestones")]
    public int TotalMilestones { get; init; }

    [JsonPropertyName("estimatedTotalTimeMinutes")]
    public int EstimatedTotalTimeMinutes { get; init; }

    [JsonPropertyName("levels")]
    public List<ScratchLevel> Levels { get; init; } = [];
}

/// <summary>
/// Step-by-step breakdown item.
/// </summary>
public sealed class ScratchExplanationStep
{
    [JsonPropertyName("stepNumber")]
    public int StepNumber { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("explanation")]
    public string Explanation { get; init; } = string.Empty;
}

/// <summary>
/// Checkpoint Quiz for verifying understanding.
/// </summary>
public sealed class ScratchQuiz
{
    [JsonPropertyName("question")]
    public string Question { get; init; } = string.Empty;

    [JsonPropertyName("options")]
    public List<string> Options { get; init; } = [];

    [JsonPropertyName("correctIndex")]
    public int CorrectIndex { get; init; }

    [JsonPropertyName("explanation")]
    public string Explanation { get; init; } = string.Empty;

    [JsonPropertyName("reinforcementTip")]
    public string ReinforcementTip { get; init; } = string.Empty;
}

/// <summary>
/// Deep methodical explanation for a specific milestone.
/// </summary>
public sealed class ScratchStepExplanation
{
    [JsonPropertyName("milestoneId")]
    public string MilestoneId { get; init; } = string.Empty;

    [JsonPropertyName("milestoneTitle")]
    public string MilestoneTitle { get; init; } = string.Empty;

    [JsonPropertyName("levelName")]
    public string LevelName { get; init; } = string.Empty;

    [JsonPropertyName("simpleConcept")]
    public string SimpleConcept { get; init; } = string.Empty; // الفكرة ببساطة (ELI5)

    [JsonPropertyName("realWorldAnalogy")]
    public string RealWorldAnalogy { get; init; } = string.Empty; // التشبيه الواقعي / المشبه به

    [JsonPropertyName("detailedExplanation")]
    public string DetailedExplanation { get; init; } = string.Empty; // الشرح المنهجي

    [JsonPropertyName("steps")]
    public List<ScratchExplanationStep> Steps { get; init; } = []; // خطوات الفهم

    [JsonPropertyName("commonPitfalls")]
    public List<string> CommonPitfalls { get; init; } = []; // أخطاء شائعة

    [JsonPropertyName("practicalExample")]
    public string PracticalExample { get; init; } = string.Empty; // مثال من التراث / البخاري

    [JsonPropertyName("quiz")]
    public ScratchQuiz? Quiz { get; init; } // اختبار التحقق السريع
}

/// <summary>
/// Request to generate a deep explanation for a single milestone.
/// </summary>
public sealed class ExplainScratchStepRequest
{
    [JsonPropertyName("topic")]
    public string Topic { get; init; } = string.Empty;

    [JsonPropertyName("levelNumber")]
    public int LevelNumber { get; init; } = 1;

    [JsonPropertyName("levelName")]
    public string LevelName { get; init; } = string.Empty;

    [JsonPropertyName("milestoneTitle")]
    public string MilestoneTitle { get; init; } = string.Empty;

    [JsonPropertyName("keyTerm")]
    public string? KeyTerm { get; init; }
}

/// <summary>
/// Request to ask the Scratch Tutor a clarifying question.
/// </summary>
public sealed class AskScratchTutorRequest
{
    [JsonPropertyName("topic")]
    public string Topic { get; init; } = string.Empty;

    [JsonPropertyName("currentMilestone")]
    public string CurrentMilestone { get; init; } = string.Empty;

    [JsonPropertyName("userQuestion")]
    public string UserQuestion { get; init; } = string.Empty;

    [JsonPropertyName("simplicityMode")]
    public string SimplicityMode { get; init; } = "أبسط ما يمكن (مثل شرح لطفل/مبتدئ)";
}

/// <summary>
/// Response from the Scratch Tutor.
/// </summary>
public sealed class AskScratchTutorResponse
{
    [JsonPropertyName("answer")]
    public string Answer { get; init; } = string.Empty;

    [JsonPropertyName("simplifiedAnalogy")]
    public string SimplifiedAnalogy { get; init; } = string.Empty;

    [JsonPropertyName("keyTakeaway")]
    public string KeyTakeaway { get; init; } = string.Empty;

    [JsonPropertyName("followUpSuggestion")]
    public string FollowUpSuggestion { get; init; } = string.Empty;
}
