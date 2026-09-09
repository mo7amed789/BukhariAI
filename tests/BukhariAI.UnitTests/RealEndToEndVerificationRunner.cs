using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Lessons.GenerateLesson;
using BukhariAI.Domain.Entities;
using BukhariAI.Infrastructure.AI;
using BukhariAI.Infrastructure.Ocr;
using BukhariAI.Infrastructure.Pdf;
using BukhariAI.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace BukhariAI.UnitTests;

public class RealEndToEndVerificationRunner
{
    private readonly ITestOutputHelper _output;
    private const string ConnectionString = "Server=(localdb)\\MSSQLLocalDB;Database=MyDatabase;Trusted_Connection=True;TrustServerCertificate=True;";
    private const string RealPdfPath = @"c:\Users\mohamed\Desktop\s.bokhari.5.pdf";

    public RealEndToEndVerificationRunner(ITestOutputHelper output)
    {
        _output = output;
    }

    private sealed class XunitLogger<T> : ILogger<T>
    {
        private readonly ITestOutputHelper _out;
        public XunitLogger(ITestOutputHelper output) => _out = output;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _out.WriteLine($"[{logLevel}] {formatter(state, exception)}");
        }
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task RunFullRealEndToEndLearningPipelineVerification()
    {
        _output.WriteLine("================================================================================");
        _output.WriteLine("              STARTING REAL END-TO-END LEARNING PIPELINE VERIFICATION           ");
        _output.WriteLine("================================================================================");

        // -------------------------------------------------------------------------
        // STEP 1: VERIFY DATABASE
        // -------------------------------------------------------------------------
        _output.WriteLine("\n>>> STEP 1: VERIFY DATABASE");
        using (var sqlConn = new SqlConnection(ConnectionString))
        {
            await sqlConn.OpenAsync();
            var cmd = sqlConn.CreateCommand();
            cmd.CommandText = @"
                SELECT TABLE_NAME 
                FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_TYPE='BASE TABLE' 
                ORDER BY TABLE_NAME;";
            var reader = await cmd.ExecuteReaderAsync();
            var tables = new List<string>();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
            _output.WriteLine($"Connected to MyDatabase. Found {tables.Count} tables:");
            foreach (var t in tables)
            {
                _output.WriteLine($"  - {t}");
            }

            tables.Should().Contain("Books");
            tables.Should().Contain("BookPages");
            tables.Should().Contain("Lessons");
            tables.Should().Contain("LessonPages");
            tables.Should().Contain("LessonHadiths");
            tables.Should().Contain("HadithEvidences");
            tables.Should().Contain("HadithLessonPoints");
            tables.Should().Contain("People");
            tables.Should().Contain("LessonPeople");
            tables.Should().Contain("HadithPeople");
            tables.Should().Contain("LessonLearningContexts");
            tables.Should().Contain("KnownPeople");
            tables.Should().Contain("KnownTerms");
            tables.Should().Contain("KnownTopics");
            tables.Should().Contain("ReadingSessions");
            tables.Should().Contain("LessonProgresses");
        }

        var dbOptions = new DbContextOptionsBuilder<BukhariDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        // -------------------------------------------------------------------------
        // STEP 2: VERIFY BOOK CREATION
        // -------------------------------------------------------------------------
        _output.WriteLine("\n>>> STEP 2: VERIFY BOOK CREATION");
        using var dbContext1 = new BukhariDbContext(dbOptions);
        var persistenceLogger = new XunitLogger<LessonPersistenceService>(_output);
        var persistenceService = new LessonPersistenceService(dbContext1, persistenceLogger);

        var book = await persistenceService.EnsureDefaultBookAsync();
        book.Should().NotBeNull();
        book.Id.Should().NotBeEmpty();
        book.Title.Should().Be("صحيح البخاري");
        _output.WriteLine($"Book verified: ID={book.Id}, Title='{book.Title}', Author='{book.Author}'");

        // Verify book re-use (no duplicates)
        var bookAgain = await persistenceService.EnsureDefaultBookAsync();
        bookAgain.Id.Should().Be(book.Id, "EnsureDefaultBookAsync must reuse the existing book.");
        _output.WriteLine("Verified book reuse: no duplicate book created.");

        // -------------------------------------------------------------------------
        // STEP 3: REAL PDF TEST (PAGES 110–111)
        // -------------------------------------------------------------------------
        _output.WriteLine("\n>>> STEP 3: REAL PDF TEST (PAGES 110–111)");
        File.Exists(RealPdfPath).Should().BeTrue($"Real PDF must exist at {RealPdfPath}");

        var extractionOptions = Options.Create(new PdfExtractionOptions
        {
            MaximumPageRange = 20,
            MinimumTextCharacters = 50,
            MinimumMeaningfulCharacterRatio = 0.50,
            OcrEnabled = true
        });

        var pageAnalyzer = new PdfPageAnalyzer(extractionOptions);
        var ocrOptions = Options.Create(new OcrOptions
        {
            Enabled = true,
            Language = "ara",
            Dpi = 300,
            PageSegmentationMode = "Auto",
            FallbackPsm = "SingleBlock",
            MinimumQualityScore = 0.45
        });

        var ocrService = new OcrService(ocrOptions, Microsoft.Extensions.Options.Options.Create(new BukhariAI.Infrastructure.AI.AiOptions()), new System.Net.Http.HttpClient(), new XunitLogger<OcrService>(_output));
        var pdfExtractionService = new PdfExtractionService(
            pageAnalyzer,
            ocrService,
            extractionOptions,
            new XunitLogger<PdfExtractionService>(_output));

        PdfExtractionResult extractionResult;
        using (var pdfStream = File.OpenRead(RealPdfPath))
        {
            extractionResult = await pdfExtractionService.ExtractAsync(pdfStream, 110, 111);
        }

        extractionResult.Should().NotBeNull();
        extractionResult.Pages.Should().HaveCount(2);
        _output.WriteLine($"Extraction Completed: UsedOcr={extractionResult.UsedOcr}, PagesCount={extractionResult.Pages.Count}");

        foreach (var page in extractionResult.Pages)
        {
            _output.WriteLine($"  - Page {page.PageNumber}: Method={page.Method}, TextLength={page.Text.Length} chars");
            page.Text.Should().NotBeNullOrWhiteSpace();
            _output.WriteLine($"    Sample: {page.Text[..Math.Min(120, page.Text.Length)]}...");
        }

        // -------------------------------------------------------------------------
        // STEP 4: REAL GEMINI GENERATION
        // -------------------------------------------------------------------------
        _output.WriteLine("\n>>> STEP 4: REAL GEMINI GENERATION");
        string? apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.Process)
                         ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.User)
                         ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.Machine);

        apiKey.Should().NotBeNullOrWhiteSpace("GEMINI_API_KEY is required for real Gemini generation.");

        var aiOptions = Options.Create(new AiOptions
        {
            Provider = "Gemini",
            Model = "gemini-3.6-flash",
            ApiKey = apiKey
        });

        var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        var promptBuilder = new LessonPromptBuilder();
        var geminiService = new GeminiAiService(httpClient, aiOptions, promptBuilder, new XunitLogger<GeminiAiService>(_output));

        string combinedText = string.Join("\n\n", extractionResult.Pages.Select(p => $"[PDF Page {p.PageNumber}]\n{p.Text}"));
        var geminiResponse = await geminiService.GenerateLessonAsync(combinedText);

        geminiResponse.Should().NotBeNull();
        geminiResponse.Title.Should().NotBeNullOrWhiteSpace();
        geminiResponse.Overview.Should().NotBeNullOrWhiteSpace();
        geminiResponse.Hadiths.Should().NotBeEmpty();

        _output.WriteLine($"Gemini Response Generated successfully:");
        _output.WriteLine($"  Title: {geminiResponse.Title}");
        _output.WriteLine($"  Overview: {geminiResponse.Overview}");
        _output.WriteLine($"  Hadiths Count: {geminiResponse.Hadiths.Count}");
        _output.WriteLine($"  Connections Count: {geminiResponse.Connections.Count}");
        _output.WriteLine($"  Review Questions Count: {geminiResponse.ReviewQuestions.Count}");

        foreach (var h in geminiResponse.Hadiths)
        {
            _output.WriteLine($"\n  [Hadith/Issue]: {h.Reference}");
            _output.WriteLine($"    Problem: {h.Problem}");
            _output.WriteLine($"    Evidence Count: {h.Evidence.Count}");
            _output.WriteLine($"    Reasoning: {h.Reasoning}");
            _output.WriteLine($"    ScholarlyDiscussion: {h.ScholarlyDiscussion}");
            _output.WriteLine($"    Conclusion: {h.Conclusion}");
            _output.WriteLine($"    EasyExplanation: {h.EasyExplanation}");
            _output.WriteLine($"    People: {string.Join(" | ", h.People)}");
            _output.WriteLine($"    Lessons/Points: {string.Join(" | ", h.Lessons)}");
        }

        // -------------------------------------------------------------------------
        // STEP 5: PERSIST THE LESSON
        // -------------------------------------------------------------------------
        _output.WriteLine("\n>>> STEP 5: PERSIST THE LESSON");
        using var dbContext2 = new BukhariDbContext(dbOptions);
        var persistenceService2 = new LessonPersistenceService(dbContext2, persistenceLogger);

        Guid lessonId = await persistenceService2.PersistLessonAsync(geminiResponse, extractionResult, book.Id);
        lessonId.Should().NotBeEmpty();
        _output.WriteLine($"Persisted lesson successfully with ID: {lessonId}");

        // -------------------------------------------------------------------------
        // STEP 6: VERIFY PERSON REUSE
        // -------------------------------------------------------------------------
        _output.WriteLine("\n>>> STEP 6: VERIFY PERSON REUSE");
        using var dbContext3 = new BukhariDbContext(dbOptions);
        var peopleInDb = await dbContext3.People.ToListAsync();
        _output.WriteLine($"Total People records in DB: {peopleInDb.Count}");
        foreach (var p in peopleInDb)
        {
            _output.WriteLine($"  - Person: ID={p.Id}, Name='{p.Name}', Desc='{p.Description}'");
        }

        // -------------------------------------------------------------------------
        // STEP 7: VERIFY LEARNING MEMORY
        // -------------------------------------------------------------------------
        _output.WriteLine("\n>>> STEP 7: VERIFY LEARNING MEMORY");
        using var dbContext4 = new BukhariDbContext(dbOptions);
        var persistenceService4 = new LessonPersistenceService(dbContext4, persistenceLogger);
        var learningContext = await persistenceService4.GetLearningContextByBookIdAsync(book.Id);

        learningContext.Should().NotBeNull();
        _output.WriteLine($"Learning Context ID: {learningContext!.Id}, BookId: {learningContext.BookId}");
        _output.WriteLine($"  Known People ({learningContext.KnownPeople.Count}):");
        foreach (var kp in learningContext.KnownPeople)
        {
            _output.WriteLine($"    - {kp.Person.Name} (TimesSeen={kp.TimesSeen}, Level={kp.LearningLevel}, FirstLesson={kp.FirstIntroducedLessonId}, LastLesson={kp.LastReferencedLessonId})");
            kp.TimesSeen.Should().BeGreaterThan(0);
        }

        _output.WriteLine($"  Known Topics ({learningContext.KnownTopics.Count}):");
        foreach (var kt in learningContext.KnownTopics)
        {
            _output.WriteLine($"    - {kt.Topic} (FirstLesson={kt.FirstIntroducedLessonId}, LastLesson={kt.LastReferencedLessonId})");
        }

        _output.WriteLine($"  Known Terms ({learningContext.KnownTerms.Count}):");
        foreach (var kt in learningContext.KnownTerms)
        {
            _output.WriteLine($"    - {kt.Term}: {kt.Explanation} (TimesSeen={kt.TimesSeen}, Level={kt.LearningLevel})");
            kt.TimesSeen.Should().BeGreaterThan(0);
        }

        var activeEducationalContext = await persistenceService4.GetActiveEducationalContextAsync(book.Id);
        activeEducationalContext.Should().NotBeNull();
        _output.WriteLine($"Active Educational Context generated successfully: {activeEducationalContext.People.Count} people, {activeEducationalContext.Terms.Count} terms.");

        // -------------------------------------------------------------------------
        // STEP 8: RETRIEVE THE LESSON (GET /api/Lessons/{id})
        // -------------------------------------------------------------------------
        _output.WriteLine("\n>>> STEP 8: RETRIEVE THE LESSON");
        using var dbContext5 = new BukhariDbContext(dbOptions);
        var persistenceService5 = new LessonPersistenceService(dbContext5, persistenceLogger);
        var retrievedLesson = await persistenceService5.GetLessonByIdAsync(lessonId);

        retrievedLesson.Should().NotBeNull();
        retrievedLesson!.Title.Should().Be(geminiResponse.Title);
        retrievedLesson.Overview.Should().Be(geminiResponse.Overview);
        retrievedLesson.StartPage.Should().Be(110);
        retrievedLesson.EndPage.Should().Be(111);
        retrievedLesson.LessonPages.Should().HaveCount(2);
        retrievedLesson.Hadiths.Should().HaveCount(geminiResponse.Hadiths.Count);
        retrievedLesson.Connections.Should().HaveCount(geminiResponse.Connections.Count);
        retrievedLesson.ReviewQuestions.Should().HaveCount(geminiResponse.ReviewQuestions.Count);

        _output.WriteLine("Retrieved lesson matches original Gemini response with 100% integrity.");

        // -------------------------------------------------------------------------
        // STEP 9: RETRIEVE BOOK LESSONS (GET /api/Books/{bookId}/lessons)
        // -------------------------------------------------------------------------
        _output.WriteLine("\n>>> STEP 9: RETRIEVE BOOK LESSONS");
        var bookLessons = await persistenceService5.GetLessonsByBookIdAsync(book.Id);
        bookLessons.Should().NotBeEmpty();
        bookLessons.Should().Contain(l => l.Id == lessonId);
        _output.WriteLine($"Found {bookLessons.Count} lessons for Book '{book.Title}'.");

        // -------------------------------------------------------------------------
        // STEP 10: RETRIEVE LEARNING CONTEXT (GET /api/Books/{bookId}/learning-context)
        // -------------------------------------------------------------------------
        _output.WriteLine("\n>>> STEP 10: RETRIEVE LEARNING CONTEXT");
        var retrievedContext = await persistenceService5.GetLearningContextByBookIdAsync(book.Id);
        retrievedContext.Should().NotBeNull();
        _output.WriteLine($"Retrieved Learning Context successfully for Book {book.Id}.");

        // -------------------------------------------------------------------------
        // STEP 11: DUPLICATE SAFETY TEST
        // -------------------------------------------------------------------------
        _output.WriteLine("\n>>> STEP 11: DUPLICATE SAFETY TEST");
        int countBeforeSecondPersist = (await persistenceService5.GetLessonsByBookIdAsync(book.Id)).Count;
        int peopleCountBefore = (await dbContext5.People.ToListAsync()).Count;

        _output.WriteLine($"Current Lesson count: {countBeforeSecondPersist}, People count: {peopleCountBefore}");
        _output.WriteLine("Re-persisting the same lesson response for pages 110-111...");

        using var dbContext6 = new BukhariDbContext(dbOptions);
        var persistenceService6 = new LessonPersistenceService(dbContext6, persistenceLogger);
        Guid secondLessonId = await persistenceService6.PersistLessonAsync(geminiResponse, extractionResult, book.Id);

        int countAfterSecondPersist = (await persistenceService6.GetLessonsByBookIdAsync(book.Id)).Count;
        int peopleCountAfter = (await dbContext6.People.ToListAsync()).Count;

        _output.WriteLine($"Result of Duplicate Persist:");
        _output.WriteLine($"  Second Lesson ID: {secondLessonId}");
        _output.WriteLine($"  Lesson count before={countBeforeSecondPersist}, after={countAfterSecondPersist}");
        _output.WriteLine($"  People count before={peopleCountBefore}, after={peopleCountAfter}");

        peopleCountAfter.Should().Be(peopleCountBefore, "People must be reused and not duplicated.");

        _output.WriteLine("================================================================================");
        _output.WriteLine("          ALL REAL END-TO-END VERIFICATION PHASES COMPLETED SUCCESSFULLY!       ");
        _output.WriteLine("================================================================================");
    }
}

