using System.Net;
using System.Text.Json;
using BukhariAI.Api.Controllers;
using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Lessons.Biography;
using BukhariAI.Application.Lessons.GenerateLesson;
using BukhariAI.Domain.Entities;
using BukhariAI.Infrastructure.AI;
using BukhariAI.Infrastructure.Pdf;
using BukhariAI.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace BukhariAI.UnitTests;

public class PersonBiographyServiceTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }

    private static BukhariDbContext CreateDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<BukhariDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new BukhariDbContext(options);
    }

    [Fact]
    public void BiographyPromptBuilder_ShouldConstructSystemAndUserPromptsWithSiyarSources()
    {
        var builder = new BiographyPromptBuilder();
        string systemPrompt = builder.BuildSystemPrompt();

        systemPrompt.Should().Contain("سير أعلام النبلاء");
        systemPrompt.Should().Contain("شمس الدين الذهبي");
        systemPrompt.Should().Contain("تهذيب الكمال");
        systemPrompt.Should().Contain("siyarBiography");
        systemPrompt.Should().Contain("scholarlyPraise");
        systemPrompt.Should().Contain("teachers");
        systemPrompt.Should().Contain("students");
        systemPrompt.Should().Contain("sources");

        var request = new GetPersonBiographyRequest
        {
            Name = "عبد الله بن عمر بن الخطاب",
            ContextDescription = "راوي حديث تحريم بيع الخمر والميتة والخنزير"
        };

        string userPrompt = builder.BuildUserPrompt(request);
        userPrompt.Should().Contain("عبد الله بن عمر بن الخطاب");
        userPrompt.Should().Contain("راوي حديث تحريم بيع الخمر");
        userPrompt.Should().Contain("سير أعلام النبلاء");
    }

    [Fact]
    public async Task GetPersonBiographyAsync_WhenCachedInDatabase_ReturnsCachedDataWithoutCallingAi()
    {
        using var db = CreateDbContext("BioTest_Cached");
        var promptBuilder = new BiographyPromptBuilder();
        var settingsService = new SettingsService(db);

        var cachedDto = new PersonBiographyDto
        {
            Name = "عبد الله بن عمر",
            Title = "صحابي جليل، فقيه المدينة",
            Kunya = "أبو عبد الرحمن",
            Era = "الصحابة",
            DeathYear = "73 هـ",
            Summary = "ابن أمير المؤمنين عمر بن الخطاب رضي الله عنهما، وأحد المكثرين من رواية الحديث النبوي.",
            SiyarBiography = "قال الذهبي في سير أعلام النبلاء: الإمام القدوة، شيخ الإسلام، أبو عبد الرحمن القرشي العدوي المكي ثم المدني. هاجر وهو غلام لم يبلغ الحلم، وشهد الخندق وبيعة الرضوان.",
            Teachers = ["رسول الله ﷺ", "عمر بن الخطاب", "أبو بكر الصديق", "عثمان بن عفان"],
            Students = ["نافع مولى ابن عمر", "سالم بن عبد الله", "الزهري", "سعيد بن المسيب"],
            ScholarlyPraise =
            [
                new ScholarlyPraiseDto { Scholar = "الإمام الذهبي", Quote = "الإمام القدوة، شيخ الإسلام، كان من أئمة الهدى والدين، شديد الاتباع للأثر." },
                new ScholarlyPraiseDto { Scholar = "الإمام مالك", Quote = "كان ابن عمر إمام الناس عندنا بعد عمر." }
            ],
            VirtuesAndNarrations = "كان شديد التحري في اتباع آثار النبي ﷺ حتى في المواضع التي نزل فيها.",
            Sources = ["سير أعلام النبلاء - للإمام الذهبي", "الإصابة في تمييز الصحابة - لابن حجر"]
        };

        var person = new Person
        {
            Id = Guid.NewGuid(),
            Name = "عبد الله بن عمر",
            Description = cachedDto.Summary,
            DetailedBiographyJson = JsonSerializer.Serialize(cachedDto),
            CreatedAtUtc = DateTime.UtcNow
        };
        db.People.Add(person);
        await db.SaveChangesAsync();

        // Http client that would throw if called
        var mockHandler = new MockHttpMessageHandler(_ => throw new InvalidOperationException("AI endpoint should not be called when cached."));
        var httpClient = new HttpClient(mockHandler);
        var options = Options.Create(new AiOptions { ApiKey = "test-key", Provider = "Gemini" });

        var service = new PersonBiographyService(
            httpClient,
            options,
            db,
            promptBuilder,
            settingsService,
            NullLogger<PersonBiographyService>.Instance);

        var result = await service.GetPersonBiographyAsync(new GetPersonBiographyRequest { Name = "عبد الله بن عمر" });

        result.Should().NotBeNull();
        result.Name.Should().Be("عبد الله بن عمر");
        result.Title.Should().Contain("فقيه المدينة");
        result.SiyarBiography.Should().Contain("قال الذهبي في سير أعلام النبلاء");
        result.Teachers.Should().Contain("رسول الله ﷺ");
        result.Students.Should().Contain("نافع مولى ابن عمر");
        result.ScholarlyPraise.Should().HaveCount(2);
        result.Sources.Should().Contain("سير أعلام النبلاء - للإمام الذهبي");
    }

    [Fact]
    public async Task GetPersonBiographyAsync_WhenNotCached_CallsAi_ParsesJson_AndCachesInDb()
    {
        var biographyJson = JsonSerializer.Serialize(new
        {
            name = "نافع مولى ابن عمر",
            title = "الإمام الحافظ، فقيه المدينة ومفتيها",
            kunya = "أبو عبد الله المدني",
            era = "الطبقة الثالثة - كبار التابعين",
            deathYear = "117 هـ بالمدينة المنورة",
            summary = "نافع القرشي العدوي مولى عبد الله بن عمر، أحد أعلام التابعين وأوثق الرواة عن ابن عمر.",
            siyarBiography = "ترجم له الإمام الذهبي في سير أعلام النبلاء فقال: الإمام الفقيه، الثبت، عالم المدينة، وشيخ الإمام مالك. روى عن مولاه ابن عمر طائفة كبيرة وصحب نحو ثلاثين سنة.",
            teachers = new[] { "عبد الله بن عمر", "عائشة أم المؤمنين", "أبو هريرة" },
            students = new[] { "مالك بن أنس", "أيوب السختياني", "عبيد الله بن عمر", "ابن عون" },
            scholarlyPraise = new[]
            {
                new { scholar = "الإمام الذهبي", quote = "سلسلة الذهب: مالك عن نافع عن ابن عمر" },
                new { scholar = "يحيى بن معين", quote = "نافع ثقة حجة ثبت" }
            },
            virtuesAndNarrations = "أصح الأسانيد على الإطلاق: مالك عن نافع عن ابن عمر.",
            sources = new[] { "سير أعلام النبلاء - للإمام الذهبي", "تهذيب الكمال - للحافظ المزي" }
        });

        var geminiResponseJson = JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[]
                        {
                            new { text = biographyJson }
                        }
                    }
                }
            }
        });

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(geminiResponseJson)
            };
            return Task.FromResult(resp);
        });

        var httpClient = new HttpClient(mockHandler);
        var options = Options.Create(new AiOptions { ApiKey = "test-key", Provider = "Gemini" });

        using var db = CreateDbContext("BioTest_AiCall");
        var promptBuilder = new BiographyPromptBuilder();
        var settingsService = new SettingsService(db);

        var service = new PersonBiographyService(
            httpClient,
            options,
            db,
            promptBuilder,
            settingsService,
            NullLogger<PersonBiographyService>.Instance);

        var result = await service.GetPersonBiographyAsync(new GetPersonBiographyRequest
        {
            Name = "نافع مولى ابن عمر",
            ContextDescription = "راوٍ مدني تابعي"
        });

        result.Should().NotBeNull();
        result.Name.Should().Be("نافع مولى ابن عمر");
        result.SiyarBiography.Should().Contain("سير أعلام النبلاء");
        result.Teachers.Should().Contain("عبد الله بن عمر");
        result.Students.Should().Contain("مالك بن أنس");
        result.ScholarlyPraise.Should().HaveCount(2);

        // Verify persisted in database
        var savedPerson = await db.People.FirstOrDefaultAsync(p => p.Name == "نافع مولى ابن عمر");
        savedPerson.Should().NotBeNull();
        savedPerson!.DetailedBiographyJson.Should().NotBeNullOrWhiteSpace();
        savedPerson.DetailedBiographyJson.Should().Contain("سلسلة الذهب");
    }

    [Fact]
    public async Task LessonsController_GetPersonBiography_ReturnsOkResult()
    {
        var mockBioService = new Mock<IPersonBiographyService>();
        var mockPersistence = new Mock<ILessonPersistenceService>();
        var mockChat = new Mock<ILessonChatService>();
        var mockStudentLearning = new Mock<IStudentLearningService>();
        var mockEnv = new Mock<IWebHostEnvironment>();

        var expectedDto = new PersonBiographyDto
        {
            Name = "سعيد بن المسيب",
            Title = "سيد التابعين وفَقِيه الفقهاء",
            SiyarBiography = "ترجم له الذهبي في سير أعلام النبلاء في الطبقة الثانية...",
            Sources = ["سير أعلام النبلاء - للذهبي"]
        };

        mockBioService
            .Setup(s => s.GetPersonBiographyAsync(It.IsAny<GetPersonBiographyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedDto);

        var controller = new LessonsController(
            null!,
            mockPersistence.Object,
            Options.Create(new PdfExtractionOptions()),
            mockEnv.Object,
            mockStudentLearning.Object,
            mockChat.Object,
            mockBioService.Object,
            NullLogger<LessonsController>.Instance);

        var actionResult = await controller.GetPersonBiography("سعيد بن المسيب", "سياق فقهي", null, null, false, CancellationToken.None);
        var okResult = actionResult.Should().BeOfType<OkObjectResult>().Subject;
        var dto = okResult.Value.Should().BeOfType<PersonBiographyDto>().Subject;

        dto.Name.Should().Be("سعيد بن المسيب");
        dto.Title.Should().Contain("سيد التابعين");
    }

    [Fact]
    public async Task GetPersonBiographyAsync_WhenCachedIsPlaceholder_BypassesCacheAndCallsAi()
    {
        var placeholderDto = new PersonBiographyDto
        {
            Name = "عطاء بن أبي رباح",
            Title = "عَلَم من الأعلام",
            Summary = "تابعي جليل ومفتي مكة.",
            SiyarBiography = "ترجمة وسيرة عطاء بن أبي رباح: عَلَم ورجل من رجال الحديث والعلم، ورد ذكره في كتب السنة والآثار، ويُرجع في تفصيل مناقبه ومروياته إلى كتاب سير أعلام النبلاء للإمام الذهبي والمصادر المعتمدة.",
            Sources = ["سير أعلام النبلاء - للإمام الذهبي"]
        };

        using var db = CreateDbContext("BioTest_PlaceholderBypass");
        db.People.Add(new Person
        {
            Id = Guid.NewGuid(),
            Name = "عطاء بن أبي رباح",
            Description = "تابعي جليل",
            DetailedBiographyJson = JsonSerializer.Serialize(placeholderDto),
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var richAiJson = JsonSerializer.Serialize(new
        {
            name = "عطاء بن أبي رباح",
            title = "الإمام الحجة، مفتي الحرم وشيخ الإسلام",
            kunya = "أبو محمد القرشي الفهري",
            era = "الطبقة الثالثة - كبار التابعين",
            deathYear = "114 هـ بمكة المكرمة",
            summary = "إمام الحرم ومفتي مكة وأحد أئمة الفقه والحديث في عصر التابعين.",
            siyarBiography = "ترجم له الإمام الذهبي في سير أعلام النبلاء فقال: الإمام، شيخ الإسلام، مفتي الحرم، أبو محمد القرشي مولاهم المكي. ولد في خلافة عثمان، وجالس كبار الصحابة.",
            teachers = new[] { "ابن عباس", "أبو هريرة", "عائشة", "جابر بن عبد الله" },
            students = new[] { "الزهري", "عمرو بن دينار", "الأوزاعي", "ابن جريج" },
            scholarlyPraise = new[]
            {
                new { scholar = "ابن عباس", quote = "يا أهل مكة، أتجمعون لي المسائل وعندكم عطاء بن أبي رباح؟" }
            },
            virtuesAndNarrations = "كان من أوعية العلم والورع، حج سبعين حجة.",
            sources = new[] { "سير أعلام النبلاء - للإمام الذهبي" }
        });

        var geminiResponse = JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[] { new { text = richAiJson } }
                    }
                }
            }
        });

        var mockHandler = new MockHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(geminiResponse)
            }));

        var httpClient = new HttpClient(mockHandler);
        var options = Options.Create(new AiOptions { ApiKey = "test-key", Provider = "Gemini" });
        var promptBuilder = new BiographyPromptBuilder();
        var settingsService = new SettingsService(db);

        var service = new PersonBiographyService(
            httpClient,
            options,
            db,
            promptBuilder,
            settingsService,
            NullLogger<PersonBiographyService>.Instance);

        var result = await service.GetPersonBiographyAsync(new GetPersonBiographyRequest { Name = "عطاء بن أبي رباح" });

        result.Should().NotBeNull();
        result.Title.Should().Contain("مفتي الحرم");
        result.Teachers.Should().Contain("ابن عباس");
        result.Students.Should().Contain("ابن جريج");
        result.ScholarlyPraise.Should().HaveCount(1);
    }
}
