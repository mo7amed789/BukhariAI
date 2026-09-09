using System.Text;
using System.Text.Json;
using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Lessons.GenerateLesson;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BukhariAI.Infrastructure.AI;

public sealed class GeminiAiService : IAiService
{
    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;
    private readonly LessonPromptBuilder _promptBuilder;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<GeminiAiService> _logger;

    public GeminiAiService(
        HttpClient httpClient,
        IOptions<AiOptions> options,
        LessonPromptBuilder promptBuilder,
        ISettingsService settingsService,
        ILogger<GeminiAiService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _promptBuilder = promptBuilder;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<GenerateLessonResponse> GenerateLessonAsync(
        IReadOnlyList<PageScreenshot> pageScreenshots,
        LessonLearningContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pageScreenshots);
        if (pageScreenshots.Count == 0) throw new ArgumentException("At least one page screenshot is required.", nameof(pageScreenshots));
        cancellationToken.ThrowIfCancellationRequested();

        string? apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("GEMINI_API_KEY is not configured for Vision generation.");
        }

        string rawModel = _options.Model;
        var candidateModels = GeminiModelFallback.GetCandidateModels(rawModel);

        string? customInstructions = await _settingsService.GetAsync("AI:CustomResponseInstructions", cancellationToken);
        string systemPrompt = (await _settingsService.GetAsync("AI:LessonSystemPrompt", cancellationToken))
                              ?? _promptBuilder.BuildSystemPrompt();

        if (!string.IsNullOrWhiteSpace(customInstructions))
        {
            systemPrompt += $"\n\n==================================================\nتوجيهات وأسلوب الرد والشرح الخاصة بالمستخدم (User Custom Prompt):\n{customInstructions}\nيجب الالتزام التام بهذه التوجيهات في صياغة الشرح والتعليقات والفوائد الميسرة للمتعلم.\n==================================================";
        }

        string userPrompt = _promptBuilder.BuildVisionUserPrompt(pageScreenshots, context);
        var parts = new List<object> { new { text = userPrompt } };
        foreach (var page in pageScreenshots)
            parts.Add(new { inline_data = new { mime_type = page.MediaType, data = Convert.ToBase64String(page.ImageBytes) } });

        var requestPayload = new
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
                    parts
                }
            },
            generationConfig = new
            {
                response_mime_type = "application/json",
                temperature = 0.2
            }
        };

        string jsonPayload = JsonSerializer.Serialize(requestPayload);

        HttpResponseMessage? response = null;
        string responseBody = string.Empty;

        foreach (var model in candidateModels)
        {
            string endpoint = string.IsNullOrWhiteSpace(_options.Endpoint)
                ? $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}"
                : _options.Endpoint;

            _logger.LogInformation("Sending {PageCount} page screenshots to Gemini Vision model '{Model}' (HasContext: {HasContext}).", pageScreenshots.Count, model, context is not null);

            for (int attempt = 1; attempt <= 4; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Add("x-goog-api-key", apiKey);
                request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                try
                {
                    response = await _httpClient.SendAsync(request, cancellationToken);
                    responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                    if (response.IsSuccessStatusCode)
                    {
                        break;
                    }

                    // If rate limited or 5xx server error, retry with smart backoff
                    if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                    {
                        int backoffSeconds = attempt switch
                        {
                            1 => 3,
                            2 => 6,
                            3 => 10,
                            _ => 15
                        };
                        _logger.LogWarning("Gemini model '{Model}' returned retryable status {StatusCode} on attempt {Attempt}. Waiting {Delay}s before retry...", model, (int)response.StatusCode, attempt, backoffSeconds);
                        await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), cancellationToken);
                        continue;
                    }

                    _logger.LogWarning("Gemini Vision API model '{Model}' returned error status {StatusCode}: {ResponseBody}.", model, (int)response.StatusCode, responseBody);
                    break;
                }
                catch (Exception ex) when (attempt < 4 && (ex is HttpRequestException || ex is IOException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)))
                {
                    int delay = attempt * 2;
                    _logger.LogWarning(ex, "Transient transport error on attempt {Attempt} for model '{Model}'. Waiting {Delay}s before retry...", attempt, model, delay);
                    await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
                }
            }

            if (response is not null && response.IsSuccessStatusCode)
            {
                break;
            }
        }

        if (response is null || !response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Gemini Vision request was not successful (Status: {StatusCode}). Attempting Conduit fallback.",
                response?.StatusCode);

            // ── Conduit / OpenAI-compatible vision fallback ──────────────────
            string? dbFallbackKey = await _settingsService.GetAsync("AI:FallbackApiKey", cancellationToken);
            string? dbFallbackModel = await _settingsService.GetAsync("AI:FallbackModel", cancellationToken);

            string? fallbackKey = !string.IsNullOrWhiteSpace(dbFallbackKey)
                ? dbFallbackKey
                : (string.IsNullOrWhiteSpace(_options.FallbackApiKey)
                    ? (Environment.GetEnvironmentVariable("CONDUIT_API_KEY", EnvironmentVariableTarget.Process)
                       ?? Environment.GetEnvironmentVariable("CONDUIT_API_KEY", EnvironmentVariableTarget.User)
                       ?? Environment.GetEnvironmentVariable("CONDUIT_API_KEY", EnvironmentVariableTarget.Machine))
                    : _options.FallbackApiKey);

            if (!string.IsNullOrWhiteSpace(fallbackKey))
            {
                try
                {
                    string fallbackModel = !string.IsNullOrWhiteSpace(dbFallbackModel)
                        ? dbFallbackModel
                        : (string.IsNullOrWhiteSpace(_options.FallbackModel) ? "claude-sonnet-4-5" : _options.FallbackModel);

                    var userContent = new List<object> { new { type = "text", text = userPrompt } };
                    foreach (var page in pageScreenshots)
                    {
                        string dataUrl = $"data:{page.MediaType};base64,{Convert.ToBase64String(page.ImageBytes)}";
                        userContent.Add(new { type = "image_url", image_url = new { url = dataUrl, detail = "high" } });
                    }

                    var fallbackPayload = new
                    {
                        model = fallbackModel,
                        messages = new object[]
                        {
                            new { role = "system", content = systemPrompt },
                            new { role = "user",   content = userContent }
                        },
                        temperature = 0.2,
                        response_format = new { type = "json_object" }
                    };

                    using var fallbackReq = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, _options.FallbackEndpoint);
                    fallbackReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", fallbackKey);
                    fallbackReq.Content = new System.Net.Http.StringContent(
                        System.Text.Json.JsonSerializer.Serialize(fallbackPayload), System.Text.Encoding.UTF8, "application/json");

                    using var fallbackResponse = await _httpClient.SendAsync(fallbackReq, cancellationToken);
                    string fallbackBody = await fallbackResponse.Content.ReadAsStringAsync(cancellationToken);

                    if (fallbackResponse.IsSuccessStatusCode)
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(fallbackBody);
                        string? messageContent = doc.RootElement
                            .GetProperty("choices")[0]
                            .GetProperty("message")
                            .GetProperty("content")
                            .GetString();

                        var validationResult = AiResponseValidator.ValidateAndDeserialize(messageContent);
                        if (validationResult.IsValid && validationResult.Value is not null)
                        {
                            _logger.LogInformation("Conduit fallback succeeded for lesson generation.");
                            return validationResult.Value;
                        }
                    }
                    _logger.LogWarning("Conduit fallback returned non-success or invalid response ({StatusCode}).",
                        (int)fallbackResponse.StatusCode);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Conduit fallback also failed for lesson generation.");
                }
            }

            string detailedError = response is not null
                ? $"Gemini Vision error (HTTP {(int)response.StatusCode}): {responseBody}"
                : "Gemini Vision request returned null or timed out.";
            throw new InvalidOperationException(detailedError);
        }

        _logger.LogInformation("Received successful HTTP 200 response from Gemini API ({Bytes} bytes).", responseBody.Length);

        try
        {
            string rawJsonText = ExtractTextFromGeminiResponse(responseBody);

            var validationResult = AiResponseValidator.ValidateAndDeserialize(rawJsonText);
            if (!validationResult.IsValid || validationResult.Value is null)
            {
                throw new InvalidOperationException($"Gemini Vision returned an invalid lesson response: {validationResult.ErrorMessage}");
            }

            _logger.LogInformation(
                "Successfully validated and parsed lesson from Gemini: '{Title}' ({HadithsCount} hadiths, {QuestionsCount} review questions).",
                validationResult.Value.Title,
                validationResult.Value.Hadiths.Count,
                validationResult.Value.ReviewQuestions.Count);

            return validationResult.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Gemini Vision response.");
            throw new InvalidOperationException("Gemini Vision returned an unreadable response.", ex);
        }
    }

    public async Task<GenerateLessonResponse> GenerateLessonFromTextAsync(
        string sourceText,
        LessonLearningContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceText);
        cancellationToken.ThrowIfCancellationRequested();

        string? apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("GEMINI_API_KEY is not configured.");
        }

        string rawModel = _options.Model;
        var candidateModels = GeminiModelFallback.GetCandidateModels(rawModel);

        string? customInstructions = await _settingsService.GetAsync("AI:CustomResponseInstructions", cancellationToken);
        string systemPrompt = (await _settingsService.GetAsync("AI:LessonSystemPrompt", cancellationToken))
                              ?? _promptBuilder.BuildSystemPrompt();

        if (!string.IsNullOrWhiteSpace(customInstructions))
        {
            systemPrompt += $"\n\n==================================================\nتوجيهات وأسلوب الرد والشرح الخاصة بالمستخدم (User Custom Prompt):\n{customInstructions}\nيجب الالتزام التام بهذه التوجيهات في صياغة الشرح والتعليقات والفوائد الميسرة للمتعلم.\n==================================================";
        }

        string userPrompt = _promptBuilder.BuildUserPrompt(sourceText, context);

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

        string jsonPayload = JsonSerializer.Serialize(requestPayload);
        HttpResponseMessage? response = null;
        string responseBody = string.Empty;

        foreach (var model in candidateModels)
        {
            string endpoint = string.IsNullOrWhiteSpace(_options.Endpoint)
                ? $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}"
                : _options.Endpoint;

            _logger.LogInformation("Sending source text ({Length} chars) to Gemini model '{Model}'.", sourceText.Length, model);

            for (int attempt = 1; attempt <= 4; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                    request.Headers.Add("x-goog-api-key", apiKey);
                    request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    response = await _httpClient.SendAsync(request, cancellationToken);
                    responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                    if (response.IsSuccessStatusCode) break;

                    if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                    {
                        int backoffSeconds = attempt switch
                        {
                            1 => 3,
                            2 => 6,
                            3 => 10,
                            _ => 15
                        };
                        _logger.LogWarning("Gemini text request attempt {Attempt} with model '{Model}' returned retryable status {StatusCode}. Waiting {Delay}s before retry...", attempt, model, (int)response.StatusCode, backoffSeconds);
                        await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), cancellationToken);
                        continue;
                    }
                    break;
                }
                catch (Exception ex) when (attempt < 4 && (ex is HttpRequestException || ex is IOException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)))
                {
                    int delay = attempt * 2;
                    _logger.LogWarning(ex, "Gemini text request transport error on attempt {Attempt} for model '{Model}'. Waiting {Delay}s before retry...", attempt, model, delay);
                    await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
                }
            }

            if (response is not null && response.IsSuccessStatusCode)
            {
                break;
            }
        }

        if (response is null || !response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Gemini text request failed. Attempting Conduit fallback.");

            string? dbFallbackKey = await _settingsService.GetAsync("AI:FallbackApiKey", cancellationToken);
            string? dbFallbackModel = await _settingsService.GetAsync("AI:FallbackModel", cancellationToken);

            string? fallbackKey = !string.IsNullOrWhiteSpace(dbFallbackKey)
                ? dbFallbackKey
                : (string.IsNullOrWhiteSpace(_options.FallbackApiKey)
                    ? (Environment.GetEnvironmentVariable("CONDUIT_API_KEY", EnvironmentVariableTarget.Process)
                       ?? Environment.GetEnvironmentVariable("CONDUIT_API_KEY", EnvironmentVariableTarget.User)
                       ?? Environment.GetEnvironmentVariable("CONDUIT_API_KEY", EnvironmentVariableTarget.Machine))
                    : _options.FallbackApiKey);

            if (!string.IsNullOrWhiteSpace(fallbackKey))
            {
                try
                {
                    string fallbackModel = !string.IsNullOrWhiteSpace(dbFallbackModel)
                        ? dbFallbackModel
                        : (string.IsNullOrWhiteSpace(_options.FallbackModel) ? "claude-sonnet-4-5" : _options.FallbackModel);

                    var fallbackPayload = new
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

                    using var fallbackReq = new HttpRequestMessage(HttpMethod.Post, _options.FallbackEndpoint);
                    fallbackReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", fallbackKey);
                    fallbackReq.Content = new StringContent(JsonSerializer.Serialize(fallbackPayload), Encoding.UTF8, "application/json");

                    using var fallbackResponse = await _httpClient.SendAsync(fallbackReq, cancellationToken);
                    string fallbackBody = await fallbackResponse.Content.ReadAsStringAsync(cancellationToken);

                    if (fallbackResponse.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(fallbackBody);
                        string? messageContent = doc.RootElement
                            .GetProperty("choices")[0]
                            .GetProperty("message")
                            .GetProperty("content")
                            .GetString();

                        var validationResult = AiResponseValidator.ValidateAndDeserialize(messageContent);
                        if (validationResult.IsValid && validationResult.Value is not null)
                        {
                            _logger.LogInformation("Conduit fallback succeeded for text lesson generation.");
                            return validationResult.Value;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Conduit fallback failed for text lesson generation.");
                }
            }

            throw new InvalidOperationException("AI service could not process the provided text.");
        }

        try
        {
            string rawJsonText = ExtractTextFromGeminiResponse(responseBody);
            var validationResult = AiResponseValidator.ValidateAndDeserialize(rawJsonText);
            if (!validationResult.IsValid || validationResult.Value is null)
            {
                throw new InvalidOperationException($"Gemini returned an invalid lesson response: {validationResult.ErrorMessage}");
            }
            return validationResult.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Gemini text response.");
            throw new InvalidOperationException("Gemini returned an unreadable response.", ex);
        }
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

        if (candidate.TryGetProperty("finishReason", out var finishReason))
        {
            string? reason = finishReason.GetString();
            if (reason is not null && reason is not "STOP" and not "MAX_TOKENS")
            {
                throw new InvalidOperationException($"Gemini generation terminated prematurely with reason: '{reason}'.");
            }
        }

        if (!candidate.TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) ||
            parts.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Gemini candidate content or parts were missing.");
        }

        string? text = parts[0].GetProperty("text").GetString();

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("Gemini returned empty text in candidate content.");
        }

        return text;
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
}
