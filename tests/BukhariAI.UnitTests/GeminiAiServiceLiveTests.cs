using BukhariAI.Application.Lessons.GenerateLesson;
using BukhariAI.Infrastructure.AI;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace BukhariAI.UnitTests;

public class GeminiAiServiceLiveTests
{
    private readonly ITestOutputHelper _output;

    // Shared sample text representing page 110 of the source book
    private const string Page110Text = """
        [PDF Page 110]
        في هذا الحديث: دليلٌ على أن قولَه تعالى: ﴿حُرِّمَتْ عَلَيْكُمُ الْمَيْتَةُ﴾ [المائدة: 3] ليس عاماً في جميع وجوه الانتفاع؛ إنما المحرم أكلها، وبناء على ذلك لو أننا انتفعنا بشحمها ولحمها في غير الأكل جاز ذلك؛ لأن كلمة: «إنما حرم أكلها» تدل على الحصر، وعليه فيجوز أن تُطلى بشحومها السفن، وتُدهن بها الجلود، ولا حرج في ذلك.
        ولما حرم النبي ﷺ بيع الميتة قالوا: يا رسول الله، أرأيت شحوم الميتة؛ فإنها تطلى بها السفن وتدهن بها الجلود ويستصبح بها الناس؟ قال: «لا، هو حرام». فلما قال هذا اختلف العلماء في قوله: «هو حرام» هل يعود على ما ذكر من الانتفاع، أو يعود على ما السياق فيه، ألا وهو البيع.
        وهذا الحديث يؤيد أنه يعود على البيع، وفي لفظ آخر قال: «يطهرها الماء والقرظ» يعني: الدبغ؛ دل هذا على أن جلد الميتة يسلخ من الميتة ويطهر بالدبغ؛ فإذا طهر بالدبغ جاز استعماله في اليابسات وغير اليابسات.
        """;

    public GeminiAiServiceLiveTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private (GeminiAiService service, bool hasKey) CreateService()
    {
        string? apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.Process)
                         ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.User)
                         ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.Machine);

        if (string.IsNullOrWhiteSpace(apiKey)) return (null!, false);

        var options = Options.Create(new AiOptions
        {
            Provider = "Gemini",
            Model = "gemini-3.6-flash",
            ApiKey = apiKey
        });

        var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        var promptBuilder = new LessonPromptBuilder();
        var logger = NullLogger<GeminiAiService>.Instance;
        return (new GeminiAiService(httpClient, options, promptBuilder, logger), true);
    }

    [Fact]
    public async Task GenerateLessonAsync_WithRealGeminiApi_GeneratesValidEpistemicLesson()
    {
        var (service, hasKey) = CreateService();
        if (!hasKey)
        {
            _output.WriteLine("[SKIP] GEMINI_API_KEY not set.");
            return;
        }

        _output.WriteLine("Sending live request to Gemini API (no context)...");

        // Act
        GenerateLessonResponse? lesson = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                lesson = await service.GenerateLessonAsync(Page110Text);
                break;
            }
            catch (HttpRequestException ex) when (attempt < 3)
            {
                _output.WriteLine($"[WARNING] Transient network error on attempt {attempt}: {ex.Message}. Retrying...");
                await Task.Delay(2000);
            }
            catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
            {
                _output.WriteLine($"[SKIP] Live Gemini API endpoint unreachable: {ex.Message}");
                return;
            }
        }

        // Assert & Log
        lesson.Should().NotBeNull();
        PrintLesson(lesson!);

        lesson!.Title.Should().NotBeNullOrWhiteSpace();
        lesson.Overview.Should().NotBeNullOrWhiteSpace();
        lesson.Hadiths.Should().NotBeEmpty();
        
        var h = lesson.Hadiths[0];
        h.Problem.Should().NotBeNullOrWhiteSpace("Problem must be present in epistemic schema");
        h.Conclusion.Should().NotBeNullOrWhiteSpace("Conclusion must be present in epistemic schema");
        h.Reasoning.Should().NotBeNullOrWhiteSpace("Reasoning must be present in epistemic schema");
        h.EasyExplanation.Should().NotBeNullOrWhiteSpace("EasyExplanation must be present in epistemic schema");
        h.SourcePages.Should().Contain(110);
        h.Evidence.Should().NotBeEmpty("Evidence items should be extracted from the source");
    }

    [Fact]
    public async Task GenerateLessonAsync_WithLessonLearningContext_InjectsContextIntoPrompt()
    {
        var (service, hasKey) = CreateService();
        if (!hasKey)
        {
            _output.WriteLine("[SKIP] GEMINI_API_KEY not set.");
            return;
        }

        var context = new LessonLearningContext
        {
            People = ["النبي محمد ﷺ — رسول الله وصاحب الشريعة الإسلامية"],
            Terms = ["الدباغ — معالجة جلد الحيوان بالماء والقرظ حتى يطهر ويصبح صالحًا للاستعمال"],
            KeyIdeas = ["جلد الميتة يطهر بالدباغ ويجوز استعماله في اليابسات وغير اليابسات بعد التطهير"],
            PreviousDiscussions = ["مسألة بيع شحوم الميتة والخلاف الفقهي حول قوله ﷺ «هو حرام»: هل يعود على البيع أم الانتفاع؟"]
        };

        _output.WriteLine("Sending live request to Gemini API (WITH LessonLearningContext)...");

        // Act
        GenerateLessonResponse? lesson = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                lesson = await service.GenerateLessonAsync(Page110Text, context);
                break;
            }
            catch (HttpRequestException ex) when (attempt < 3)
            {
                _output.WriteLine($"[WARNING] Transient network error on attempt {attempt}: {ex.Message}. Retrying...");
                await Task.Delay(2000);
            }
            catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
            {
                _output.WriteLine($"[SKIP] Live Gemini API endpoint unreachable: {ex.Message}");
                return;
            }
        }

        // Assert & Log
        lesson.Should().NotBeNull();
        PrintLesson(lesson!);

        lesson!.Hadiths.Should().NotBeEmpty();
        lesson.Hadiths[0].Problem.Should().NotBeNullOrWhiteSpace();
        lesson.Hadiths[0].Conclusion.Should().NotBeNullOrWhiteSpace();
        lesson.Hadiths[0].EasyExplanation.Should().NotBeNullOrWhiteSpace();
    }

    private void PrintLesson(GenerateLessonResponse lesson)
    {
        _output.WriteLine("\n=============================================================");
        _output.WriteLine("  GEMINI LESSON GENERATION RESULT (Epistemic Schema)");
        _output.WriteLine("=============================================================");
        _output.WriteLine($"Title:              {lesson.Title}");
        _output.WriteLine($"Overview:           {lesson.Overview}");
        _output.WriteLine($"Historical Context: {lesson.HistoricalContext}");
        _output.WriteLine($"Source Pages:       [{string.Join(", ", lesson.SourcePages)}]");
        _output.WriteLine($"Hadiths Count:      {lesson.Hadiths.Count}");

        foreach (var h in lesson.Hadiths)
        {
            _output.WriteLine($"\n--- [{h.Reference}] ---");
            _output.WriteLine($"SourcePages:            [{string.Join(", ", h.SourcePages)}]");
            _output.WriteLine($"Summary:                {h.Summary}");
            _output.WriteLine($"Problem:                {h.Problem}");
            _output.WriteLine($"Evidence ({h.Evidence.Count} items):");
            foreach (var e in h.Evidence)
                _output.WriteLine($"  - Text: \"{e.Text}\" | Role: \"{e.Role}\" | SourcePages: [{string.Join(", ", e.SourcePages)}]");
            _output.WriteLine($"Reasoning:              {h.Reasoning}");
            _output.WriteLine($"ScholarlyDiscussion:    {h.ScholarlyDiscussion}");
            _output.WriteLine($"Conclusion:             {h.Conclusion}");
            _output.WriteLine($"EasyExplanation:        {h.EasyExplanation}");
            _output.WriteLine($"People:                 [{string.Join(" | ", h.People)}]");
            _output.WriteLine($"Lessons:                [{string.Join(" | ", h.Lessons)}]");
            _output.WriteLine($"Connections (intra):    [{string.Join(" | ", h.Connections)}]");
        }

        _output.WriteLine($"\nConnections (lesson):   [{string.Join(" | ", lesson.Connections)}]");
        _output.WriteLine($"Review Questions:       [{string.Join(" | ", lesson.ReviewQuestions)}]");
        _output.WriteLine("=============================================================");

        var json = System.Text.Json.JsonSerializer.Serialize(lesson, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        _output.WriteLine("\n[FULL JSON OUTPUT]:\n" + json);
    }
}
