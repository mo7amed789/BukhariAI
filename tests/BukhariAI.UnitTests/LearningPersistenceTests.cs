using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Lessons.GenerateLesson;
using BukhariAI.Domain.Entities;
using BukhariAI.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace BukhariAI.UnitTests;

public class LearningPersistenceTests
{
    private readonly ITestOutputHelper _output;

    public LearningPersistenceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private ILogger<LessonPersistenceService> CreateLogger() => new TestOutputLogger<LessonPersistenceService>(_output);

    private static BukhariDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<BukhariDbContext>()
            .UseInMemoryDatabase(databaseName: $"BukhariAI_Learning_{Guid.NewGuid()}")
            .EnableSensitiveDataLogging()
            .Options;

        return new BukhariDbContext(options);
    }

    private sealed class TestOutputLogger<T> : ILogger<T>
    {
        private readonly ITestOutputHelper _output;
        public TestOutputLogger(ITestOutputHelper output) => _output = output;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _output.WriteLine($"[{logLevel}] {formatter(state, exception)}");
        }
    }

    private static (GenerateLessonResponse response, PdfExtractionResult extractionResult) CreateSampleLessonData(
        string title = "حكم الانتفاع بأجزاء الميتة وتطهير الجلد بالدباغ")
    {
        var extractionResult = new PdfExtractionResult
        {
            Pages =
            [
                new ExtractedPage
                {
                    PageNumber = 110,
                    Text = "في هذا الحديث دليل على أن قوله تعالى: حرمت عليكم الميتة ليس عاما في جميع وجوه الانتفاع...",
                    Method = ExtractionMethod.Ocr
                },
                new ExtractedPage
                {
                    PageNumber = 111,
                    Text = "كثير من الخفاف الآن مأخوذة من جلود ما لا يحل أكله لكنه مدبوغ...",
                    Method = ExtractionMethod.Ocr
                }
            ],
            UsedOcr = true
        };

        var response = new GenerateLessonResponse
        {
            Title = title,
            Overview = "يتناول هذا الدرس شرح مسائل الانتفاع بشحوم الميتة وجلودها وحكم بيعها وتطهيرها بالدباغ.",
            HistoricalContext = "لا يوجد سياق تاريخي إضافي موثق في النص المقدم.",
            SourcePages = [110, 111],
            Hadiths =
            [
                new HadithExplanation
                {
                    Reference = "مسألة طلاء السفن بشحوم الميتة",
                    SourcePages = [110],
                    Summary = "مناقشة تخصيص تحريم الميتة بالأكل وإباحة سائر الانتفاعات الخدمية.",
                    Problem = "هل تحريم الميتة يعم جميع وجوه الانتفاع أم هو مقصور على الأكل والبيع؟",
                    Evidence =
                    [
                        new Evidence
                        {
                            Text = "﴿حُرِّمَتْ عَلَيْكُمُ الْمَيْتَةُ﴾ [المائدة: 3]",
                            Role = "نص قرآني ظاهر العموم",
                            SourcePages = [110]
                        },
                        new Evidence
                        {
                            Text = "إنما حرم أكلها",
                            Role = "دليل مخصص دال على الحصر",
                            SourcePages = [110]
                        }
                    ],
                    Reasoning = "دلالة لفظة الحصر (إنما) على قصر التحريم على الأكل.",
                    ScholarlyDiscussion = "خلاف في مرجع الضمير في قوله: «لا، هو حرام».",
                    Conclusion = "جواز الانتفاع بشحوم الميتة في غير الأكل كطلاء السفن.",
                    EasyExplanation = "شرح ميسر: التحريم خاص بالأكل، ويجوز استعمال الشحم في طلاء السفن ودباغة الجلد بـ القرظ (شجر يستخدم ورقه في دباغة الجلود وتطهيرها).",
                    People =
                    [
                        "النبي محمد ﷺ — رسول الله ومبين الأحكام الشرعية",
                        "الصحابة رضي الله عنهم — السائلون عن حكم الانتفاع بالشحوم"
                    ],
                    Lessons =
                    [
                        "ألفاظ الحصر تخصص العمومات",
                        "مراعاة سياق الحديث عند تفسير الضمائر"
                    ],
                    Connections = ["ربط تخصيص التحريم بمسألة الدباغ"]
                }
            ],
            Connections = ["الربط بين تخصيص عموم آية الميتة وأحاديث طهارة الجلود بالدباغ"],
            ReviewQuestions =
            [
                "كيف استدل المؤلف بحديث «إنما حرم أكلها»؟",
                "ما القولان في مرجع الضمير في قوله ﷺ «لا، هو حرام»؟"
            ],
            Metadata = new LessonMetadata
            {
                StartPage = 110,
                EndPage = 111,
                TotalPagesProcessed = 2,
                UsedOcr = true
            }
        };

        return (response, extractionResult);
    }

    [Fact]
    public async Task EnsureDefaultBookAsync_ShouldCreateSahihAlBukhariWithLearningContext()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var service = new LessonPersistenceService(context, NullLogger<LessonPersistenceService>.Instance);

        // Act
        var book = await service.EnsureDefaultBookAsync();

        // Assert
        book.Should().NotBeNull();
        book.Title.Should().Be("صحيح البخاري");
        book.Author.Should().Contain("البخاري");

        var booksInDb = await context.Books.Include(b => b.LearningContexts).ToListAsync();
        booksInDb.Should().HaveCount(1);
        booksInDb.First().LearningContexts.Should().HaveCount(1);
    }

    [Fact]
    public async Task PersistLessonAsync_ShouldPersistBookPagesAndTraceability()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var service = new LessonPersistenceService(context, CreateLogger());
        var (response, extractionResult) = CreateSampleLessonData();

        // Act
        Guid lessonId = await service.PersistLessonAsync(response, extractionResult);

        // Assert
        var pages = await context.BookPages.ToListAsync();
        pages.Should().HaveCount(2);
        pages.Should().Contain(p => p.PageNumber == 110 && p.ExtractedText.Contains("حرمت عليكم الميتة") && p.UsedOcr);
        pages.Should().Contain(p => p.PageNumber == 111 && p.ExtractedText.Contains("الخفاف") && p.UsedOcr);
    }

    [Fact]
    public async Task PersistLessonAsync_ShouldPersistLessonAndLessonPagesRelationships()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var service = new LessonPersistenceService(context, NullLogger<LessonPersistenceService>.Instance);
        var (response, extractionResult) = CreateSampleLessonData();

        // Act
        Guid lessonId = await service.PersistLessonAsync(response, extractionResult);

        // Assert
        var lesson = await service.GetLessonByIdAsync(lessonId);
        lesson.Should().NotBeNull();
        lesson!.Title.Should().Be(response.Title);
        lesson.Book.Should().NotBeNull();
        lesson.Book.Title.Should().Be("صحيح البخاري");
        lesson.StartPage.Should().Be(110);
        lesson.EndPage.Should().Be(111);

        lesson.LessonPages.Should().HaveCount(2);
        lesson.LessonPages.Should().Contain(lp => lp.PageNumber == 110 && lp.BookPage.ExtractedText.Contains("الميتة"));
    }

    [Fact]
    public async Task PersistLessonAsync_ShouldPersistLessonHadithsWithEvidencesAndLessonPoints()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var service = new LessonPersistenceService(context, NullLogger<LessonPersistenceService>.Instance);
        var (response, extractionResult) = CreateSampleLessonData();

        // Act
        Guid lessonId = await service.PersistLessonAsync(response, extractionResult);

        // Assert
        var lesson = await service.GetLessonByIdAsync(lessonId);
        lesson!.Hadiths.Should().HaveCount(1);

        var hadith = lesson.Hadiths.First();
        hadith.Reference.Should().Be("مسألة طلاء السفن بشحوم الميتة");
        hadith.Problem.Should().Contain("هل تحريم الميتة يعم");
        hadith.Reasoning.Should().Contain("دلالة لفظة الحصر");
        hadith.ScholarlyDiscussion.Should().Contain("مرجع الضمير");
        hadith.Conclusion.Should().Contain("جواز الانتفاع بشحوم الميتة");
        hadith.EasyExplanation.Should().Contain("شرح ميسر");

        hadith.Evidences.Should().HaveCount(2);
        hadith.Evidences.Should().Contain(e => e.Text.Contains("إنما حرم أكلها") && e.Role.Contains("دليل مخصص"));

        hadith.LessonPoints.Should().HaveCount(2);
        hadith.LessonPoints.Should().Contain(lp => lp.Point.Contains("ألفاظ الحصر تخصص العمومات"));
    }

    [Fact]
    public async Task PersistLessonAsync_WhenSamePersonAppearsInMultipleLessons_ShouldReuseExistingPerson()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var service = new LessonPersistenceService(context, NullLogger<LessonPersistenceService>.Instance);

        var (response1, extraction1) = CreateSampleLessonData("درس أول");
        var (response2, extraction2) = CreateSampleLessonData("درس ثانٍ لنفس الشخصية");

        // Act - Persist first lesson
        Guid lesson1Id = await service.PersistLessonAsync(response1, extraction1);
        int personCountAfterLesson1 = await context.People.CountAsync();

        // Act - Persist second lesson
        Guid lesson2Id = await service.PersistLessonAsync(response2, extraction2);
        int personCountAfterLesson2 = await context.People.CountAsync();

        // Assert - People table must not duplicate
        personCountAfterLesson2.Should().Be(personCountAfterLesson1, "Existing person entity must be reused.");

        var lesson1 = await service.GetLessonByIdAsync(lesson1Id);
        var lesson2 = await service.GetLessonByIdAsync(lesson2Id);

        var prophetInLesson1 = lesson1!.LessonPeople.First(lp => lp.Person.Name.Contains("النبي محمد"));
        var prophetInLesson2 = lesson2!.LessonPeople.First(lp => lp.Person.Name.Contains("النبي محمد"));

        prophetInLesson1.PersonId.Should().Be(prophetInLesson2.PersonId);
    }

    [Fact]
    public async Task PersistLessonAsync_ShouldUpdateLearningContextWithKnownPeopleKnownTermsAndKnownTopics()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var service = new LessonPersistenceService(context, NullLogger<LessonPersistenceService>.Instance);

        var (response1, extraction1) = CreateSampleLessonData("درس الانتفاع بالدباغ");
        var (response2, extraction2) = CreateSampleLessonData("درس آخر في نفس الكتاب");

        // Act - Persist two lessons
        Guid lesson1Id = await service.PersistLessonAsync(response1, extraction1);
        Guid lesson2Id = await service.PersistLessonAsync(response2, extraction2);

        // Assert
        var book = await service.EnsureDefaultBookAsync();
        var learningContext = await service.GetLearningContextByBookIdAsync(book.Id);

        learningContext.Should().NotBeNull();
        learningContext!.KnownPeople.Should().NotBeEmpty();
        learningContext.KnownPeople.Should().Contain(kp => kp.Person.Name.Contains("النبي محمد"));

        learningContext.KnownTopics.Should().HaveCount(2);
        learningContext.KnownTopics.Should().Contain(kt => kt.Topic == "درس الانتفاع بالدباغ" && kt.FirstIntroducedLessonId == lesson1Id);
        learningContext.KnownTopics.Should().Contain(kt => kt.Topic == "درس آخر في نفس الكتاب" && kt.FirstIntroducedLessonId == lesson2Id);

        learningContext.KnownTerms.Should().NotBeEmpty();
        learningContext.KnownTerms.Should().Contain(kt => kt.Term.Contains("القرظ"));
    }

    [Fact]
    public async Task PersistLessonAsync_ShouldRecordLessonProgressAndReadingSession()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var service = new LessonPersistenceService(context, NullLogger<LessonPersistenceService>.Instance);
        var (response, extractionResult) = CreateSampleLessonData();

        // Act
        Guid lessonId = await service.PersistLessonAsync(response, extractionResult);

        // Assert
        var progressList = await context.LessonProgresses.ToListAsync();
        progressList.Should().HaveCount(1);
        progressList.First().LessonId.Should().Be(lessonId);
        progressList.First().Status.Should().Be(LessonProgressStatus.NotStarted,
            "generating a lesson is not evidence that the student completed it");

        var sessions = await context.ReadingSessions.ToListAsync();
        sessions.Should().HaveCount(1);
        sessions.First().StartPage.Should().Be(110);
        sessions.First().EndPage.Should().Be(111);
        sessions.First().LessonId.Should().Be(lessonId);
        sessions.First().Status.Should().Be(ReadingSessionStatus.NotStarted);
    }

    [Fact]
    public async Task GetLessonsByBookIdAsync_ShouldReturnRecentLessonsFirst()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var service = new LessonPersistenceService(context, NullLogger<LessonPersistenceService>.Instance);
        var book = await service.EnsureDefaultBookAsync();

        var (response1, extraction1) = CreateSampleLessonData("درس الصفحة 110-111");
        var (response2, extraction2) = CreateSampleLessonData("درس الصفحة 112-113");

        // Act
        await service.PersistLessonAsync(response1, extraction1, book.Id);
        await service.PersistLessonAsync(response2, extraction2, book.Id);

        var lessons = await service.GetLessonsByBookIdAsync(book.Id);

        // Assert
        lessons.Should().HaveCount(2);
        lessons.Should().BeInDescendingOrder(l => l.CreatedAtUtc);
    }

    [Fact]
    public async Task GetBooksAsync_ShouldReturnRegisteredBooksWithLessons()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var service = new LessonPersistenceService(context, NullLogger<LessonPersistenceService>.Instance);
        var (response, extraction) = CreateSampleLessonData();

        // Act
        await service.PersistLessonAsync(response, extraction);
        var books = await service.GetBooksAsync();

        // Assert
        books.Should().NotBeEmpty();
        books.First().Title.Should().Be("صحيح البخاري");
        books.First().Lessons.Should().HaveCount(1);
    }

    [Fact]
    public async Task ActiveMemory_FirstLesson_ShouldCreateKnownPersonAndTermWithIntroducedLevel()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var service = new LessonPersistenceService(context, NullLogger<LessonPersistenceService>.Instance);
        var (response, extraction) = CreateSampleLessonData();

        // Act
        Guid lessonId = await service.PersistLessonAsync(response, extraction);

        // Assert
        var book = await service.EnsureDefaultBookAsync();
        var learningContext = await service.GetLearningContextByBookIdAsync(book.Id);

        learningContext.Should().NotBeNull();
        learningContext!.KnownPeople.Should().NotBeEmpty();
        var knownPerson = learningContext.KnownPeople.First(kp => kp.Person.Name.Contains("النبي محمد"));
        knownPerson.TimesSeen.Should().Be(1);
        knownPerson.LearningLevel.Should().Be(LearningLevel.Introduced);
        knownPerson.FirstIntroducedLessonId.Should().Be(lessonId);
        knownPerson.LastReferencedLessonId.Should().Be(lessonId);

        learningContext.KnownTerms.Should().NotBeEmpty();
        var knownTerm = learningContext.KnownTerms.First(kt => kt.Term.Contains("القرظ"));
        knownTerm.TimesSeen.Should().Be(1);
        knownTerm.LearningLevel.Should().Be(LearningLevel.Introduced);
        knownTerm.FirstIntroducedLessonId.Should().Be(lessonId);
        knownTerm.LastReferencedLessonId.Should().Be(lessonId);
    }

    [Fact]
    public async Task ActiveMemory_SecondLessonWithSameTermAndPerson_ShouldIncrementTimesSeenAndAdvanceLevel()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var service = new LessonPersistenceService(context, NullLogger<LessonPersistenceService>.Instance);

        var (response1, extraction1) = CreateSampleLessonData("الدرس الأول في الدباغة");
        var (response2, extraction2) = CreateSampleLessonData("الدرس الثاني في الدباغة");

        // Act
        Guid lesson1Id = await service.PersistLessonAsync(response1, extraction1);
        Guid lesson2Id = await service.PersistLessonAsync(response2, extraction2);

        // Assert
        var book = await service.EnsureDefaultBookAsync();
        var learningContext = await service.GetLearningContextByBookIdAsync(book.Id);

        learningContext.Should().NotBeNull();

        // Check Person
        var knownPerson = learningContext!.KnownPeople.First(kp => kp.Person.Name.Contains("النبي محمد"));
        knownPerson.TimesSeen.Should().Be(2);
        knownPerson.LearningLevel.Should().Be(LearningLevel.Familiar);
        knownPerson.FirstIntroducedLessonId.Should().Be(lesson1Id, "FirstIntroducedLessonId must not change");
        knownPerson.LastReferencedLessonId.Should().Be(lesson2Id, "LastReferencedLessonId must update to latest lesson");

        // Check Term
        var knownTerm = learningContext.KnownTerms.First(kt => kt.Term.Contains("القرظ"));
        knownTerm.TimesSeen.Should().Be(2);
        knownTerm.LearningLevel.Should().Be(LearningLevel.Familiar);
        knownTerm.FirstIntroducedLessonId.Should().Be(lesson1Id, "FirstIntroducedLessonId must not change");
        knownTerm.LastReferencedLessonId.Should().Be(lesson2Id, "LastReferencedLessonId must update to latest lesson");
    }

    [Fact]
    public async Task ActiveMemory_GetActiveEducationalContextAsync_ShouldBuildFormattedContextForPrompt()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var service = new LessonPersistenceService(context, NullLogger<LessonPersistenceService>.Instance);
        var (response, extraction) = CreateSampleLessonData();

        // Act
        await service.PersistLessonAsync(response, extraction);
        var educationalContext = await service.GetActiveEducationalContextAsync();

        // Assert
        educationalContext.Should().NotBeNull();
        educationalContext.People.Should().NotBeEmpty();
        educationalContext.People.Should().Contain(p => p.Contains("النبي محمد"));
        educationalContext.Terms.Should().NotBeEmpty();
        educationalContext.Terms.Should().Contain(t => t.Contains("القرظ"));
        educationalContext.Topics.Should().Contain("حكم الانتفاع بأجزاء الميتة وتطهير الجلد بالدباغ");
        educationalContext.KeyIdeas.Should().NotBeEmpty();
        educationalContext.PreviousDiscussions.Should().NotBeEmpty();
    }

    [Fact]
    public void ActiveMemory_PromptBuilder_ShouldIncludeActiveEducationalMemorySections()
    {
        // Arrange
        var promptBuilder = new LessonPromptBuilder();
        var context = new BukhariAI.Application.Lessons.GenerateLesson.LessonLearningContext
        {
            People = ["عائشة رضي الله عنها — أم المؤمنين وزوج النبي ﷺ"],
            Terms = ["الدباغ — معالجة جلد الميتة بالماء ومواد التطهير"],
            Topics = ["حكم الانتفاع بجلود الميتة"],
            KeyIdeas = ["ألفاظ الحصر تخصص العمومات"],
            PreviousDiscussions = ["هل تحريم الميتة يعم جميع وجوه الانتفاع؟ → النتيجة: خاص بالأكل"],
            EstablishedConnections = ["ربط تخصيص التحريم بمسألة الدباغ"]
        };

        // Act
        string userPrompt = promptBuilder.BuildUserPrompt("نص تجريبي مستخرج من الصفحة 112", context);

        // Assert
        userPrompt.Should().Contain("## ذاكرة الدروس السابقة (Active Lesson Learning Memory)");
        userPrompt.Should().Contain("عائشة رضي الله عنها");
        userPrompt.Should().Contain("الدباغ");
        userPrompt.Should().Contain("حكم الانتفاع بجلود الميتة");
        userPrompt.Should().Contain("ألفاظ الحصر تخصص العمومات");
    }
}
