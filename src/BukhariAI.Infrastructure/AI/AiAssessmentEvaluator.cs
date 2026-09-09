using System.Text;
using System.Text.Json;
using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Assessments;
using BukhariAI.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BukhariAI.Infrastructure.AI;

public sealed class AiAssessmentEvaluator : IAssessmentEvaluator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<AiAssessmentEvaluator> _logger;

    public AiAssessmentEvaluator(
        HttpClient httpClient,
        IOptions<AiOptions> options,
        ISettingsService settingsService,
        ILogger<AiAssessmentEvaluator> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<AssessmentEvaluationResult> EvaluateAnswerAsync(
        AssessmentEvaluationInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Question);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.StudentAnswer);

        string? apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("No API key is configured for AI assessment evaluation.");
        }

        string model = string.IsNullOrWhiteSpace(_options.Model) || _options.Model.Contains("2.5")
            ? (string.Equals(_options.Provider, "OpenCode", StringComparison.OrdinalIgnoreCase) ? "deepseek-v4-flash" : "gemini-3.6-flash")
            : _options.Model;

        bool isOpenAiCompatible = string.Equals(_options.Provider, "OpenCode", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(_options.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase) ||
                                  (!string.IsNullOrWhiteSpace(_options.Endpoint) && _options.Endpoint.Contains("chat/completions"));

        string defaultEndpoint = string.Equals(_options.Provider, "OpenCode", StringComparison.OrdinalIgnoreCase)
            ? "https://opencode.ai/zen/v1/chat/completions"
            : string.Equals(_options.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase)
                ? "https://api.openai.com/v1/chat/completions"
                : $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

        string endpoint = string.IsNullOrWhiteSpace(_options.Endpoint) ? defaultEndpoint : _options.Endpoint;

        string? customInstructions = await _settingsService.GetAsync("AI:CustomResponseInstructions", cancellationToken);
        string systemPrompt = (await _settingsService.GetAsync("AI:AssessmentSystemPrompt", cancellationToken))
                              ?? GetBuiltInSystemPrompt();

        if (!string.IsNullOrWhiteSpace(customInstructions))
        {
            systemPrompt += $"\n\n==================================================\nتوجيهات وأسلوب الرد والتقييم الخاصة بالمستخدم (User Custom Prompt):\n{customInstructions}\nيجب الالتزام التام بهذه التوجيهات في صياغة التغذية الراجعة (feedback) والشرح.\n==================================================";
        }

        string userPrompt = BuildUserPrompt(input);

        string jsonPayload;
        if (isOpenAiCompatible)
        {
            var openAiPayload = new
            {
                model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.1,
                response_format = new { type = "json_object" }
            };
            jsonPayload = JsonSerializer.Serialize(openAiPayload);
        }
        else
        {
            var geminiPayload = new
            {
                system_instruction = new
                {
                    parts = new[]
                    {
                        new { text = systemPrompt }
                    }
                },
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[]
                        {
                            new { text = userPrompt }
                        }
                    }
                },
                generationConfig = new
                {
                    response_mime_type = "application/json",
                    temperature = 0.1
                }
            };
            jsonPayload = JsonSerializer.Serialize(geminiPayload);
        }

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            if (isOpenAiCompatible)
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            }
            else
            {
                request.Headers.Add("x-goog-api-key", apiKey);
            }
            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.SendAsync(request, cancellationToken);
                string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        string rawJsonText;
                        if (isOpenAiCompatible)
                        {
                            using var doc = JsonDocument.Parse(responseBody);
                            rawJsonText = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
                        }
                        else
                        {
                            rawJsonText = ExtractTextFromGeminiResponse(responseBody);
                        }

                        return ParseEvaluationResult(rawJsonText);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("The AI evaluator returned an invalid response.", ex);
                    }
                }

                if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                {
                    int delaySeconds = attempt switch
                    {
                        1 => 2,
                        2 => 3,
                        _ => 5
                    };
                    _logger.LogWarning("AI evaluation rate limit or error {StatusCode} on attempt {Attempt}. Waiting {Delay}s before retry...", (int)response.StatusCode, attempt, delaySeconds);
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                    continue;
                }

                _logger.LogWarning("AI evaluation call returned non-success status {StatusCode}: {Body}", (int)response.StatusCode, responseBody);
                break;
            }
            catch (Exception ex) when (attempt < 3 && (ex is HttpRequestException || ex is IOException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)))
            {
                _logger.LogWarning(ex, "Transient exception on evaluation attempt {Attempt}. Retrying in 2s...", attempt);
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HTTP exception while evaluating the student's answer.");
                throw;
            }
        }

        // ── Primary provider exhausted — try Conduit fallback ───────────────
        string? dbFallbackKey = await _settingsService.GetAsync("AI:FallbackApiKey", cancellationToken);
        string? dbFallbackModel = await _settingsService.GetAsync("AI:FallbackModel", cancellationToken);

        string? fallbackKey = !string.IsNullOrWhiteSpace(dbFallbackKey) 
            ? dbFallbackKey 
            : ResolveFallbackApiKey();

        if (!string.IsNullOrWhiteSpace(fallbackKey))
        {
            _logger.LogWarning("Primary AI provider failed. Attempting Conduit fallback endpoint '{Endpoint}'.", _options.FallbackEndpoint);
            try
            {
                string fallbackModel = !string.IsNullOrWhiteSpace(dbFallbackModel)
                    ? dbFallbackModel
                    : (string.IsNullOrWhiteSpace(_options.FallbackModel) ? "claude-sonnet-4-5" : _options.FallbackModel);

                var fallbackPayload = new
                {
                    model = fallbackModel,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user",   content = userPrompt }
                    },
                    temperature = 0.1,
                    response_format = new { type = "json_object" }
                };

                using var fallbackRequest = new HttpRequestMessage(HttpMethod.Post, _options.FallbackEndpoint);
                fallbackRequest.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", fallbackKey);
                fallbackRequest.Content = new StringContent(
                    JsonSerializer.Serialize(fallbackPayload), Encoding.UTF8, "application/json");

                var fallbackResponse = await _httpClient.SendAsync(fallbackRequest, cancellationToken);
                string fallbackBody = await fallbackResponse.Content.ReadAsStringAsync(cancellationToken);

                if (fallbackResponse.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(fallbackBody);
                    string rawJsonText = doc.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString() ?? string.Empty;

                    _logger.LogInformation("Conduit fallback succeeded for assessment evaluation.");
                    return ParseEvaluationResult(rawJsonText);
                }

                _logger.LogWarning("Conduit fallback returned non-success status {StatusCode}: {Body}",
                    (int)fallbackResponse.StatusCode, fallbackBody);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Conduit fallback also failed for assessment evaluation.");
            }
        }

        throw new InvalidOperationException("The AI evaluator is currently unavailable; no substitute evaluation was generated.");
    }

    public static string GetBuiltInSystemPrompt()
    {
        return """
            أنت مقيّم تربوي ومعلم شرعي متخصص في قياس الفهم والاستيعاب المفاهيمي لطلاب علوم الحديث والفقه.

            المبدأ التربوي الأساسي:
            رؤية المفهوم أو حفظ ألفاظه لا يعني فهمه. مطابقة الكلمات المفتاحية (Keyword Matching) وحدها لا تستحق الدرجة.

            قواعد التقييم الصارمة:
            1. قيّم الفهم المفاهيمي العميق (Conceptual Understanding) وليس مجرد ورود كلمات معينة.
            2. الطالب قد يستخدم صياغة مختلفة تماماً أو أسلوبه الخاص ومع ذلك يكون جوهر فهمه صحيحاً ودقيقاً 100% -> أعطه درجة كاملة.
            3. الطالب قد يذكر جميع المصطلحات والكلمات المفتاحية (مثل: الدباغ، القرظ، تخصيص العموم) ولكنه يخلط في معناها أو يربطها بنتيجة خاطئة أو يناقض الاستدلال -> لا تنخدع بالكلمات، واكشف سوء الفهم وضع درجة منخفضة وضع المفهوم في misconceptions.
            4. قارن إجابة الطالب بـ: النص المصدر، الشرح التعليمي، السؤال، المفاهيم المتوقعة، وإرشادات التقييم المعيارية.
            5. حدد بدقة:
               - score: درجة عددية من 0.0 إلى 1.0 تعبر عن جودة الفهم.
               - level: مستوى الإتقان المعروض في هذه الإجابة (Introduced | Familiar | Understood | Mastered).
               - understoodConcepts: قائمة بعناوين المفاهيم التي أثبت الطالب استيعابها (يجب أن يكون كل مفهوم عنواناً فقهياً موجزاً 2-5 كلمات، وممنوع إرجاع جمل كاملة أو نصوص مقطوعة).
               - missingConcepts: قائمة بالمفاهيم الأساسية المتوقعة التي أغفلها الطالب في إجابته (عناوين فقهية موجزة 2-5 كلمات).
               - misconceptions: قائمة بالمفاهيم التي فهمها الطالب خطأ أو خلط في الاستدلال بها (عناوين فقهية موجزة 2-5 كلمات).
               - feedback: تغذية راجعة تعليمية بناءة باللغة العربية توضح للطالب مواطن الإجادة وتصحح له الخلل بلباقة.

            مستويات التقييم (level):
            - Mastered: إجابة نموذجية ممتازة استوعبت المسألة وعمقتها وبينت وجه الاستدلال بدقة عالية (score >= 0.85).
            - Understood: إجابة صحيحة واضحة فهمت جوهر المسألة والدليل دون خلط (0.65 <= score < 0.85).
            - Familiar: إجابة أظهرت إلماماً جزئياً أو سطحياً ولكن ينقصها الربط والتعليل الأساسي (0.40 <= score < 0.65).
            - Introduced: إجابة ضعيفة جداً أو خاطئة أو تحتوي سوء فهم جوهري أو مجرد كلمات دون فهم (score < 0.40).

            أعد الإجابة بتنسيق JSON صالح فقط وفق المخطط التالي دون أي نصوص إضافية:
            {
              "score": 0.0,
              "level": "Introduced | Familiar | Understood | Mastered",
              "understoodConcepts": ["string — عنوان موجز 2-5 كلمات"],
              "missingConcepts": ["string — عنوان موجز 2-5 كلمات"],
              "misconceptions": ["string — عنوان موجز 2-5 كلمات"],
              "feedback": "string"
            }
            """;
    }

    private static string BuildUserPrompt(AssessmentEvaluationInput input)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## بيانات التقييم المفاهيمي لإجابة الطالب");
        sb.AppendLine();

        sb.AppendLine("### 1. نص المصدر المستخرج:");
        sb.AppendLine(input.OriginalSourceText);
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(input.LessonExplanation))
        {
            sb.AppendLine("### 2. الشرح التعليمي للدرس:");
            sb.AppendLine(input.LessonExplanation);
            sb.AppendLine();
        }

        sb.AppendLine("### 3. السؤال التقييمي:");
        sb.AppendLine($"السؤال: {input.Question}");
        if (!string.IsNullOrWhiteSpace(input.QuestionType)) sb.AppendLine($"نوع السؤال: {input.QuestionType}");
        if (!string.IsNullOrWhiteSpace(input.Difficulty)) sb.AppendLine($"مستوى الصعوبة: {input.Difficulty}");
        sb.AppendLine();

        if (input.ExpectedConcepts.Count > 0)
        {
            sb.AppendLine("### 4. المفاهيم المتوقع إثبات فهمها:");
            foreach (var c in input.ExpectedConcepts) sb.AppendLine($"- {c}");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(input.EvaluationGuidance))
        {
            sb.AppendLine("### 5. معيار وإرشادات التقييم:");
            sb.AppendLine(input.EvaluationGuidance);
            sb.AppendLine();
        }

        if (input.RelevantStudentMastery.Count > 0)
        {
            sb.AppendLine("### 6. السجل المعرفي المسبق للطالب في هذه المفاهيم:");
            foreach (var sm in input.RelevantStudentMastery)
            {
                sb.AppendLine($"- {sm.Concept}: المستوى الحالي={sm.Level}, الدرجة={sm.Score:F2}, مرات التقييم={sm.AssessmentCount}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("### 7. إجابة الطالب المقدمة للتقييم:");
        sb.AppendLine(input.StudentAnswer);
        sb.AppendLine();
        sb.AppendLine("قيّم إجابة الطالب مفاهيمياً وأعد الـ JSON.");

        return sb.ToString();
    }

    private static string ExtractTextFromGeminiResponse(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;

        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Gemini API response contained no candidates.");
        }

        var candidate = candidates[0];
        if (!candidate.TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) ||
            parts.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Gemini candidate content or parts were missing.");
        }

        return parts[0].GetProperty("text").GetString() ?? string.Empty;
    }

    private static AssessmentEvaluationResult ParseEvaluationResult(string rawJsonText)
    {
        string cleaned = CleanJsonString(rawJsonText);
        using var doc = JsonDocument.Parse(cleaned);
        var root = doc.RootElement;

        // If array, unwrap the first element
        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
        {
            root = root[0];
        }

        // If wrapped in an object like { "evaluation": { ... } } or { "assessment": { ... } }
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("evaluation", out var evalObj) && evalObj.ValueKind == JsonValueKind.Object)
            {
                root = evalObj;
            }
            else if (root.TryGetProperty("assessment", out var assessObj) && assessObj.ValueKind == JsonValueKind.Object)
            {
                root = assessObj;
            }
            else if (root.TryGetProperty("result", out var resObj) && resObj.ValueKind == JsonValueKind.Object)
            {
                root = resObj;
            }
        }

        double score = 0.0;
        if (root.TryGetProperty("score", out var s))
        {
            if (s.ValueKind == JsonValueKind.Number)
            {
                score = s.GetDouble();
            }
            else if (s.ValueKind == JsonValueKind.String && double.TryParse(s.GetString(), out double parsedScore))
            {
                score = parsedScore;
            }
        }
        score = Math.Clamp(score, 0.0, 1.0);

        string levelStr = root.TryGetProperty("level", out var l) ? (l.GetString() ?? "Introduced") : "Introduced";
        var level = ParseLearningLevel(levelStr, score);

        var understood = new List<string>();
        if (root.TryGetProperty("understoodConcepts", out var uc) && uc.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in uc.EnumerateArray())
            {
                string? val = item.GetString();
                string? clean = ConceptSanitizer.CleanAndValidate(val);
                if (clean != null && !understood.Contains(clean, StringComparer.OrdinalIgnoreCase)) understood.Add(clean);
            }
        }

        var missing = new List<string>();
        if (root.TryGetProperty("missingConcepts", out var mc) && mc.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in mc.EnumerateArray())
            {
                string? val = item.GetString();
                string? clean = ConceptSanitizer.CleanAndValidate(val);
                if (clean != null && !missing.Contains(clean, StringComparer.OrdinalIgnoreCase)) missing.Add(clean);
            }
        }

        var misconceptions = new List<string>();
        if (root.TryGetProperty("misconceptions", out var misc) && misc.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in misc.EnumerateArray())
            {
                string? val = item.GetString();
                string? clean = ConceptSanitizer.CleanAndValidate(val);
                if (clean != null && !misconceptions.Contains(clean, StringComparer.OrdinalIgnoreCase)) misconceptions.Add(clean);
            }
        }

        string feedback = root.TryGetProperty("feedback", out var fb) ? (fb.GetString() ?? string.Empty) : string.Empty;

        return new AssessmentEvaluationResult
        {
            Score = score,
            Level = level,
            UnderstoodConcepts = understood,
            MissingConcepts = missing,
            Misconceptions = misconceptions,
            Feedback = feedback
        };
    }

    private static LearningLevel ParseLearningLevel(string levelStr, double score)
    {
        if (Enum.TryParse<LearningLevel>(levelStr.Trim(), true, out var level))
        {
            return level;
        }

        return score switch
        {
            >= 0.85 => LearningLevel.Mastered,
            >= 0.65 => LearningLevel.Understood,
            >= 0.40 => LearningLevel.Familiar,
            _ => LearningLevel.Introduced
        };
    }

    private static string CleanJsonString(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[7..];
        else if (trimmed.StartsWith("```")) trimmed = trimmed[3..];
        if (trimmed.EndsWith("```")) trimmed = trimmed[..^3];
        trimmed = trimmed.Trim();

        int firstObj = trimmed.IndexOf('{');
        int firstArr = trimmed.IndexOf('[');
        int start = -1;
        int end = -1;

        if (firstObj >= 0 && firstArr >= 0)
        {
            if (firstObj < firstArr)
            {
                start = firstObj;
                end = trimmed.LastIndexOf('}');
            }
            else
            {
                start = firstArr;
                end = trimmed.LastIndexOf(']');
            }
        }
        else if (firstObj >= 0)
        {
            start = firstObj;
            end = trimmed.LastIndexOf('}');
        }
        else if (firstArr >= 0)
        {
            start = firstArr;
            end = trimmed.LastIndexOf(']');
        }

        if (start >= 0 && end >= start)
        {
            trimmed = trimmed.Substring(start, end - start + 1);
        }

        return trimmed.Trim();
    }

    private string? ResolveApiKey()
    {
        // 1. Configuration option
        if (!string.IsNullOrWhiteSpace(_options.ApiKey)) return _options.ApiKey;

        // 2. Process environment variable
        string? envKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.Process)
                         ?? Environment.GetEnvironmentVariable("AI_API_KEY", EnvironmentVariableTarget.Process)
                         ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY", EnvironmentVariableTarget.Process);
        if (!string.IsNullOrWhiteSpace(envKey)) return envKey;

        // 3. User environment variable
        envKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.User)
                 ?? Environment.GetEnvironmentVariable("AI_API_KEY", EnvironmentVariableTarget.User)
                 ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY", EnvironmentVariableTarget.User);
        if (!string.IsNullOrWhiteSpace(envKey)) return envKey;

        // 4. Machine environment variable
        envKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.Machine)
                 ?? Environment.GetEnvironmentVariable("AI_API_KEY", EnvironmentVariableTarget.Machine)
                 ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY", EnvironmentVariableTarget.Machine);
        if (!string.IsNullOrWhiteSpace(envKey)) return envKey;

        return null;
    }

    private string? ResolveFallbackApiKey()
    {
        // 1. DB-stored setting (set via Settings UI)
        // Note: We cannot call the async settings service here (sync context), so we rely on
        // the options and environment variable instead. The FallbackApiKey can also be set via
        // the Settings page which writes to appsettings.Development.json via IConfiguration reload,
        // but for simplicity the Settings controller writes it to the DB.
        // The EvaluateAnswerAsync method should pass it through if needed.
        // For now: options config → env var.
        if (!string.IsNullOrWhiteSpace(_options.FallbackApiKey)) return _options.FallbackApiKey;

        string? envKey = Environment.GetEnvironmentVariable("CONDUIT_API_KEY", EnvironmentVariableTarget.Process)
                         ?? Environment.GetEnvironmentVariable("CONDUIT_API_KEY", EnvironmentVariableTarget.User)
                         ?? Environment.GetEnvironmentVariable("CONDUIT_API_KEY", EnvironmentVariableTarget.Machine);
        return envKey;
    }
}
