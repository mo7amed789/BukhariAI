using BukhariAI.Api.Controllers;
using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Quran;
using BukhariAI.Infrastructure.Quran;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;
using Xunit;

namespace BukhariAI.UnitTests;

public class QuranAnalysisTests
{
    [Fact]
    public void QuranDataCatalog_ShouldContain114SurahsWithAccurateMetadata()
    {
        var surahs = QuranDataCatalog.AllSurahs;
        surahs.Should().HaveCount(114);

        var fatiha = QuranDataCatalog.FindByNumber(1);
        fatiha.Should().NotBeNull();
        fatiha!.Name.Should().Be("الفاتحة");
        fatiha.TotalAyat.Should().Be(7);
        fatiha.RevelationType.Should().Be("مكية");

        var baqarah = QuranDataCatalog.FindByName("البقرة");
        baqarah.Should().NotBeNull();
        baqarah!.Number.Should().Be(2);
        baqarah.TotalAyat.Should().Be(286);
        baqarah.RevelationType.Should().Be("مدنية");

        var mulk = QuranDataCatalog.FindByName("سورة الملك");
        mulk.Should().NotBeNull();
        mulk!.Number.Should().Be(67);
        mulk.TotalAyat.Should().Be(30);
    }

    [Fact]
    public void QuranPromptBuilder_ShouldConstructSystemAndUserPrompts()
    {
        var builder = new QuranPromptBuilder();
        string systemPrompt = builder.BuildSystemPrompt();
        systemPrompt.Should().Contain("نظم الدرر");
        systemPrompt.Should().Contain("ملاك التأويل");
        systemPrompt.Should().Contain("علم المناسبات والربط البياني المحكم بين الآيات");
        systemPrompt.Should().Contain("thematicSections");
        systemPrompt.Should().Contain("ayahAnalyses");
        systemPrompt.Should().Contain("mutashabihat");
        systemPrompt.Should().Contain("actionableTadabbur");
        systemPrompt.Should().Contain("virtues");
        systemPrompt.Should().NotContain("waqfAndIbtida");
        systemPrompt.Should().NotContain("memorizationQuizzes");
        systemPrompt.Should().NotContain("mindMap");
        systemPrompt.Should().NotContain("knowledgeMap");
        systemPrompt.Should().NotContain("tajweedOrRecitationNote");
        systemPrompt.Should().NotContain("memoryAnchor");

        var request = new AnalyzeQuranRequest
        {
            SurahNumber = 67,
            SurahName = "الملك",
            StartAyah = 1,
            EndAyah = 15
        };

        string userPrompt = builder.BuildUserPrompt(request);
        userPrompt.Should().Contain("الملك");
        userPrompt.Should().Contain("67");
        userPrompt.Should().Contain("من الآية 1 إلى الآية 15");
        userPrompt.Should().NotContain("waqfAndIbtida");
        userPrompt.Should().NotContain("memorizationQuizzes");
    }

    [Fact]
    public void QuranSurahAnalysisResponse_ShouldDeserializeKnowledgeMapWithFlowAndCrossLinks()
    {
        const string json = """
            {
              "surahInfo": { 
                "name": "الملك",
                "names": ["تبارك", "المانعة"],
                "virtues": ["المانعة من عذاب القبر"],
                "memorizationPlan": "مقطع يومياً"
              },
              "knowledgeMap": {
                "overallJourney": "مسار السورة",
                "openingClosingConnection": "صلة البداية بالخاتمة",
                "nodes": [{ "id": "theme_1", "level": 2, "title": "الموضوع الأول", "ayahNumbers": [1, 5], "keyConcepts": ["الملك"], "isKeyPassage": true }],
                "links": [{ "id": "flow_1", "fromId": "theme_1", "toId": "theme_2", "relationType": "نتيجة", "description": "ينتقل السياق إلى الأثر", "isCrossLink": false }, { "id": "cross_1", "fromId": "theme_1", "toId": "theme_3", "relationType": "مقابلة", "description": "صلة متقاطعة", "isCrossLink": true }]
              },
              "ayahAnalyses": [
                {
                  "ayahNumber": 1,
                  "ayahText": "تبارك الذي بيده الملك",
                  "actionableTadabbur": "استشعار ملك الله المطلق",
                  "tajweedOrRecitationNote": "تفخيم الراء",
                  "asbabNuzul": "مكية النزول"
                }
              ],
              "memorizationQuizzes": [
                {
                  "id": "q1",
                  "quizType": "nextAyah",
                  "question": "ما هي الآية التالية؟",
                  "options": ["الذي خلق الموت والحياة", "الذي خلق سبع سماوات"],
                  "correctAnswer": "الذي خلق الموت والحياة",
                  "explanation": "تسلسل خلق الموت والحياة",
                  "ayahNumber": 1
                }
              ],
              "waqfAndIbtida": [
                {
                  "ayahNumber": 2,
                  "ayahText": "الذي خلق الموت والحياة ليبلوكم أيكم أحسن عملا وهو العزيز الغفور",
                  "stopPosition": "أيكم أحسن عملا",
                  "waqfType": "كافٍ",
                  "mushafSign": "ج",
                  "explanationAndTadabbur": "الوقف كاف لانتهاء الغاية من خلق الموت والحياة",
                  "ibtidaGuidance": "يبتدئ بقوله وهو العزيز الغفور"
                }
              ]
            }
            """;

        var response = JsonSerializer.Deserialize<QuranSurahAnalysisResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        response.Should().NotBeNull();
        response!.SurahInfo.Names.Should().Contain("المانعة");
        response.SurahInfo.Virtues.Should().ContainSingle();
        response.KnowledgeMap.Nodes.Should().ContainSingle();
        response.KnowledgeMap.Nodes[0].AyahNumbers.Should().Equal(1, 5);
        response.KnowledgeMap.Links.Should().HaveCount(2);
        response.KnowledgeMap.Links.Should().Contain(link => link.IsCrossLink && link.RelationType == "مقابلة");
        response.AyahAnalyses.Should().ContainSingle();
        response.AyahAnalyses[0].ActionableTadabbur.Should().Be("استشعار ملك الله المطلق");
        response.AyahAnalyses[0].TajweedOrRecitationNote.Should().Be("تفخيم الراء");
        response.MemorizationQuizzes.Should().ContainSingle();
        response.MemorizationQuizzes[0].QuizType.Should().Be("nextAyah");
        response.WaqfAndIbtida.Should().ContainSingle();
        response.WaqfAndIbtida[0].WaqfType.Should().Be("كافٍ");
        response.WaqfAndIbtida[0].MushafSign.Should().Be("ج");
    }

    [Fact]
    public async Task QuranController_GetSurahs_ShouldReturn114Surahs()
    {
        var mockService = new Mock<IQuranAnalysisService>();
        var mockPersistence = new Mock<ILessonPersistenceService>();
        var controller = new QuranController(mockService.Object, mockPersistence.Object, NullLogger<QuranController>.Instance);

        var actionResult = controller.GetSurahs();
        var okResult = actionResult.Should().BeOfType<OkObjectResult>().Subject;
        var list = okResult.Value.Should().BeAssignableTo<IReadOnlyList<SurahMeta>>().Subject;
        list.Should().HaveCount(114);
    }

    [Fact]
    public async Task QuranController_Analyze_WhenValidRequest_ShouldReturnOkWithAnalysisAndPersistLesson()
    {
        var mockService = new Mock<IQuranAnalysisService>();
        var mockPersistence = new Mock<ILessonPersistenceService>();
        var expectedResponse = new QuranSurahAnalysisResponse
        {
            SurahInfo = new QuranSurahInfo
            {
                Number = 67,
                Name = "الملك",
                TotalAyat = 30,
                MainObjective = "إثبات كمال الملك والقدرة",
                Names = ["سورة الملك", "تبارك"],
                Virtues = ["المانعة من عذاب القبر"]
            },
            ThematicSections =
            [
                new QuranThematicSection
                {
                    Id = "sec_1",
                    Title = "عظمة الملك والقدرة",
                    AyahRange = "1 - 5",
                    Summary = "مظاهر القدرة الإلهية"
                }
            ],
            AyahAnalyses =
            [
                new QuranAyahAnalysis
                {
                    AyahNumber = 1,
                    AyahText = "تَبَارَكَ الَّذِي بِيَدِهِ الْمُلْكُ",
                    GeneralMeaning = "تعاظم وثبت خير الله",
                    EndingFasila = "وَهُوَ عَلَىٰ كُلِّ شَيْءٍ قَدِيرٌ",
                    EndingReason = "الملك التام لا يتم إلا بالقدرة الشاملة",
                    MemoryAnchor = "الملك يتبعه الموت والحياة",
                    ActionableTadabbur = "تسليم الأمر لله",
                    TajweedOrRecitationNote = "تفخيم الراء"
                }
            ],
            MindMap =
            [
                new QuranMindMapNode
                {
                    Id = "root",
                    Title = "سورة الملك",
                    Order = 1
                }
            ],
            Mutashabihat =
            [
                new QuranMutashabihItem
                {
                    BaseAyahNumber = 2,
                    BaseAyahText = "الذي خلق الموت والحياة",
                    SimilarSurahOrAyah = "هود 7",
                    DifferenceSummary = "تقديم الموت والحياة",
                    MnemonicRule = "قاعدة المجاورة وسياق السورة"
                }
            ],
            MemorizationQuizzes =
            [
                new QuranMemorizationQuizItem
                {
                    Id = "q1",
                    QuizType = "nextAyah",
                    Question = "ما هي الآية التالية؟",
                    Options = ["الذي خلق الموت والحياة"],
                    CorrectAnswer = "الذي خلق الموت والحياة"
                }
            ],
            WaqfAndIbtida =
            [
                new QuranWaqfItem
                {
                    AyahNumber = 2,
                    AyahText = "الذي خلق الموت والحياة ليبلوكم أيكم أحسن عملا",
                    StopPosition = "أيكم أحسن عملا",
                    WaqfType = "كافٍ",
                    MushafSign = "ج",
                    ExplanationAndTadabbur = "انتهاء الغاية من خلق الموت والحياة",
                    IbtidaGuidance = "يبتدئ بما بعده"
                }
            ]
        };

        var expectedLessonId = Guid.NewGuid();
        mockService.Setup(s => s.AnalyzeSurahAsync(It.IsAny<AnalyzeQuranRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);
        mockPersistence.Setup(p => p.PersistOrUpdateQuranSurahLessonAsync(It.IsAny<QuranSurahAnalysisResponse>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedLessonId);

        var controller = new QuranController(mockService.Object, mockPersistence.Object, NullLogger<QuranController>.Instance);

        var request = new AnalyzeQuranRequest { SurahNumber = 67 };
        var actionResult = await controller.Analyze(request, CancellationToken.None);

        var okResult = actionResult.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<QuranSurahAnalysisResponse>().Subject;
        response.SurahInfo.Name.Should().Be("الملك");
        response.LessonId.Should().Be(expectedLessonId);
        response.ThematicSections.Should().HaveCount(1);
        response.AyahAnalyses.Should().HaveCount(1);
        response.AyahAnalyses[0].ActionableTadabbur.Should().Be("تسليم الأمر لله");
        response.MindMap.Should().HaveCount(1);
        response.Mutashabihat.Should().HaveCount(1);
        response.MemorizationQuizzes.Should().HaveCount(1);
        response.WaqfAndIbtida.Should().HaveCount(1);
        response.WaqfAndIbtida[0].WaqfType.Should().Be("كافٍ");

        mockPersistence.Verify(p => p.PersistOrUpdateQuranSurahLessonAsync(It.IsAny<QuranSurahAnalysisResponse>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void QuranSurahAnalysisResponse_WhenJsonContainsUnescapedInternalQuotes_ShouldBeRepairedAndDeserialized()
    {
        // JSON containing unescaped double quotes inside Arabic asbabNuzul field: "نزلت في "يا عبادي" لما تابوا"
        const string rawMalformedJson = """
            {
              "surahInfo": {
                "number": 39,
                "name": "سورة الزمر",
                "revelationType": "مكية",
                "totalAyat": 75,
                "mainObjective": "إخلاص التوحيد والدين لله"
              },
              "ayahAnalyses": [
                {
                  "ayahNumber": 53,
                  "ayahText": "قُلْ يَا عِبَادِيَ الَّذِينَ أَسْرَفُوا عَلَىٰ أَنفُسِهِمْ",
                  "generalMeaning": "دعوة للتوبة وعدم اليأس من رحمة الله",
                  "asbabNuzul": "روي عن ابن عباس: لما نزلت "يا عبادي" في قوم أذنبوا فتابوا",
                  "actionableTadabbur": "الاستغفار الفوري",
                  "tajweedOrRecitationNote": "مد منفصل",
                  "endingFasila": "إِنَّ اللَّهَ يَغْفِرُ الذُّنُوبَ جَمِيعًا",
                  "endingReason": "سعة المغفرة والرحمة",
                  "memoryAnchor": "النداء بالعبودية يتبعه النهي عن القنوط"
                }
              ]
            }
            """;

        // Testing the parsing logic via reflection / direct repair
        var repairMethod = typeof(QuranAnalysisService).GetMethod("RepairUnescapedQuotesInJson", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        repairMethod.Should().NotBeNull();

        string repaired = (string)repairMethod!.Invoke(null, [rawMalformedJson])!;
        repaired.Should().NotBeNullOrWhiteSpace();

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var response = JsonSerializer.Deserialize<QuranSurahAnalysisResponse>(repaired, options);

        response.Should().NotBeNull();
        response!.SurahInfo.Should().NotBeNull();
        response.SurahInfo!.Name.Should().Be("سورة الزمر");
        response.AyahAnalyses.Should().HaveCount(1);
        response.AyahAnalyses[0].AsbabNuzul.Should().Contain("يا عبادي");
    }
}