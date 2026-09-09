using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BukhariAI.Application.Abstractions;
using BukhariAI.Application.ScratchLearning;
using BukhariAI.Infrastructure.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BukhariAI.Infrastructure.ScratchLearning;

public sealed class ScratchLearningService : IScratchLearningService
{
    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;
    private readonly ScratchLearningPromptBuilder _promptBuilder;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<ScratchLearningService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public ScratchLearningService(
        HttpClient httpClient,
        IOptions<AiOptions> options,
        ScratchLearningPromptBuilder promptBuilder,
        ISettingsService settingsService,
        ILogger<ScratchLearningService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _promptBuilder = promptBuilder;
        _settingsService = settingsService;
        _logger = logger;
    }

    public IReadOnlyList<CuratedScratchTrack> GetCuratedTracks()
    {
        return ScratchDataCatalog.CuratedTracks;
    }

    public async Task<ScratchRoadmapResponse> GenerateRoadmapAsync(
        ScratchRoadmapRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // 1. Check if trackId is provided directly
        if (!string.IsNullOrWhiteSpace(request.TrackId))
        {
            try
            {
                var curated = ScratchDataCatalog.GetCuratedRoadmap(request.TrackId);
                return curated;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load curated track by id: {TrackId}", request.TrackId);
            }
        }

        // 2. Check if topic matches a curated track
        if (ScratchDataCatalog.TryGetCuratedRoadmap(request.Topic, out var matchedCurated))
        {
            _logger.LogInformation("Matched topic '{Topic}' to curated track '{TrackTopic}'", request.Topic, matchedCurated.Topic);
            return matchedCurated;
        }

        // 3. Dynamic Generation via AI LLM
        string systemPrompt = _promptBuilder.BuildRoadmapSystemPrompt();
        string userPrompt = _promptBuilder.BuildRoadmapUserPrompt(request);

        string? customInstructions = await _settingsService.GetAsync("AI:CustomResponseInstructions", cancellationToken);
        if (!string.IsNullOrWhiteSpace(customInstructions))
        {
            systemPrompt += $"\n\n==================================================\nتوجيهات إضافية خاصة بالمستخدم:\n{customInstructions}\n==================================================";
        }

        string? rawJson = null;

        try
        {
            rawJson = await CallPrimaryAiAsync(systemPrompt, userPrompt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Primary AI provider failed for Scratch Roadmap generation. Trying fallback AI.");
        }

        if (string.IsNullOrWhiteSpace(rawJson))
        {
            try
            {
                rawJson = await CallFallbackAiAsync(systemPrompt, userPrompt, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallback AI failed for Scratch Roadmap generation.");
            }
        }

        if (!string.IsNullOrWhiteSpace(rawJson))
        {
            string cleanedJson = CleanJsonFences(rawJson);
            try
            {
                var result = JsonSerializer.Deserialize<ScratchRoadmapResponse>(cleanedJson, JsonOptions);
                if (result != null && result.Levels.Count > 0)
                {
                    // Recalculate totals for consistency
                    int totalMilestones = 0;
                    foreach (var lvl in result.Levels)
                    {
                        totalMilestones += lvl.Milestones.Count;
                    }
                    if (result.TotalMilestones <= 0) result = new ScratchRoadmapResponse
                    {
                        Topic = result.Topic,
                        Title = result.Title,
                        Introduction = result.Introduction,
                        TargetAudience = result.TargetAudience,
                        TotalMilestones = totalMilestones,
                        EstimatedTotalTimeMinutes = result.EstimatedTotalTimeMinutes > 0 ? result.EstimatedTotalTimeMinutes : totalMilestones * 5,
                        Levels = result.Levels
                    };
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize AI-generated Scratch Roadmap. Generating synthetic fallback.");
            }
        }

        // 4. Fallback Synthetic Generation for any custom topic
        return GenerateSyntheticFallbackRoadmap(request.Topic);
    }

    public async Task<ScratchStepExplanation> ExplainStepAsync(
        ExplainScratchStepRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        string systemPrompt = _promptBuilder.BuildRoadmapSystemPrompt();
        string userPrompt = _promptBuilder.BuildExplainStepPrompt(request);

        string? rawJson = null;
        try
        {
            rawJson = await CallPrimaryAiAsync(systemPrompt, userPrompt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Primary AI failed for ExplainStep. Attempting fallback.");
        }

        if (string.IsNullOrWhiteSpace(rawJson))
        {
            try
            {
                rawJson = await CallFallbackAiAsync(systemPrompt, userPrompt, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallback AI failed for ExplainStep.");
            }
        }

        if (!string.IsNullOrWhiteSpace(rawJson))
        {
            string cleanedJson = CleanJsonFences(rawJson);
            try
            {
                var result = JsonSerializer.Deserialize<ScratchStepExplanation>(cleanedJson, JsonOptions);
                if (result != null && !string.IsNullOrWhiteSpace(result.SimpleConcept))
                {
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize ExplainStep response.");
            }
        }

        return GenerateSyntheticStepExplanation(request);
    }

    public async Task<AskScratchTutorResponse> AskTutorAsync(
        AskScratchTutorRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        string systemPrompt = _promptBuilder.BuildRoadmapSystemPrompt();
        string userPrompt = _promptBuilder.BuildAskTutorPrompt(request);

        string? rawJson = null;
        try
        {
            rawJson = await CallPrimaryAiAsync(systemPrompt, userPrompt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Primary AI failed for AskTutor.");
        }

        if (string.IsNullOrWhiteSpace(rawJson))
        {
            try
            {
                rawJson = await CallFallbackAiAsync(systemPrompt, userPrompt, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallback AI failed for AskTutor.");
            }
        }

        if (!string.IsNullOrWhiteSpace(rawJson))
        {
            string cleanedJson = CleanJsonFences(rawJson);
            try
            {
                var result = JsonSerializer.Deserialize<AskScratchTutorResponse>(cleanedJson, JsonOptions);
                if (result != null && !string.IsNullOrWhiteSpace(result.Answer))
                {
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize AskTutor response.");
            }
        }

        return new AskScratchTutorResponse
        {
            Answer = $"أهلاً بك يا بطل! سؤالك حول «{request.UserQuestion}» ممتاز جداً. فكرة هذا المفهوم في المحطة «{request.CurrentMilestone}» تقوم على التدرج البسيط: نفهم الصورة العامة أولاً ثم نضبط القواعد دون قلق من التعقيد.",
            SimplifiedAnalogy = "تخيل أنك تبني بيتاً من مكعبات Lego: تبدأ بالقاعدة القوية (اللبنة الأولى) ثم ترتفع حبة بحبة حتى يكتمل البناء وتراه شامخاً.",
            KeyTakeaway = "القاعدة الذهبية: كل مسألة معقدة في التراث هي في أصلها فكرة بديهية بسيطة كُسيت مصطلحات لتنظيمها.",
            FollowUpSuggestion = "هل ترغب في أن نأخذ مثالاً عملياً من صحيح البخاري ونطبقه معاً خطوة بخطوة؟"
        };
    }

    private async Task<string?> CallPrimaryAiAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        string? apiKey = await _settingsService.GetAsync("AI:ApiKey", cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = _options.ApiKey;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogInformation("No primary AI ApiKey configured.");
            return null;
        }

        string provider = (await _settingsService.GetAsync("AI:Provider", cancellationToken)) ?? _options.Provider ?? "Gemini";
        string? rawModel = await _settingsService.GetAsync("AI:Model", cancellationToken);
        if (string.IsNullOrWhiteSpace(rawModel)) rawModel = _options.Model;

        string? customEndpoint = await _settingsService.GetAsync("AI:Endpoint", cancellationToken);
        if (string.IsNullOrWhiteSpace(customEndpoint)) customEndpoint = _options.Endpoint;

        if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
        {
            string model = string.IsNullOrWhiteSpace(rawModel) || rawModel.Contains("2.5") ? "gemini-3.6-flash" : rawModel;
            string endpoint = string.IsNullOrWhiteSpace(customEndpoint)
                ? $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent"
                : customEndpoint;

            var requestPayload = new
            {
                system_instruction = new
                {
                    parts = new[] { new { text = systemPrompt } }
                },
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new object[] { new { text = userPrompt } }
                    }
                },
                generationConfig = new
                {
                    response_mime_type = "application/json",
                    temperature = 0.2
                }
            };

            string payloadJson = JsonSerializer.Serialize(requestPayload);

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
                    req.Headers.Add("x-goog-api-key", apiKey);
                    req.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

                    using var resp = await _httpClient.SendAsync(req, cancellationToken);
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

                    _logger.LogWarning("Gemini Scratch generation returned status {StatusCode} on attempt {Attempt}", resp.StatusCode, attempt);

                    if ((int)resp.StatusCode == 429 || (int)resp.StatusCode >= 500)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
                        continue;
                    }
                    break;
                }
                catch (Exception ex) when (attempt < 3 && (ex is HttpRequestException || ex is IOException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)))
                {
                    _logger.LogWarning(ex, "Transient error on attempt {Attempt} for Scratch generation.", attempt);
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
                }
            }
        }
        else
        {
            // OpenAI compatible
            string model = string.IsNullOrWhiteSpace(rawModel) ? "gpt-4o" : rawModel;
            string endpoint = string.IsNullOrWhiteSpace(customEndpoint)
                ? "https://api.openai.com/v1/chat/completions"
                : customEndpoint;

            var requestPayload = new
            {
                model,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = userPrompt }
                },
                temperature = 0.2,
                response_format = new { type = "json_object" }
            };

            string payloadJson = JsonSerializer.Serialize(requestPayload);

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    req.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

                    using var resp = await _httpClient.SendAsync(req, cancellationToken);
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
                        await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
                        continue;
                    }
                    break;
                }
                catch (Exception ex) when (attempt < 3 && (ex is HttpRequestException || ex is IOException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)))
                {
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
                }
            }
        }

        return null;
    }

    private async Task<string?> CallFallbackAiAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
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

        if (string.IsNullOrWhiteSpace(fallbackKey)) return null;

        string fallbackModel = !string.IsNullOrWhiteSpace(dbFallbackModel)
            ? dbFallbackModel
            : (string.IsNullOrWhiteSpace(_options.FallbackModel) ? "claude-sonnet-4-5" : _options.FallbackModel);

        var payload = new
        {
            model = fallbackModel,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt }
            },
            temperature = 0.2,
            response_format = new { type = "json_object" }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, _options.FallbackEndpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fallbackKey);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await _httpClient.SendAsync(req, cancellationToken);
        if (resp.IsSuccessStatusCode)
        {
            string body = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }

        return null;
    }

    private static string CleanJsonFences(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        string trimmed = input.Trim();
        trimmed = Regex.Replace(trimmed, @"^```(?:json)?\s*", "", RegexOptions.IgnoreCase);
        trimmed = Regex.Replace(trimmed, @"\s*```$", "");
        return trimmed.Trim();
    }

    private static ScratchRoadmapResponse GenerateSyntheticFallbackRoadmap(string topic)
    {
        string sanitizedTopic = string.IsNullOrWhiteSpace(topic) ? "الموضوع المختار" : topic.Trim();

        return new ScratchRoadmapResponse
        {
            Topic = sanitizedTopic,
            Title = $"خريطة التأسيس المنهجي في «{sanitizedTopic}» (من الصفر إلى الإتقان)",
            Introduction = $"مرحباً بك في مسار التأسيس لـ «{sanitizedTopic}». صممت هذه الخريطة لتبسط لك المفهوم من الصفر المطلق خطوة بخطوة، مع أمثلة وتشبيهات ملموسة واختبارات تفاعلية.",
            TargetAudience = "مبتدئ تماماً (من الصفر)",
            TotalMilestones = 8,
            EstimatedTotalTimeMinutes = 40,
            Levels =
            [
                new ScratchLevel
                {
                    LevelNumber = 1,
                    LevelName = "المستوى الأول: التأسيس والمدخل البديهي",
                    Badge = "اللبنة الأولى 🟢",
                    ColorTheme = "#10B981",
                    Objective = "تكوين التصور الكلي للموضوع وكسر حاجز الهيبة بدون مصطلحات معقدة.",
                    Milestones =
                    [
                        new ScratchMilestone
                        {
                            Id = "m1-1",
                            Order = 1,
                            Title = $"ما هو {sanitizedTopic}؟ ولماذا نهتم به؟",
                            ShortSummary = "الصورة الكلية والغاية الأساسية من دراسة هذا الباب.",
                            Icon = "🌱",
                            KeyTerm = "التصور الكلي",
                            Explanation = new ScratchStepExplanation
                            {
                                MilestoneId = "m1-1",
                                MilestoneTitle = $"ما هو {sanitizedTopic}؟",
                                LevelName = "المستوى الأول",
                                SimpleConcept = $"فكرة «{sanitizedTopic}» تقوم ببساطة على حل مسألة محددة في فهم ونقل وتطبيق نصوص التراث والعلوم الشرعية بدقة وأمانة.",
                                RealWorldAnalogy = "تخيل أنك تنظر إلى خريطة مدينة من طائرة: ترى المعالم الكبرى والشوارع الرئيسية قبل أن تنزل إلى تفاصيل الأزقة والمباني.",
                                DetailedExplanation = "الخطوة الأولى في أي علم هي معرفة حده (تعريفه) وموضوعه وفائدته، حتى لا يتشتت ذهن المتعلم في الجزئيات الدقيقة قبل بناء القاعدة.",
                                Steps =
                                [
                                    new ScratchExplanationStep { StepNumber = 1, Title = "تحديد الغاية", Explanation = "ما الذي سنستفيده بعد إتقان هذا الموضوع؟" },
                                    new ScratchExplanationStep { StepNumber = 2, Title = "الربط بالواقع", Explanation = "كيف يظهر أثر هذا المفهوم في حياتنا وممارستنا اليومية؟" }
                                ],
                                CommonPitfalls = ["البدء بالخلافات والمسائل الصعبة قبل ضبط الصورة الكلية."],
                                PracticalExample = "قال العلماء: 'الحكم على الشيء فرع عن تصوره'، فلا بد أولاً من تصور سليم للباب.",
                                Quiz = new ScratchQuiz
                                {
                                    Question = $"ما هي الخطوة الأولى الصحيحة عند بدء دراسة «{sanitizedTopic}»؟",
                                    Options = ["القفز إلى الخلافات الدقيقة", "فهم التصور الكلي والغاية الأساسية بلغة ميسرة", "حفظ المصطلحات الصعبة فقط"],
                                    CorrectIndex = 1,
                                    Explanation = "التصور الكلي هو الأساس الذي تبنى عليه كافة القواعد اللاحقة.",
                                    ReinforcementTip = "افهم الفكرة العامة أولاً، وستجد كل تفصيل بعد ذلك سهلاً ومنطقياً."
                                }
                            }
                        },
                        new ScratchMilestone
                        {
                            Id = "m1-2",
                            Order = 2,
                            Title = $"الأركان الأساسية في {sanitizedTopic}",
                            ShortSummary = "تفكيك المفهوم إلى 2-3 عناصر جوهرية سهلة التذكر.",
                            Icon = "🧱",
                            KeyTerm = "الأركان والمكونات",
                            Explanation = new ScratchStepExplanation
                            {
                                MilestoneId = "m1-2",
                                MilestoneTitle = $"أركان {sanitizedTopic}",
                                LevelName = "المستوى الأول",
                                SimpleConcept = $"يتكون «{sanitizedTopic}» من أركان واضحة تترابط فيما بينها لتشكل المعنى التام.",
                                RealWorldAnalogy = "مثل أضلاع المثلث أو أعمدة الخيمة: كل ركن يدعم الركن الآخر ليكتمل البناء.",
                                DetailedExplanation = "بفهم هذه الأركان الأولية، يصبح لديك إطار ذهني واضح يمكنك من خلاله تصنيف أي معلومة جديدة بدقة.",
                                Steps =
                                [
                                    new ScratchExplanationStep { StepNumber = 1, Title = "الركن الأول (الأساس)", Explanation = "القاعدة التي ينطلق منها المفهوم." },
                                    new ScratchExplanationStep { StepNumber = 2, Title = "الركن الثاني (التطبيق)", Explanation = "كيف نتحقق من صحة انطباق القاعدة." }
                                ],
                                CommonPitfalls = ["الخلط بين الأركان الأساسية والزوائد التكميلية."],
                                PracticalExample = "تطبيق مباشر يوضح كيفية تفاعل هذه الأركان في نصوص التراث.",
                                Quiz = new ScratchQuiz
                                {
                                    Question = "لماذا نقوم بتفكيك المفهوم إلى أركان محددة؟",
                                    Options = ["لتسهيل الفهم والتمييز بين الأساسيات والفرعيات", "لزيادة صعوبة الحفظ", "لتضييع الوقت"],
                                    CorrectIndex = 0,
                                    Explanation = "التفكيك المنهجي يمنح الذهن وضوحاً فائقاً يمنع الخلط والتشويش.",
                                    ReinforcementTip = "اضبط الأركان أولاً، وسيسهل عليك إدراك التفريعات."
                                }
                            }
                        }
                    ]
                },
                new ScratchLevel
                {
                    LevelNumber = 2,
                    LevelName = "المستوى الثاني: البناء واكتساب المصطلحات",
                    Badge = "قواعد الصنعة 🟡",
                    ColorTheme = "#3B82F6",
                    Objective = "ضبط المصطلحات العلمية الدقيقة وربطها بالأساس التأسيسي الأول.",
                    Milestones =
                    [
                        new ScratchMilestone
                        {
                            Id = "m2-1",
                            Order = 3,
                            Title = $"المصطلحات الدقيقة في {sanitizedTopic}",
                            ShortSummary = "تسمية الأشياء بأسمائها الاصطلاحية المعتمدة عند الأئمة.",
                            Icon = "🏷️",
                            KeyTerm = "المصطلح العلمي",
                            Explanation = new ScratchStepExplanation
                            {
                                MilestoneId = "m2-1",
                                MilestoneTitle = $"مصطلحات {sanitizedTopic}",
                                LevelName = "المستوى الثاني",
                                SimpleConcept = "المصطلح هو اتفاق بين أهل الفن على استخدام لفظ معين للدلالة على معنى دقيق ومحدد.",
                                RealWorldAnalogy = "مثل المصطلحات البرمجية (API, Database, Function): كل مصطلح يختصر مفهوماً برمجياً متفقاً عليه.",
                                DetailedExplanation = "معرفة المصطلحات تفتح لك أبواب قراءة كتب التراث مباشرة دون وسيط، وتجعلك تفهم عبارات الأئمة كما أرادوها.",
                                Steps =
                                [
                                    new ScratchExplanationStep { StepNumber = 1, Title = "التعريف اللغوي والاصطلاحي", Explanation = "أصل الكلمة في لغة العرب ثم معناها الشرعي." },
                                    new ScratchExplanationStep { StepNumber = 2, Title = "الربط بالتشبيه", Explanation = "تثبيت المصطلح برابط ذهني واقعي." }
                                ],
                                CommonPitfalls = ["استخدام المصطلح في غير موضعه الصحيح."],
                                PracticalExample = "نماذج من استعمال الأئمة لهذا المصطلح في كتبهم المعتمدة.",
                                Quiz = new ScratchQuiz
                                {
                                    Question = "ما هي الفائدة الأساسية من ضبط المصطلحات العلمية؟",
                                    Options = ["التعالي على المبتدئين", "فهم كلام الأئمة بدقة واختصار المعاني الطويلة في كلمات محددة", "إلغاء المعنى البسيط"],
                                    CorrectIndex = 1,
                                    Explanation = "المصطلح العلمي هو لغة التخاطب بين المتخصصين التي تختصر المعاني بدقة متناهية.",
                                    ReinforcementTip = "لكل فن لغته ومصطلحاته، وضبطها نصف العلم."
                                }
                            }
                        },
                        new ScratchMilestone
                        {
                            Id = "m2-2",
                            Order = 4,
                            Title = $"القواعد والضوابط الحاكمة",
                            ShortSummary = "المعايير التي تفصل بين الصحيح والخطأ في هذا الباب.",
                            Icon = "📐",
                            KeyTerm = "القواعد والضوابط",
                            Explanation = new ScratchStepExplanation
                            {
                                MilestoneId = "m2-2",
                                MilestoneTitle = $"ضوابط {sanitizedTopic}",
                                LevelName = "المستوى الثاني",
                                SimpleConcept = "الضابط هو القاعدة الكلية التي تجمع مسائل الباب الواحد تحت قانون موحد.",
                                RealWorldAnalogy = "مثل قوانين الفيزياء أو قواعد المرور: تضمن سلامة المسار وتمنع الحوادث والانحرافات.",
                                DetailedExplanation = "الضوابط الشرعية تحمي الباحث من التناقض، وتجعله يقيس المسائل المتشابهة بنفس الميزان العادل.",
                                Steps =
                                [
                                    new ScratchExplanationStep { StepNumber = 1, Title = "حفظ نص القاعدة", Explanation = "استحضار العبارة الضابطة للباب." },
                                    new ScratchExplanationStep { StepNumber = 2, Title = "معرفة نطاقها", Explanation = "ما الذي يدخل تحتها وما الذي يخرج عنها؟" }
                                ],
                                CommonPitfalls = ["تعميم الضابط على مسائل خارجة عن مجاله."],
                                PracticalExample = "أمثلة تطبيقية تبرز انطباق الضابط على الفروع المختلفة.",
                                Quiz = new ScratchQuiz
                                {
                                    Question = "ما وظيفة 'الضابط' في العلوم الشرعية؟",
                                    Options = ["جمع المسائل المتفرقة تحت قاعدة كلية موحدة", "زيادة عدد الصفحات", "تغيير الأحكام عشوائياً"],
                                    CorrectIndex = 0,
                                    Explanation = "الضابط ينظم الجزئيات المتناثرة في قاعدة كلية واحدة محكمة.",
                                    ReinforcementTip = "من حفظ الضوابط هانت عليه مئات المسائل الفرعية."
                                }
                            }
                        }
                    ]
                },
                new ScratchLevel
                {
                    LevelNumber = 3,
                    LevelName = "المستوى الثالث: التطبيق ونماذج التراث",
                    Badge = "ميدان العمل 🟠",
                    ColorTheme = "#F59E0B",
                    Objective = "تطبيق القواعد على نصوص حقيقية من صحيح البخاري وكتب الأثر.",
                    Milestones =
                    [
                        new ScratchMilestone
                        {
                            Id = "m3-1",
                            Order = 5,
                            Title = $"تطبيق عملي: تحليل نص تراثي في {sanitizedTopic}",
                            ShortSummary = "دراسة حالة خطوة بخطوة من أمهات الكتب.",
                            Icon = "🔍",
                            KeyTerm = "التحليل الميداني",
                            Explanation = new ScratchStepExplanation
                            {
                                MilestoneId = "m3-1",
                                MilestoneTitle = "تطبيق عملي وتحليل تراثي",
                                LevelName = "المستوى الثالث",
                                SimpleConcept = "سنأخذ نصاً حقيقياً ونطبق عليه كل ما تعلمناه في المستويين الأول والثاني خطوة بخطوة.",
                                RealWorldAnalogy = "مثل التدريب العملي في المختبر بعد دراسة القوانين النظرية في الفصل الدراسي.",
                                DetailedExplanation = "التطبيق العملي هو المحك الحقيقي لترسيخ الفهم؛ به تكتشف كيف كان الأئمة يتعاملون مع النصوص بواقعية وعمق.",
                                Steps =
                                [
                                    new ScratchExplanationStep { StepNumber = 1, Title = "قراءة النص وضبط ألفاظه", Explanation = "التأكد من سلامة المتن والإسناد." },
                                    new ScratchExplanationStep { StepNumber = 2, Title = "إجراء التحليل واستنباط النتائج", Explanation = "تطبيق الضوابط واستخراج الحكم." }
                                ],
                                CommonPitfalls = ["الاكتفاء بالدراسة النظرية دون خوض غمار التطبيق."],
                                PracticalExample = "نموذج تحليلي معاصر يوضح ثمرة التطبيق على نص من صحيح البخاري.",
                                Quiz = new ScratchQuiz
                                {
                                    Question = "ما هي الفائدة الكبرى من التطبيق العملي على نصوص التراث؟",
                                    Options = ["تحويل القواعد النظرية إلى مهارة وملكة راسخة في ذهن الباحث", "الاكتفاء بالحفظ السطحي", "إلغاء القواعد"],
                                    CorrectIndex = 0,
                                    Explanation = "الممارسة التطبيقية هي التي تصنع العالم والباحث المتمكن.",
                                    ReinforcementTip = "العلم صيد والتطبيق قيده."
                                }
                            }
                        },
                        new ScratchMilestone
                        {
                            Id = "m3-2",
                            Order = 6,
                            Title = "كشف الأخطاء الشائعة والشبهات",
                            ShortSummary = "كيف تتجنب المزالق الفكرية والمنهجية التي يقع فيها المبتدئون.",
                            Icon = "⚠️",
                            KeyTerm = "درء الأوهام",
                            Explanation = new ScratchStepExplanation
                            {
                                MilestoneId = "m3-2",
                                MilestoneTitle = "الأخطاء الشائعة وكيفية تجنبها",
                                LevelName = "المستوى الثالث",
                                SimpleConcept = "معرفة الخطأ تحميك من الوقوع فيه؛ حصر أشهر 3 أخطاء شائعة في هذا الباب وطرق تفاديها.",
                                RealWorldAnalogy = "مثل لوحات التحذير على الطرق السريعة (انعطاف حاد، منطقة ضباب): تنبهك قبل أن تقع في الخطر.",
                                DetailedExplanation = "كثير من الإشكالات تنشأ بسبب سوء فهم مصطلح أو إسقاط مفهوم معاصر على نص قديم؛ لذا كان بيان الأخطاء صيانة للمنهج.",
                                Steps =
                                [
                                    new ScratchExplanationStep { StepNumber = 1, Title = "رصد الخطأ الشائع", Explanation = "تحديد الوهم بدقة." },
                                    new ScratchExplanationStep { StepNumber = 2, Title = "تبيين الوجه الصواب", Explanation = "الدليل على صحة المنهج المعتمد." }
                                ],
                                CommonPitfalls = ["اتباع الآراء الشاذة دون الرجوع إلى إجماع المحققين."],
                                PracticalExample = "بيان خطأ شائع وقع فيه بعض المعاصرين وكيف رده أئمة التحقيق.",
                                Quiz = new ScratchQuiz
                                {
                                    Question = "لماذا ندرس الأخطاء الشائعة والمزالق المنهجية؟",
                                    Options = ["لحماية الفهم من التحريف والانحراف ولتثبيت المنهج الصحيح", "للترويج للأخطاء", "للتسلية"],
                                    CorrectIndex = 0,
                                    Explanation = "معرفة مواضع الزلل هي أفضل وقاية للمتعلم في مسيرته العلمية.",
                                    ReinforcementTip = "عرفتُ الشر لا للشر لكن لتوقيه * ومن لا يعرف الخير من الشر يقع فيه."
                                }
                            }
                        }
                    ]
                },
                new ScratchLevel
                {
                    LevelNumber = 4,
                    LevelName = "المستوى الرابع: التعميق والإتقان والتمكين",
                    Badge = "رسوخ وتخصص 🟣",
                    ColorTheme = "#8B5CF6",
                    Objective = "دقائق المسائل، الفروق الخفية، ودمج المعرفة مع أدوات منصة دِراية AI.",
                    Milestones =
                    [
                        new ScratchMilestone
                        {
                            Id = "m4-1",
                            Order = 7,
                            Title = $"دقائق المسائل والعلل الخفية في {sanitizedTopic}",
                            ShortSummary = "المستوى التخصصي: الفروق الدقيقة والمناقشات العميقة للمحققين.",
                            Icon = "💎",
                            KeyTerm = "دقائق التحقيق",
                            Explanation = new ScratchStepExplanation
                            {
                                MilestoneId = "m4-1",
                                MilestoneTitle = "المستوى التخصصي ودقائق المسائل",
                                LevelName = "المستوى الرابع",
                                SimpleConcept = "هنا ننتقل إلى دقائق الفن: استثناءات القواعد، المسائل المركبة، وأسرار الفروق بين العبارات المتقاربة.",
                                RealWorldAnalogy = "مثل المهندس المعماري المتقدم الذي يحلل إجهادات المواد واهتزازات الزلازل الدقيقة جداً.",
                                DetailedExplanation = "هذا المستوى يصقل الملكة العلمية ويجعلك قادراً على الموازنة والترجيح بين أقوال أهل العلم ببصيرة ورسوخ.",
                                Steps =
                                [
                                    new ScratchExplanationStep { StepNumber = 1, Title = "المقارنة والموازنة", Explanation = "دراسة أوجه الخلاف وحجج كل فريق." },
                                    new ScratchExplanationStep { StepNumber = 2, Title = "استخلاص الراجح", Explanation = "بناء الرأي على أقوى الأدلة وأصح القواعد." }
                                ],
                                CommonPitfalls = ["التعصب لقول دون النظر في وجوه أدلة المخالفين."],
                                PracticalExample = "مسألة دقيقة حققها أئمة النقد كابن حجر أو البخاري أو النووي.",
                                Quiz = new ScratchQuiz
                                {
                                    Question = "ما هي السمة الأساسية للباحث المتمكن الراسخ في هذا المستوى المتقدم؟",
                                    Options = ["الإنصاف العلمي والقدرة على فهم دقائق الأدلة والترجيح المنهجي", "التسرع والتعصب", "رفض الحوار"],
                                    CorrectIndex = 0,
                                    Explanation = "الرسوخ العلمي يورث الإنصاف والدقة وحسن الأدب مع أقوال العلماء.",
                                    ReinforcementTip = "قمة العلم تورث التواضع وسعة الصدر ورسوخ الفهم."
                                }
                            }
                        },
                        new ScratchMilestone
                        {
                            Id = "m4-2",
                            Order = 8,
                            Title = "الخلاصة والتمكين في منصة دِراية AI",
                            ShortSummary = "دمج المهارات المكتسبة مع أدوات المنصة للمحافظة على الرسوخ ومكافحة النسيان.",
                            Icon = "🎓",
                            KeyTerm = "الرسوخ والاستمرار",
                            Explanation = new ScratchStepExplanation
                            {
                                MilestoneId = "m4-2",
                                MilestoneTitle = "التمكين وصناعة الأثر المستمر",
                                LevelName = "المستوى الرابع",
                                SimpleConcept = $"مبارك! لقد أتممت خريطة «{sanitizedTopic}» من الصفر إلى الإتقان. لديك الآن الأدوات الكاملة للمواصلة والمراجعة الذكية.",
                                RealWorldAnalogy = "أصبحت تملك البوصلة والخرائط والمفتاح؛ يمكنك الإبحار في أي كتاب تراثي بثقة وأمان.",
                                DetailedExplanation = "استثمر أدوات منصة دِراية AI لمواصلة المدارسة، وتثبيت المفاهيم بخوارزمية التكرار المتباعد (SM-2)، وطرح أي استفسار على المعلم الذكي.",
                                Steps =
                                [
                                    new ScratchExplanationStep { StepNumber = 1, Title = "المراجعة الدورية", Explanation = "أداء التقييمات التفاعلية أسبوعياً." },
                                    new ScratchExplanationStep { StepNumber = 2, Title = "تطبيق المعرفة ونشرها", Explanation = "تعليم غيرك ومدارسة الإخوان." }
                                ],
                                CommonPitfalls = ["الانقطاع عن المدارسة والظن بأن المعرفة تثبت دون تعاهد مستمر."],
                                PracticalExample = "مواصلة قراءة المتون والكتب المعتمدة بانتظام مع تدوين الفوائد.",
                                Quiz = new ScratchQuiz
                                {
                                    Question = "ما هو سر الحفاظ على المكتسبات العلمية التي تعلمتها في هذا المسار؟",
                                    Options = ["التعاهد المستمر والمراجعة الدورية والتطبيق في القراءة اليومية", "إغلاق المنصة نهائياً", "الاعتماد على الذاكرة دون مراجعة"],
                                    CorrectIndex = 0,
                                    Explanation = "التعاهد المستمر والممارسة هما سر ثبات العلم ورسوخ الملكات.",
                                    ReinforcementTip = "مبارك إتمامك لهذا المسار التأسيسي بنجاح وتفوق!"
                                }
                            }
                        }
                    ]
                }
            ]
        };
    }

    private static ScratchStepExplanation GenerateSyntheticStepExplanation(ExplainScratchStepRequest request)
    {
        return new ScratchStepExplanation
        {
            MilestoneId = "custom-step",
            MilestoneTitle = request.MilestoneTitle,
            LevelName = request.LevelName,
            SimpleConcept = $"الفكرة الأساسية لمحطة «{request.MilestoneTitle}» تقوم على تبسيط هذا المفهوم إلى لغة واضحة دون أي تعقيد مصطلحي.",
            RealWorldAnalogy = "مثل بناء الجدار: كل حجر يوضع في مكانه الصحيح ليستند عليه الحجر التالي بأمان وثبات.",
            DetailedExplanation = $"في هذه المحطة من مسار «{request.Topic}»، نتدرج في شرح «{request.MilestoneTitle}» عبر تفكيك عناصرها وربطها بأمثلة تطبيقية.",
            Steps =
            [
                new ScratchExplanationStep { StepNumber = 1, Title = "الاستيعاب المبدئي", Explanation = "فهم المعنى العام للمحطة." },
                new ScratchExplanationStep { StepNumber = 2, Title = "التأصيل العلمي", Explanation = "معرفة القاعدة التي تنظم هذا المفهوم." },
                new ScratchExplanationStep { StepNumber = 3, Title = "التطبيق العملي", Explanation = "اختبار الفهم على نص حقيقي." }
            ],
            CommonPitfalls =
            [
                "الاستعجال في الحكم قبل اكتمال أركان المسألة.",
                "إغفال السياق العام للنص التراثي."
            ],
            PracticalExample = "تطبيق من كتب السنة والأثر يوضح كيفية تنزيل هذا المفهوم عملياً.",
            Quiz = new ScratchQuiz
            {
                Question = $"ما هي الغاية الأساسية من محطة «{request.MilestoneTitle}»؟",
                Options = ["بناء الفهم التأسيسي المتدرج خطوة بخطوة", "حفظ الألفاظ دون تدبر", "تخطي الأساسيات"],
                CorrectIndex = 0,
                Explanation = "الغاية هي بناء الفهم الراسخ الممنهج خطوة بخطوة.",
                ReinforcementTip = "تدرج في الفهم يثبت العلم في الصدور."
            }
        };
    }
}
