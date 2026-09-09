using System.Text.Json.Serialization;

namespace BukhariAI.Application.Quran;

public sealed class SurahMeta
{
    public int Number { get; init; }
    public string Name { get; init; } = string.Empty;
    public string EnglishName { get; init; } = string.Empty;
    public int TotalAyat { get; init; }
    public string RevelationType { get; init; } = "مكية"; // مكية أو مدنية
}

public sealed class AnalyzeQuranRequest
{
    public int? SurahNumber { get; init; }
    public string? SurahName { get; init; }
    public int? StartAyah { get; init; }
    public int? EndAyah { get; init; }
    public string? CustomText { get; init; }
}

public sealed class QuranThematicSection
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("ayahRange")]
    public string AyahRange { get; init; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;

    [JsonPropertyName("themeColor")]
    public string ThemeColor { get; init; } = "#10B981";

    [JsonPropertyName("icon")]
    public string Icon { get; init; } = "📌";
}

public sealed class QuranVocabularyItem
{
    [JsonPropertyName("word")]
    public string Word { get; init; } = string.Empty;

    [JsonPropertyName("meaning")]
    public string Meaning { get; init; } = string.Empty;
}

public sealed class QuranAyahAnalysis
{
    [JsonPropertyName("ayahNumber")]
    public int AyahNumber { get; init; }

    [JsonPropertyName("ayahText")]
    public string AyahText { get; init; } = string.Empty;

    [JsonPropertyName("generalMeaning")]
    public string GeneralMeaning { get; init; } = string.Empty;

    [JsonPropertyName("vocabulary")]
    public List<QuranVocabularyItem> Vocabulary { get; init; } = [];

    [JsonPropertyName("contextWithNext")]
    public string ContextWithNext { get; init; } = string.Empty;

    [JsonPropertyName("endingFasila")]
    public string EndingFasila { get; init; } = string.Empty;

    [JsonPropertyName("endingReason")]
    public string EndingReason { get; init; } = string.Empty;

    [JsonPropertyName("memoryAnchor")]
    public string MemoryAnchor { get; init; } = string.Empty;

    [JsonPropertyName("actionableTadabbur")]
    public string ActionableTadabbur { get; init; } = string.Empty;

    [JsonPropertyName("tajweedOrRecitationNote")]
    public string TajweedOrRecitationNote { get; init; } = string.Empty;

    [JsonPropertyName("asbabNuzul")]
    public string AsbabNuzul { get; init; } = string.Empty;
}

public sealed class QuranMindMapNode
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("parentId")]
    public string? ParentId { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; init; }

    [JsonPropertyName("ayahRange")]
    public string? AyahRange { get; init; }

    [JsonPropertyName("icon")]
    public string Icon { get; init; } = "🌳";

    [JsonPropertyName("color")]
    public string Color { get; init; } = "#0D9488";

    [JsonPropertyName("order")]
    public int Order { get; init; }
}

/// <summary>
/// A semantic node in the surah knowledge map.  Unlike the compact mind-map
/// tree, this preserves the ayah evidence and the level of the topic.
/// </summary>
public sealed class QuranKnowledgeMapNode
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("parentId")]
    public string? ParentId { get; init; }

    [JsonPropertyName("level")]
    public int Level { get; init; } = 2;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;

    [JsonPropertyName("ayahNumbers")]
    public List<int> AyahNumbers { get; init; } = [];

    [JsonPropertyName("keyConcepts")]
    public List<string> KeyConcepts { get; init; } = [];

    [JsonPropertyName("memorizationCue")]
    public string MemorizationCue { get; init; } = string.Empty;

    [JsonPropertyName("isKeyPassage")]
    public bool IsKeyPassage { get; init; }

    [JsonPropertyName("order")]
    public int Order { get; init; }
}

/// <summary>Explains either the normal sequential flow or a cross-link in a surah.</summary>
public sealed class QuranKnowledgeMapLink
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("fromId")]
    public string FromId { get; init; } = string.Empty;

    [JsonPropertyName("toId")]
    public string ToId { get; init; } = string.Empty;

    [JsonPropertyName("relationType")]
    public string RelationType { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("isCrossLink")]
    public bool IsCrossLink { get; init; }

    [JsonPropertyName("order")]
    public int Order { get; init; }
}

public sealed class QuranKnowledgeMap
{
    [JsonPropertyName("nodes")]
    public List<QuranKnowledgeMapNode> Nodes { get; init; } = [];

    [JsonPropertyName("links")]
    public List<QuranKnowledgeMapLink> Links { get; init; } = [];

    [JsonPropertyName("overallJourney")]
    public string OverallJourney { get; init; } = string.Empty;

    [JsonPropertyName("openingClosingConnection")]
    public string OpeningClosingConnection { get; init; } = string.Empty;
}

public sealed class QuranMutashabihItem
{
    [JsonPropertyName("baseAyahNumber")]
    public int BaseAyahNumber { get; init; }

    [JsonPropertyName("baseAyahText")]
    public string BaseAyahText { get; init; } = string.Empty;

    [JsonPropertyName("similarSurahOrAyah")]
    public string SimilarSurahOrAyah { get; init; } = string.Empty;

    [JsonPropertyName("similarText")]
    public string SimilarText { get; init; } = string.Empty;

    [JsonPropertyName("differenceSummary")]
    public string DifferenceSummary { get; init; } = string.Empty;

    [JsonPropertyName("mnemonicRule")]
    public string MnemonicRule { get; init; } = string.Empty;
}

public sealed class QuranSurahInfo
{
    [JsonPropertyName("number")]
    public int Number { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("revelationType")]
    public string RevelationType { get; init; } = "مكية";

    [JsonPropertyName("totalAyat")]
    public int TotalAyat { get; init; }

    [JsonPropertyName("analyzedRange")]
    public string AnalyzedRange { get; init; } = string.Empty;

    [JsonPropertyName("mainObjective")]
    public string MainObjective { get; init; } = string.Empty;

    [JsonPropertyName("names")]
    public List<string> Names { get; init; } = [];

    [JsonPropertyName("virtues")]
    public List<string> Virtues { get; init; } = [];

    [JsonPropertyName("asbabNuzulGeneral")]
    public string AsbabNuzulGeneral { get; init; } = string.Empty;

    [JsonPropertyName("memorizationPlan")]
    public string MemorizationPlan { get; init; } = string.Empty;
}

public sealed class QuranMemorizationQuizItem
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("quizType")]
    public string QuizType { get; init; } = "nextAyah"; // nextAyah | fasila | mutashabih | meaning

    [JsonPropertyName("question")]
    public string Question { get; init; } = string.Empty;

    [JsonPropertyName("options")]
    public List<string> Options { get; init; } = [];

    [JsonPropertyName("correctAnswer")]
    public string CorrectAnswer { get; init; } = string.Empty;

    [JsonPropertyName("explanation")]
    public string Explanation { get; init; } = string.Empty;

    [JsonPropertyName("ayahNumber")]
    public int AyahNumber { get; init; }
}

public sealed class QuranSurahAnalysisResponse
{
    [JsonPropertyName("surahInfo")]
    public QuranSurahInfo SurahInfo { get; init; } = new();

    [JsonPropertyName("thematicSections")]
    public List<QuranThematicSection> ThematicSections { get; init; } = [];

    [JsonPropertyName("ayahAnalyses")]
    public List<QuranAyahAnalysis> AyahAnalyses { get; init; } = [];

    [JsonPropertyName("mindMap")]
    public List<QuranMindMapNode> MindMap { get; init; } = [];

    [JsonPropertyName("knowledgeMap")]
    public QuranKnowledgeMap KnowledgeMap { get; init; } = new();

    [JsonPropertyName("mutashabihat")]
    public List<QuranMutashabihItem> Mutashabihat { get; init; } = [];

    [JsonPropertyName("coreReflections")]
    public List<string> CoreReflections { get; init; } = [];

    [JsonPropertyName("virtues")]
    public List<string> Virtues { get; init; } = [];

    [JsonPropertyName("memorizationQuizzes")]
    public List<QuranMemorizationQuizItem> MemorizationQuizzes { get; init; } = [];

    [JsonPropertyName("waqfAndIbtida")]
    public List<QuranWaqfItem> WaqfAndIbtida { get; init; } = [];

    [JsonPropertyName("lessonId")]
    public Guid? LessonId { get; set; }

    [JsonPropertyName("isExistingLessonUpdated")]
    public bool IsExistingLessonUpdated { get; set; }
}

public sealed class QuranWaqfItem
{
    [JsonPropertyName("ayahNumber")]
    public int AyahNumber { get; init; }

    [JsonPropertyName("ayahText")]
    public string AyahText { get; init; } = string.Empty;

    [JsonPropertyName("stopPosition")]
    public string StopPosition { get; init; } = string.Empty;

    [JsonPropertyName("waqfType")]
    public string WaqfType { get; init; } = "كافٍ"; // تام | كافٍ | حسن | قبيح | لازم | جائز | تعانق

    [JsonPropertyName("mushafSign")]
    public string MushafSign { get; init; } = "قلي"; // مـ | قلي | صلي | ج | لا | ∴

    [JsonPropertyName("explanationAndTadabbur")]
    public string ExplanationAndTadabbur { get; init; } = string.Empty;

    [JsonPropertyName("ibtidaGuidance")]
    public string IbtidaGuidance { get; init; } = string.Empty;
}
