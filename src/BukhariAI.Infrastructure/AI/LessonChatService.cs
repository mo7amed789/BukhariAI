using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Lessons.Chat;
using BukhariAI.Domain.Entities;
using BukhariAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BukhariAI.Infrastructure.AI;

public sealed class LessonChatService : ILessonChatService
{
    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;
    private readonly BukhariDbContext _dbContext;
    private readonly ILessonPersistenceService _lessonService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<LessonChatService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public LessonChatService(
        HttpClient httpClient,
        IOptions<AiOptions> options,
        BukhariDbContext dbContext,
        ILessonPersistenceService lessonService,
        ISettingsService settingsService,
        ILogger<LessonChatService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _dbContext = dbContext;
        _lessonService = lessonService;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<LessonChatResponse> AskLessonQuestionAsync(
        LessonChatRequest request,
        Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            throw new ArgumentException("نص السؤال أو الاستفسار مطلوب.", nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var targetUserId = userId ?? User.DefaultUserId;

        // 1. Resolve or Create Chat Session
        ChatSession? session = null;
        if (request.SessionId.HasValue && request.SessionId.Value != Guid.Empty)
        {
            session = await _dbContext.ChatSessions
                .Include(s => s.Messages)
                .FirstOrDefaultAsync(s => s.Id == request.SessionId.Value && s.UserId == targetUserId, cancellationToken);
        }

        Lesson? lesson = null;
        Guid? targetLessonId = request.LessonId ?? session?.LessonId;
        if (targetLessonId.HasValue && targetLessonId.Value != Guid.Empty)
        {
            lesson = await _lessonService.GetLessonByIdAsync(targetLessonId.Value, cancellationToken);
        }

        Guid? targetBookId = request.BookId ?? session?.BookId;
        if (!targetBookId.HasValue && lesson != null)
        {
            var bookLesson = await _dbContext.Lessons.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lesson.Id, cancellationToken);
            targetBookId = bookLesson?.BookId;
        }

        if (session == null)
        {
            string sessionTitle = !string.IsNullOrWhiteSpace(lesson?.Title)
                ? $"مدارسة: {lesson.Title}"
                : (request.Message.Length > 40 ? request.Message[..40] + "..." : request.Message);

            session = new ChatSession
            {
                Title = sessionTitle,
                UserId = targetUserId,
                BookId = targetBookId,
                LessonId = targetLessonId,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _dbContext.ChatSessions.Add(session);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // 2. Build Specialized Multi-Layer Context
        var (systemPrompt, contextSummary, bookTitle, lessonTitle) = await BuildSpecializedContextAsync(lesson, targetBookId, cancellationToken);
        string userPrompt = BuildUserPrompt(request, lesson);

        // 3. Prepare History for AI Prompt
        var historyPayload = new List<ChatMessageDto>();
        if (session.Messages != null && session.Messages.Count > 0)
        {
            foreach (var m in session.Messages.OrderBy(x => x.CreatedAtUtc).TakeLast(8))
            {
                historyPayload.Add(new ChatMessageDto
                {
                    Role = m.Role,
                    Content = m.Content
                });
            }
        }
        else if (request.History != null && request.History.Count > 0)
        {
            historyPayload.AddRange(request.History);
        }

        string? rawJson = null;

        // 4. Try Primary AI Provider
        try
        {
            rawJson = await CallPrimaryAiAsync(systemPrompt, userPrompt, historyPayload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Primary AI provider failed for lesson chat inquiry. Attempting fallback.");
        }

        // 5. Try Fallback Provider (Conduit)
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            rawJson = await CallFallbackAiAsync(systemPrompt, userPrompt, historyPayload, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(rawJson))
        {
            throw new InvalidOperationException("تعذر الحصول على استجابة من نموذج الذكاء الاصطناعي للإجابة على استفسارك.");
        }

        // 6. Clean and parse JSON response
        string cleanedJson = CleanJsonFences(rawJson);
        LessonChatResponse parsed;
        try
        {
            var deserialized = JsonSerializer.Deserialize<LessonChatResponse>(cleanedJson, JsonOptions);
            if (deserialized != null && !string.IsNullOrWhiteSpace(deserialized.Reply))
            {
                parsed = deserialized;
            }
            else
            {
                parsed = new LessonChatResponse { Reply = rawJson.Trim() };
            }
        }
        catch (JsonException)
        {
            parsed = new LessonChatResponse
            {
                Reply = rawJson.Trim(),
                SuggestedQuestions = GenerateDefaultSuggestions(lesson)
            };
        }

        if (parsed.SuggestedQuestions == null || parsed.SuggestedQuestions.Count == 0)
        {
            parsed.SuggestedQuestions = GenerateDefaultSuggestions(lesson);
        }
        parsed.SourcesCited ??= [];

        // 7. Persist Message History in Database
        var userMsg = new ChatMessage
        {
            SessionId = session.Id,
            Role = "user",
            Content = request.Message.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };
        _dbContext.ChatMessages.Add(userMsg);

        var tutorMsg = new ChatMessage
        {
            SessionId = session.Id,
            Role = "assistant",
            Content = parsed.Reply.Trim(),
            SuggestedQuestionsJson = parsed.SuggestedQuestions.Count > 0 ? JsonSerializer.Serialize(parsed.SuggestedQuestions) : null,
            SourcesCitedJson = parsed.SourcesCited.Count > 0 ? JsonSerializer.Serialize(parsed.SourcesCited) : null,
            CreatedAtUtc = DateTime.UtcNow
        };
        _dbContext.ChatMessages.Add(tutorMsg);

        session.UpdatedAtUtc = DateTime.UtcNow;
        session.ContextSummary = contextSummary;
        if (session.Title == "محادثة جديدة" && !string.IsNullOrWhiteSpace(request.Message))
        {
            session.Title = request.Message.Length > 50 ? request.Message[..50] + "..." : request.Message;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        parsed.SessionId = session.Id;
        parsed.LessonTitle = lessonTitle ?? lesson?.Title;
        parsed.BookTitle = bookTitle;
        parsed.ContextSummary = contextSummary;

        return parsed;
    }

    public async Task<List<ChatSessionDto>> GetChatSessionsAsync(
        Guid? bookId = null,
        Guid? lessonId = null,
        Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        var targetUserId = userId ?? User.DefaultUserId;
        var query = _dbContext.ChatSessions
            .AsNoTracking()
            .Where(s => s.UserId == targetUserId)
            .Include(s => s.Book)
            .Include(s => s.Lesson)
            .Include(s => s.Messages)
            .AsQueryable();

        if (lessonId.HasValue && lessonId.Value != Guid.Empty)
        {
            query = query.Where(s => s.LessonId == lessonId.Value);
        }
        else if (bookId.HasValue && bookId.Value != Guid.Empty)
        {
            query = query.Where(s => s.BookId == bookId.Value);
        }

        var list = await query
            .OrderByDescending(s => s.UpdatedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        return list.Select(s => new ChatSessionDto
        {
            Id = s.Id,
            Title = s.Title,
            BookId = s.BookId,
            BookTitle = s.Book?.Title,
            LessonId = s.LessonId,
            LessonTitle = s.Lesson?.Title,
            MessageCount = s.Messages.Count,
            LastMessagePreview = s.Messages.OrderByDescending(m => m.CreatedAtUtc).Select(m => m.Content).FirstOrDefault(),
            ContextSummary = s.ContextSummary,
            CreatedAtUtc = s.CreatedAtUtc,
            UpdatedAtUtc = s.UpdatedAtUtc
        }).ToList();
    }

    public async Task<ChatSessionDetailDto?> GetChatSessionByIdAsync(
        Guid sessionId,
        Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        var targetUserId = userId ?? User.DefaultUserId;
        var session = await _dbContext.ChatSessions
            .AsNoTracking()
            .Include(s => s.Book)
            .Include(s => s.Lesson)
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == targetUserId, cancellationToken);

        if (session == null) return null;

        return new ChatSessionDetailDto
        {
            Id = session.Id,
            Title = session.Title,
            BookId = session.BookId,
            BookTitle = session.Book?.Title,
            LessonId = session.LessonId,
            LessonTitle = session.Lesson?.Title,
            ContextSummary = session.ContextSummary,
            CreatedAtUtc = session.CreatedAtUtc,
            UpdatedAtUtc = session.UpdatedAtUtc,
            Messages = session.Messages
                .OrderBy(m => m.CreatedAtUtc)
                .Select(m => new ChatMessageDto
                {
                    Id = m.Id,
                    Role = m.Role,
                    Content = m.Content,
                    CreatedAtUtc = m.CreatedAtUtc,
                    SuggestedQuestions = DeserializeList(m.SuggestedQuestionsJson),
                    SourcesCited = DeserializeList(m.SourcesCitedJson)
                }).ToList()
        };
    }

    public async Task<ChatSessionDto> CreateChatSessionAsync(
        CreateChatSessionRequest request,
        Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var targetUserId = userId ?? User.DefaultUserId;

        Lesson? lesson = null;
        if (request.LessonId.HasValue && request.LessonId.Value != Guid.Empty)
        {
            lesson = await _dbContext.Lessons.AsNoTracking().FirstOrDefaultAsync(l => l.Id == request.LessonId.Value, cancellationToken);
        }

        string title = !string.IsNullOrWhiteSpace(request.Title)
            ? request.Title.Trim()
            : (lesson != null ? $"مدارسة: {lesson.Title}" : "محادثة جديدة");

        var session = new ChatSession
        {
            Title = title,
            UserId = targetUserId,
            BookId = request.BookId,
            LessonId = request.LessonId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _dbContext.ChatSessions.Add(session);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new ChatSessionDto
        {
            Id = session.Id,
            Title = session.Title,
            BookId = session.BookId,
            LessonId = session.LessonId,
            LessonTitle = lesson?.Title,
            MessageCount = 0,
            CreatedAtUtc = session.CreatedAtUtc,
            UpdatedAtUtc = session.UpdatedAtUtc
        };
    }

    public async Task<bool> DeleteChatSessionAsync(
        Guid sessionId,
        Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        var targetUserId = userId ?? User.DefaultUserId;
        var session = await _dbContext.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == targetUserId, cancellationToken);
        if (session == null) return false;

        _dbContext.ChatSessions.Remove(session);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ClearChatHistoryAsync(
        Guid? sessionId = null,
        Guid? lessonId = null,
        Guid? bookId = null,
        Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        var targetUserId = userId ?? User.DefaultUserId;

        if (sessionId.HasValue && sessionId.Value != Guid.Empty)
        {
            var session = await _dbContext.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId.Value && s.UserId == targetUserId, cancellationToken);
            if (session == null) return false;

            var messages = await _dbContext.ChatMessages.Where(m => m.SessionId == sessionId.Value).ToListAsync(cancellationToken);
            _dbContext.ChatMessages.RemoveRange(messages);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        var query = _dbContext.ChatSessions.Where(s => s.UserId == targetUserId).AsQueryable();
        if (lessonId.HasValue && lessonId.Value != Guid.Empty)
        {
            query = query.Where(s => s.LessonId == lessonId.Value);
        }
        else if (bookId.HasValue && bookId.Value != Guid.Empty)
        {
            query = query.Where(s => s.BookId == bookId.Value);
        }

        var sessions = await query.ToListAsync(cancellationToken);
        _dbContext.ChatSessions.RemoveRange(sessions);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<(string systemPrompt, string contextSummary, string? bookTitle, string? lessonTitle)> BuildSpecializedContextAsync(
        Lesson? lesson,
        Guid? bookId,
        CancellationToken cancellationToken)
    {
        string? lessonTitle = lesson?.Title;
        string? bookTitle = null;
        Book? book = null;

        Guid? targetBookId = bookId;
        if (!targetBookId.HasValue && lesson != null)
        {
            var bookLesson = await _dbContext.Lessons
                .AsNoTracking()
                .Include(l => l.Book)
                .FirstOrDefaultAsync(l => l.Id == lesson.Id, cancellationToken);
            if (bookLesson?.Book != null)
            {
                targetBookId = bookLesson.BookId;
                book = bookLesson.Book;
                bookTitle = book.Title;
            }
        }
        else if (targetBookId.HasValue)
        {
            book = await _dbContext.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == targetBookId.Value, cancellationToken);
            bookTitle = book?.Title;
        }

        LessonProgress? progress = null;
        if (lesson != null)
        {
            progress = await _dbContext.LessonProgresses.AsNoTracking().FirstOrDefaultAsync(p => p.LessonId == lesson.Id, cancellationToken);
        }

        List<StudentConceptMastery> weakConcepts = [];
        if (targetBookId.HasValue)
        {
            weakConcepts = await _dbContext.StudentConceptMasteries.AsNoTracking()
                .Where(m => m.BookId == targetBookId.Value && m.LearningLevel != LearningLevel.Mastered &&
                            (m.AssessmentCount > 0 || m.ExposureCount > 0) && m.MasteryScore < 0.65)
                .OrderBy(m => m.MasteryScore)
                .Take(5)
                .ToListAsync(cancellationToken);
        }

        var sb = new StringBuilder();
        sb.AppendLine("أنت «المعلم الذكي والباحث المتمرس في علوم الشريعة والحديث وكتب التراث» في منصة «دِراية AI».");
        sb.AppendLine("مهمتك مدارسة النصوص والمسائل مع طالب العلم بأسلوب علمي رصين، مشرق، وميسر، وتقديم إجابات محررة تجمع بين دقة التحقيق اللفظي وحسن البيان وبلاغة الخطاب العربي.");
        sb.AppendLine();
        sb.AppendLine("ضوابط وأسلوب الحوار وضبط الصياغة (Tone & Style Guidelines):");
        sb.AppendLine("1. الدخول في صلب الجواب مباشرة دون أي ديباجة مكررة أو مقدمات إنشائية مستهلكة؛ ادخل في المسألة بأسلوب حواري طبيعي وواضح.");
        sb.AppendLine("2. فصاحة العبارة وجزالة اللفظ: صغ الكلام بلغة عربية فصيحة، متقنة، وسليمة التركيب، مع تجنب التقعير المتكلف أو التسطيح المخل.");
        sb.AppendLine("3. الموازنة بين الإيجاز الوافي والبيان المحكم: إذا كان السؤال عن مسألة أو حكم فبيّن الجواب وعلته وضابطه مباشرة، وإذا طلب بسطاً ففصل الأقوال ووجوه الاستدلال بأدب وإنصاف.");
        sb.AppendLine("4. سلاسة العرض والبعد عن القوالب المصطنعة: تجنب التقسيمات الآلية الجافة ما لم تقتضِ طبيعة المسألة تفريعاً علمياً منطقياً.");
        sb.AppendLine("5. التحقيق اللفظي وتوضيح غريب الألفاظ والمصطلحات: بيّن دلالة الألفاظ المشكلة في سياق الحديث أو كلام الفقهاء بين هلالين بسلاسة.");
        sb.AppendLine("6. التنسيق الأنيق بـ Markdown: تمييز المفاهيم بالخط **العريض**، وتطويق نصوص الآيات والأحاديث بأقواس « »، وتوزيع الأفكار في فقرات مريحة للقراءة.");
        sb.AppendLine("7. اقتراح أسئلة متابعة ذكية (2 إلى 3 أسئلة نوعية تحفز على تعميق الفهم والاستنباط) مع ذكر أمهات المصادر المعتمدة.");
        sb.AppendLine();
        sb.AppendLine("يجب أن تكون الاستجابة حصراً بصيغة JSON الصالحة وفق المخطط التالي:");
        sb.AppendLine("{");
        sb.AppendLine("  \"reply\": \"نص الإجابة المباشرة والشرح المنسق بـ Markdown بلغة عربية فصيحة ومتقنة\",");
        sb.AppendLine("  \"suggestedQuestions\": [\"سؤال متابعة ذكي 1\", \"سؤال متابعة ذكي 2\"],");
        sb.AppendLine("  \"sourcesCited\": [\"اسم المرجع/الكتاب 1\", \"اسم المرجع/الكتاب 2\"]");
        sb.AppendLine("}");

        var contextSummaryBuilder = new List<string>();

        if (book != null || lesson != null || weakConcepts.Count > 0 || progress != null)
        {
            sb.AppendLine();
            sb.AppendLine("==================================================");
            sb.AppendLine("سياق المدارسة المخصص والملف التكيفي لهذه الجلسة:");

            if (book != null)
            {
                sb.AppendLine($"- الكتاب المدروس: «{book.Title}»");
                contextSummaryBuilder.Add($"كتاب: {book.Title}");
            }

            if (lesson != null)
            {
                sb.AppendLine($"- الدرس الحالي: «{lesson.Title}»");
                contextSummaryBuilder.Add($"درس: {lesson.Title}");
                if (!string.IsNullOrWhiteSpace(lesson.Overview))
                {
                    sb.AppendLine($"  الهدف والمقدمة: {lesson.Overview}");
                }

                if (progress != null)
                {
                    string statusText = progress.Status switch
                    {
                        LessonProgressStatus.Completed => "أتم الطالب مدارسة هذا الدرس بنجاح",
                        LessonProgressStatus.InProgress => "الطالب قيد قراءة ومدارسة هذا الدرس حالياً",
                        LessonProgressStatus.NeedsReview => "الدرس يحتاج إلى مراجعة وتثبيت إضافي",
                        _ => "الدرس جديد في بداية المدارسة"
                    };
                    sb.AppendLine($"- حالة إتقان الطالب للدرس: {statusText}");
                }

                if (lesson.Hadiths != null && lesson.Hadiths.Count > 0)
                {
                    sb.AppendLine("\nنصوص ومسائل وأدلة الدرس:");
                    foreach (var h in lesson.Hadiths)
                    {
                        sb.AppendLine($"--- [الحديث/المقطع: {h.Reference}] ---");
                        sb.AppendLine($"المسألة/الباب: {h.Problem}");
                        if (!string.IsNullOrWhiteSpace(h.Summary))
                        {
                            sb.AppendLine($"خلاصة المتن والموضوع: {h.Summary}");
                        }
                        if (!string.IsNullOrWhiteSpace(h.EasyExplanation))
                        {
                            sb.AppendLine($"الشرح الميسر: {h.EasyExplanation}");
                        }
                        if (h.Evidences != null && h.Evidences.Count > 0)
                        {
                            sb.AppendLine("الأدلة النصية:");
                            foreach (var ev in h.Evidences)
                            {
                                sb.AppendLine($"- {ev.Role}: {ev.Text}");
                            }
                        }
                        if (!string.IsNullOrWhiteSpace(h.Reasoning))
                        {
                            sb.AppendLine($"العلة ووجه الاستدلال: {h.Reasoning}");
                        }
                        if (!string.IsNullOrWhiteSpace(h.ScholarlyDiscussion))
                        {
                            sb.AppendLine($"المباحثة العلمية: {h.ScholarlyDiscussion}");
                        }
                        if (!string.IsNullOrWhiteSpace(h.Conclusion))
                        {
                            sb.AppendLine($"الخلاصة والضابط: {h.Conclusion}");
                        }
                    }
                }
            }

            if (weakConcepts.Count > 0)
            {
                var keys = string.Join("، ", weakConcepts.Select(c => $"«{c.ConceptKey}»"));
                sb.AppendLine($"\n[تنبيه بيداغوجي للمعلّم]: الطالب لديه مفاهيم سابقة تحتاج إلى مزيد من الضبط والترسيخ في هذا الكتاب: {keys}. احرص على تيسيرها وإيضاحها إن وردت في سياق الحديث.");
                contextSummaryBuilder.Add($"مفاهيم تحت التثبيت: {string.Join("، ", weakConcepts.Take(3).Select(c => c.ConceptKey))}");
            }

            sb.AppendLine("==================================================");
        }

        string? customInstructions = await _settingsService.GetAsync("AI:CustomResponseInstructions", cancellationToken);
        if (!string.IsNullOrWhiteSpace(customInstructions))
        {
            sb.AppendLine($"\n\n==================================================\nتوجيهات وأسلوب الرد والشرح المخصصة للمستخدم:\n{customInstructions}\nيجب الالتزام التام بهذه التوجيهات عند صياغة الشرح والإجابة.\n==================================================");
        }

        string contextSummary = contextSummaryBuilder.Count > 0 ? string.Join(" | ", contextSummaryBuilder) : "سياق عام للمقرر";
        return (sb.ToString(), contextSummary, bookTitle, lessonTitle);
    }

    private string BuildUserPrompt(LessonChatRequest request, Lesson? lesson)
    {
        var sb = new StringBuilder();
        if (lesson != null)
        {
            sb.AppendLine($"[سياق الدرس: {lesson.Title}]");
        }

        sb.AppendLine($"سؤال واستفسار المتعلم:");
        sb.AppendLine(request.Message);
        sb.AppendLine();
        sb.AppendLine("أجب مباشرة وبأسلوب حواري ميسر ومتقن بصيغة JSON، واقترح 2-3 أسئلة متابعة ذكية.");
        return sb.ToString();
    }

    private async Task<string?> CallPrimaryAiAsync(
        string systemPrompt,
        string userPrompt,
        List<ChatMessageDto>? history,
        CancellationToken cancellationToken)
    {
        string? apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("No primary AI API key resolved for lesson chat.");
            return null;
        }

        bool isGemini = string.IsNullOrWhiteSpace(_options.Provider) ||
                        string.Equals(_options.Provider, "Gemini", StringComparison.OrdinalIgnoreCase);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(4));

        if (isGemini)
        {
            string rawModel = _options.Model;
            var candidateModels = GeminiModelFallback.GetCandidateModels(rawModel);

            var contents = new List<object>();

            if (history != null && history.Count > 0)
            {
                var priorHistory = history.ToList();
                if (priorHistory.Count > 0 && priorHistory[0].Role != "user")
                {
                    priorHistory.RemoveAt(0);
                }

                string? lastRole = null;
                foreach (var msg in priorHistory.TakeLast(6))
                {
                    if (string.IsNullOrWhiteSpace(msg.Content)) continue;

                    string role = string.Equals(msg.Role, "assistant", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(msg.Role, "model", StringComparison.OrdinalIgnoreCase)
                        ? "model"
                        : "user";

                    if (role == lastRole) continue;
                    lastRole = role;

                    contents.Add(new
                    {
                        role,
                        parts = new[] { new { text = msg.Content } }
                    });
                }

                if (contents.Count > 0 && lastRole == "user")
                {
                    contents.RemoveAt(contents.Count - 1);
                }
            }

            contents.Add(new
            {
                role = "user",
                parts = new[] { new { text = userPrompt } }
            });

            var requestPayload = new
            {
                system_instruction = new
                {
                    parts = new[] { new { text = systemPrompt } }
                },
                contents,
                generationConfig = new
                {
                    response_mime_type = "application/json",
                    temperature = 0.3
                }
            };

            string payloadJson = JsonSerializer.Serialize(requestPayload);

            foreach (var model in candidateModels)
            {
                string endpoint = string.IsNullOrWhiteSpace(_options.Endpoint)
                    ? $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}"
                    : _options.Endpoint;

                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
                    req.Headers.Add("x-goog-api-key", apiKey);
                    req.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

                    try
                    {
                        using var resp = await _httpClient.SendAsync(req, cts.Token);
                        string body = await resp.Content.ReadAsStringAsync(cancellationToken);

                        if (resp.IsSuccessStatusCode)
                        {
                            using var doc = JsonDocument.Parse(body);
                            var root = doc.RootElement;
                            if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                            {
                                var candidate = candidates[0];
                                if (candidate.TryGetProperty("content", out var content) &&
                                    content.TryGetProperty("parts", out var parts) &&
                                    parts.GetArrayLength() > 0)
                                {
                                    return parts[0].GetProperty("text").GetString();
                                }
                            }
                        }

                        _logger.LogWarning("Gemini model '{Model}' chat attempt {Attempt} returned status {StatusCode}: {Body}", model, attempt, (int)resp.StatusCode, body);

                        if ((int)resp.StatusCode == 429 || (int)resp.StatusCode == 404)
                        {
                            break; // Move to next candidate model immediately
                        }

                        if ((int)resp.StatusCode >= 500 && attempt < 2)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                            continue;
                        }

                        break;
                    }
                    catch (Exception ex) when (attempt < 2 && (ex is HttpRequestException || ex is IOException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)))
                    {
                        _logger.LogWarning(ex, "Transient transport error on Gemini model '{Model}' chat attempt {Attempt}.", model, attempt);
                        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    }
                }
            }
        }
        else
        {
            string model = string.IsNullOrWhiteSpace(_options.Model) ? "gpt-4o" : _options.Model;
            string endpoint = string.IsNullOrWhiteSpace(_options.Endpoint)
                ? "https://api.openai.com/v1/chat/completions"
                : _options.Endpoint;

            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt }
            };

            if (history != null && history.Count > 0)
            {
                foreach (var msg in history.TakeLast(6))
                {
                    if (string.IsNullOrWhiteSpace(msg.Content)) continue;
                    string role = string.Equals(msg.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                        ? "assistant"
                        : "user";
                    messages.Add(new { role, content = msg.Content });
                }
            }

            messages.Add(new { role = "user", content = userPrompt });

            var requestPayload = new
            {
                model,
                messages,
                temperature = 0.3,
                response_format = new { type = "json_object" }
            };

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                req.Content = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json");

                try
                {
                    using var resp = await _httpClient.SendAsync(req, cts.Token);
                    string body = await resp.Content.ReadAsStringAsync(cancellationToken);

                    if (resp.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(body);
                        return doc.RootElement
                            .GetProperty("choices")[0]
                            .GetProperty("message")
                            .GetProperty("content")
                            .GetString();
                    }

                    if ((int)resp.StatusCode == 429 || (int)resp.StatusCode >= 500)
                    {
                        int backoff = attempt switch { 1 => 2, 2 => 5, _ => 8 };
                        _logger.LogWarning("OpenAI chat attempt {Attempt} received retryable status {StatusCode}. Waiting {Delay}s before retry...", attempt, (int)resp.StatusCode, backoff);
                        await Task.Delay(TimeSpan.FromSeconds(backoff), cancellationToken);
                        continue;
                    }

                    _logger.LogWarning("OpenAI API call failed with status {StatusCode}: {Body}", (int)resp.StatusCode, body);
                    break;
                }
                catch (Exception ex) when (attempt < 3 && (ex is HttpRequestException || ex is IOException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)))
                {
                    _logger.LogWarning(ex, "Transient transport error on OpenAI chat attempt {Attempt}. Retrying...", attempt);
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
                }
            }
        }

        return null;
    }

    private async Task<string?> CallFallbackAiAsync(
        string systemPrompt,
        string userPrompt,
        List<ChatMessageDto>? history,
        CancellationToken cancellationToken)
    {
        string? dbFallbackKey = await _settingsService.GetAsync("AI:FallbackApiKey", cancellationToken);
        string? dbFallbackModel = await _settingsService.GetAsync("AI:FallbackModel", cancellationToken);

        string? fallbackKey = !string.IsNullOrWhiteSpace(dbFallbackKey)
            ? dbFallbackKey
            : (string.IsNullOrWhiteSpace(_options.FallbackApiKey)
                ? (Environment.GetEnvironmentVariable("CONDUIT_API_KEY", EnvironmentVariableTarget.Process)
                   ?? Environment.GetEnvironmentVariable("CONDUIT_API_KEY", EnvironmentVariableTarget.User)
                   ?? Environment.GetEnvironmentVariable("CONDUIT_API_KEY", EnvironmentVariableTarget.Machine))
                : _options.FallbackApiKey);

        if (string.IsNullOrWhiteSpace(fallbackKey))
        {
            _logger.LogWarning("No Conduit fallback API key configured or resolved.");
            return null;
        }

        string fallbackModel = !string.IsNullOrWhiteSpace(dbFallbackModel)
            ? dbFallbackModel
            : (string.IsNullOrWhiteSpace(_options.FallbackModel) ? "claude-sonnet-4-5" : _options.FallbackModel);

        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };

        if (history != null && history.Count > 0)
        {
            foreach (var msg in history.TakeLast(6))
            {
                if (string.IsNullOrWhiteSpace(msg.Content)) continue;
                string role = string.Equals(msg.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                    ? "assistant"
                    : "user";
                messages.Add(new { role, content = msg.Content });
            }
        }

        messages.Add(new { role = "user", content = userPrompt });

        var payload = new
        {
            model = fallbackModel,
            messages,
            temperature = 0.3,
            response_format = new { type = "json_object" }
        };

        using var fallbackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        fallbackCts.CancelAfter(TimeSpan.FromMinutes(4));

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, _options.FallbackEndpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fallbackKey);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            try
            {
                using var resp = await _httpClient.SendAsync(req, fallbackCts.Token);
                string body = await resp.Content.ReadAsStringAsync(cancellationToken);

                if (resp.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(body);
                    return doc.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString();
                }

                if ((int)resp.StatusCode == 429 && attempt < 2)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                    continue;
                }

                _logger.LogWarning("Conduit Fallback API call failed with status {StatusCode}: {Body}", (int)resp.StatusCode, body);
                break;
            }
            catch (Exception ex) when (attempt < 2 && (ex is HttpRequestException || ex is IOException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)))
            {
                _logger.LogWarning(ex, "Transient transport error on Fallback chat attempt {Attempt}. Retrying...", attempt);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        return null;
    }

    private string? ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey)) return _options.ApiKey;

        string? envKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.Process)
                         ?? Environment.GetEnvironmentVariable("AI_API_KEY", EnvironmentVariableTarget.Process)
                         ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY", EnvironmentVariableTarget.Process)
                         ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.User)
                         ?? Environment.GetEnvironmentVariable("AI_API_KEY", EnvironmentVariableTarget.User)
                         ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY", EnvironmentVariableTarget.User)
                         ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.Machine)
                         ?? Environment.GetEnvironmentVariable("AI_API_KEY", EnvironmentVariableTarget.Machine)
                         ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY", EnvironmentVariableTarget.Machine);

        return envKey;
    }

    private static string CleanJsonFences(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "{}";
        string cleaned = Regex.Replace(text, @"^```[a-zA-Z]*\s*", "", RegexOptions.Multiline);
        cleaned = Regex.Replace(cleaned, @"\s*```$", "", RegexOptions.Multiline);
        cleaned = cleaned.Trim();

        int firstBrace = cleaned.IndexOf('{');
        int lastBrace = cleaned.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return cleaned.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        return cleaned;
    }

    private static List<string>? DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static List<string> GenerateDefaultSuggestions(Lesson? lesson)
    {
        if (lesson != null)
        {
            return
            [
                "اشرح لي وجه الاستدلال في هذا الباب بمثال معاصر",
                "ما هي الفوائد التربوية والإيمانية المستنبطة من هذا المقطع؟",
                "من هم الرواة المذكورون في هذا الإسناد وما حالهم؟"
            ];
        }

        return
        [
            "ما هي أهم القواعد الفقهية المستفادة من دروس الكتاب؟",
            "كيف أربط بين المسائل التي درستها لترسيخ الحفظ والاستيعاب؟",
            "اقترح عليّ درساً أو مسألة لمراجعتها الآن"
        ];
    }
}

