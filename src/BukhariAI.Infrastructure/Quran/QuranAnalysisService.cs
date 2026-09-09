using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Quran;
using BukhariAI.Infrastructure.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BukhariAI.Infrastructure.Quran;

public sealed class QuranAnalysisService : IQuranAnalysisService
{
    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;
    private readonly QuranPromptBuilder _promptBuilder;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<QuranAnalysisService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public QuranAnalysisService(
        HttpClient httpClient,
        IOptions<AiOptions> options,
        QuranPromptBuilder promptBuilder,
        ISettingsService settingsService,
        ILogger<QuranAnalysisService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _promptBuilder = promptBuilder;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<QuranSurahAnalysisResponse> AnalyzeSurahAsync(
        AnalyzeQuranRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // Enrich request with metadata if available
        if (request.SurahNumber.HasValue && string.IsNullOrWhiteSpace(request.SurahName))
        {
            var meta = QuranDataCatalog.FindByNumber(request.SurahNumber.Value);
            if (meta != null)
            {
                request = new AnalyzeQuranRequest
                {
                    SurahNumber = meta.Number,
                    SurahName = meta.Name,
                    StartAyah = request.StartAyah,
                    EndAyah = request.EndAyah,
                    CustomText = request.CustomText
                };
            }
        }

        string systemPrompt = _promptBuilder.BuildSystemPrompt();
        string userPrompt = _promptBuilder.BuildUserPrompt(request);

        string? customInstructions = await _settingsService.GetAsync("AI:CustomResponseInstructions", cancellationToken);
        if (!string.IsNullOrWhiteSpace(customInstructions))
        {
            systemPrompt += $"\n\n==================================================\nتوجيهات إضافية خاصة بالمستخدم:\n{customInstructions}\n==================================================";
        }

        string? rawJson = null;

        // 1. Try Primary AI Provider (Gemini / OpenAI / OpenCode)
        try
        {
            rawJson = await CallPrimaryAiAsync(systemPrompt, userPrompt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Primary AI provider failed for Quran analysis. Attempting fallback.");
        }

        // 2. Try Fallback Provider (Conduit) if primary failed or returned empty
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            rawJson = await CallFallbackAiAsync(systemPrompt, userPrompt, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(rawJson))
        {
            throw new InvalidOperationException("تعذر الحصول على استجابة صالحة من نموذج الذكاء الاصطناعي لتحليل السورة.");
        }

        // 3. Clean and Deserialize JSON with robust auto-repair fallback
        string cleanedJson = CleanJsonFences(rawJson);
        QuranSurahAnalysisResponse? response = null;

        try
        {
            response = JsonSerializer.Deserialize<QuranSurahAnalysisResponse>(cleanedJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Initial JSON deserialization failed. Attempting intelligent repair of unescaped quotes/syntax.");
            try
            {
                string repairedJson = RepairUnescapedQuotesInJson(cleanedJson);
                response = JsonSerializer.Deserialize<QuranSurahAnalysisResponse>(repairedJson, JsonOptions);
            }
            catch (Exception repairEx)
            {
                _logger.LogError(repairEx, "Failed to deserialize Quran analysis JSON even after auto-repair: {RawJson}", rawJson);
                throw new InvalidOperationException("فشل تحليل استجابة الذكاء الاصطناعي؛ لم تكن بتنسيق JSON المطابق للمخطط.", repairEx);
            }
        }

        if (response == null || string.IsNullOrWhiteSpace(response.SurahInfo?.Name))
        {
            throw new InvalidOperationException("استجابة الذكاء الاصطناعي فارغة أو تفتقر لمعلومات السورة الأساسية.");
        }

        return response;
    }

    private async Task<string?> CallPrimaryAiAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        string? dbApiKey = await _settingsService.GetAsync("AI:ApiKey", cancellationToken);
        string? apiKey = !string.IsNullOrWhiteSpace(dbApiKey) ? dbApiKey : ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("No API key configured for Quran analysis.");
            return null;
        }

        string? dbProvider = await _settingsService.GetAsync("AI:Provider", cancellationToken);
        string provider = !string.IsNullOrWhiteSpace(dbProvider) ? dbProvider : _options.Provider;

        string? dbModel = await _settingsService.GetAsync("AI:Model", cancellationToken);
        string rawModel = !string.IsNullOrWhiteSpace(dbModel) ? dbModel : _options.Model;

        string? dbEndpoint = await _settingsService.GetAsync("AI:Endpoint", cancellationToken);
        string? customEndpoint = !string.IsNullOrWhiteSpace(dbEndpoint) ? dbEndpoint : _options.Endpoint;

        bool isGemini = string.IsNullOrWhiteSpace(provider) ||
                        string.Equals(provider, "Gemini", StringComparison.OrdinalIgnoreCase);

        if (isGemini)
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

                    _logger.LogWarning("Gemini Quran analysis returned non-success status {StatusCode} on attempt {Attempt}: {ResponseBody}", resp.StatusCode, attempt, body);

                    if ((int)resp.StatusCode == 429 || (int)resp.StatusCode >= 500)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
                        continue;
                    }

                    break;
                }
                catch (Exception ex) when (attempt < 3 && (ex is HttpRequestException || ex is IOException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)))
                {
                    _logger.LogWarning(ex, "Transient transport/timeout exception on attempt {Attempt} contacting Gemini for Quran analysis. Retrying...", attempt);
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
                }
            }
        }
        else
        {
            // OpenAI-compatible endpoint
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

                    _logger.LogWarning("OpenAI-compatible Quran analysis returned non-success status {StatusCode} on attempt {Attempt}: {ResponseBody}", resp.StatusCode, attempt, body);

                    if ((int)resp.StatusCode == 429 || (int)resp.StatusCode >= 500)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
                        continue;
                    }

                    break;
                }
                catch (Exception ex) when (attempt < 3 && (ex is HttpRequestException || ex is IOException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)))
                {
                    _logger.LogWarning(ex, "Transient transport/timeout exception on attempt {Attempt} contacting AI for Quran analysis. Retrying...", attempt);
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

        return null;
    }

    private static string CleanJsonFences(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        string cleaned = input.Trim();
        var match = Regex.Match(cleaned, @"^```(?:json)?\s*([\s\S]*?)\s*```$", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            cleaned = match.Groups[1].Value.Trim();
        }

        // Remove trailing commas before } or ]
        cleaned = Regex.Replace(cleaned, @",\s*([\]}])", "$1");

        return cleaned;
    }

    private static string RepairUnescapedQuotesInJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;

        var sb = new StringBuilder(json.Length + 128);
        bool inString = false;

        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];

            if (c == '\\' && inString && i + 1 < json.Length)
            {
                sb.Append(c);
                sb.Append(json[i + 1]);
                i++;
                continue;
            }

            if (c == '"')
            {
                if (!inString)
                {
                    inString = true;
                    sb.Append(c);
                }
                else
                {
                    // Check if this quote legitimately closes a JSON string or property name:
                    // A legitimate closing quote is followed (skipping whitespace) by structural chars: ',', '}', ']', or ':'
                    int nextIdx = i + 1;
                    while (nextIdx < json.Length && char.IsWhiteSpace(json[nextIdx]))
                    {
                        nextIdx++;
                    }

                    if (nextIdx < json.Length && (json[nextIdx] == ',' || json[nextIdx] == '}' || json[nextIdx] == ']' || json[nextIdx] == ':'))
                    {
                        inString = false;
                        sb.Append(c);
                    }
                    else
                    {
                        // It's an internal unescaped quote inside Arabic text (e.g. "نزلت في "يا عبادي"...")
                        sb.Append("\\\"");
                    }
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        string result = sb.ToString();
        // Remove trailing commas before } or ]
        result = Regex.Replace(result, @",\s*([\]}])", "$1");
        return result;
    }

    private string? ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey)) return _options.ApiKey;

        string? envKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.Process)
                         ?? Environment.GetEnvironmentVariable("AI_API_KEY", EnvironmentVariableTarget.Process)
                         ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY", EnvironmentVariableTarget.Process);
        if (!string.IsNullOrWhiteSpace(envKey)) return envKey;

        envKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.User)
                 ?? Environment.GetEnvironmentVariable("AI_API_KEY", EnvironmentVariableTarget.User)
                 ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY", EnvironmentVariableTarget.User);
        if (!string.IsNullOrWhiteSpace(envKey)) return envKey;

        return Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.Machine)
               ?? Environment.GetEnvironmentVariable("AI_API_KEY", EnvironmentVariableTarget.Machine)
               ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY", EnvironmentVariableTarget.Machine);
    }
}
