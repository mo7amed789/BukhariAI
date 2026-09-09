using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Lessons.GenerateLesson;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BukhariAI.Infrastructure.AI;

public sealed class AiService : IAiService
{
    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;
    private readonly LessonPromptBuilder _promptBuilder;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<AiService> _logger;

    public AiService(
        HttpClient httpClient,
        IOptions<AiOptions> options,
        LessonPromptBuilder promptBuilder,
        ISettingsService settingsService,
        ILogger<AiService> logger)
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
        _logger.LogInformation("Invoking Vision AI provider '{Provider}' with {PageCount} page screenshots.", _options.Provider, pageScreenshots.Count);

        string? apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException($"No API key is configured for Vision provider '{_options.Provider}'.");
        }

        string defaultEndpoint = string.Equals(_options.Provider, "OpenCode", StringComparison.OrdinalIgnoreCase)
            ? "https://opencode.ai/zen/v1/chat/completions"
            : "https://api.openai.com/v1/chat/completions";

        string defaultModel = string.Equals(_options.Provider, "OpenCode", StringComparison.OrdinalIgnoreCase)
            ? "deepseek-v4-flash"
            : "gpt-4o";

        string endpoint = string.IsNullOrWhiteSpace(_options.Endpoint) ? defaultEndpoint : _options.Endpoint;
        string model = string.IsNullOrWhiteSpace(_options.Model) ? defaultModel : _options.Model;

        string? customInstructions = await _settingsService.GetAsync("AI:CustomResponseInstructions", cancellationToken);
        string systemPrompt = (await _settingsService.GetAsync("AI:LessonSystemPrompt", cancellationToken))
                              ?? _promptBuilder.BuildSystemPrompt();

        if (!string.IsNullOrWhiteSpace(customInstructions))
        {
            systemPrompt += $"\n\n==================================================\nتوجيهات وأسلوب الرد والشرح الخاصة بالمستخدم (User Custom Prompt):\n{customInstructions}\nيجب الالتزام التام بهذه التوجيهات في صياغة الشرح والتعليقات والفوائد الميسرة للمتعلم.\n==================================================";
        }

        string userPrompt = _promptBuilder.BuildVisionUserPrompt(pageScreenshots, context);
        var userContent = new List<object> { new { type = "text", text = userPrompt } };
        foreach (var page in pageScreenshots)
        {
            string dataUrl = $"data:{page.MediaType};base64,{Convert.ToBase64String(page.ImageBytes)}";
            userContent.Add(new { type = "image_url", image_url = new { url = dataUrl, detail = "high" } });
        }

        var requestPayload = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userContent }
            },
            temperature = 0.2,
            response_format = new { type = "json_object" }
        };

        string jsonPayload = JsonSerializer.Serialize(requestPayload);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await SendWithRetryAsync(request, cancellationToken);
            string responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Vision provider '{_options.Provider}' returned {(int)response.StatusCode}: {responseContent}");

            using var doc = JsonDocument.Parse(responseContent);
            string? messageContent = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            var validationResult = AiResponseValidator.ValidateAndDeserialize(messageContent);
            if (!validationResult.IsValid || validationResult.Value is null)
            {
                throw new InvalidOperationException($"Vision model returned an invalid lesson response: {validationResult.ErrorMessage}");
            }

            return validationResult.Value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vision request to AI provider '{Provider}' failed. Attempting Conduit fallback.", _options.Provider);

            string? dbFallbackKey = await _settingsService.GetAsync("AI:FallbackApiKey", cancellationToken);
            string? dbFallbackModel = await _settingsService.GetAsync("AI:FallbackModel", cancellationToken);

            string? fallbackKey = !string.IsNullOrWhiteSpace(dbFallbackKey)
                ? dbFallbackKey
                : (string.IsNullOrWhiteSpace(_options.FallbackApiKey)
                    ? (Environment.GetEnvironmentVariable("CONDUIT_API_KEY", EnvironmentVariableTarget.Process)
                       ?? Environment.GetEnvironmentVariable("CONDUIT_API_KEY", EnvironmentVariableTarget.User)
                       ?? Environment.GetEnvironmentVariable("CONDUIT_API_KEY", EnvironmentVariableTarget.Machine))
                    : _options.FallbackApiKey);

            if (!string.IsNullOrWhiteSpace(fallbackKey) && !string.Equals(endpoint, _options.FallbackEndpoint, StringComparison.OrdinalIgnoreCase))
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
                            new { role = "user",   content = userContent }
                        },
                        temperature = 0.2,
                        response_format = new { type = "json_object" }
                    };

                    using var fallbackReq = new HttpRequestMessage(HttpMethod.Post, _options.FallbackEndpoint);
                    fallbackReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fallbackKey);
                    fallbackReq.Content = new StringContent(JsonSerializer.Serialize(fallbackPayload), Encoding.UTF8, "application/json");

                    using var fallbackResp = await _httpClient.SendAsync(fallbackReq, cancellationToken);
                    string fallbackContent = await fallbackResp.Content.ReadAsStringAsync(cancellationToken);

                    if (fallbackResp.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(fallbackContent);
                        string? messageContent = doc.RootElement
                            .GetProperty("choices")[0]
                            .GetProperty("message")
                            .GetProperty("content")
                            .GetString();

                        var validationResult = AiResponseValidator.ValidateAndDeserialize(messageContent);
                        if (validationResult.IsValid && validationResult.Value is not null)
                        {
                            _logger.LogInformation("Conduit fallback succeeded for lesson generation in AiService.");
                            return validationResult.Value;
                        }
                    }
                }
                catch (Exception fbEx)
                {
                    _logger.LogError(fbEx, "Conduit fallback also failed in AiService.");
                }
            }

            throw;
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
            string keyName = string.Equals(_options.Provider, "OpenCode", StringComparison.OrdinalIgnoreCase) ? "OPENCODE_API_KEY" : "OPENAI_API_KEY";
            throw new InvalidOperationException($"API key is not configured for provider '{_options.Provider}'. Set {keyName} or AI:ApiKey.");
        }

        string defaultEndpoint = string.Equals(_options.Provider, "OpenCode", StringComparison.OrdinalIgnoreCase)
            ? "https://opencode.ai/zen/v1/chat/completions"
            : "https://api.openai.com/v1/chat/completions";

        string defaultModel = string.Equals(_options.Provider, "OpenCode", StringComparison.OrdinalIgnoreCase)
            ? "deepseek-v4-flash"
            : "gpt-4o";

        string endpoint = string.IsNullOrWhiteSpace(_options.Endpoint) ? defaultEndpoint : _options.Endpoint;
        string model = string.IsNullOrWhiteSpace(_options.Model) ? defaultModel : _options.Model;

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
            model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt }
            },
            temperature = 0.2,
            response_format = new { type = "json_object" }
        };

        string jsonPayload = JsonSerializer.Serialize(requestPayload);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await SendWithRetryAsync(request, cancellationToken);
            string responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Provider '{_options.Provider}' returned {(int)response.StatusCode}: {responseContent}");

            using var doc = JsonDocument.Parse(responseContent);
            string? messageContent = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            var validationResult = AiResponseValidator.ValidateAndDeserialize(messageContent);
            if (!validationResult.IsValid || validationResult.Value is null)
            {
                throw new InvalidOperationException($"Model returned an invalid lesson response: {validationResult.ErrorMessage}");
            }

            return validationResult.Value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Text request to AI provider '{Provider}' failed. Attempting Conduit fallback.", _options.Provider);

            string? dbFallbackKey = await _settingsService.GetAsync("AI:FallbackApiKey", cancellationToken);
            string? dbFallbackModel = await _settingsService.GetAsync("AI:FallbackModel", cancellationToken);

            string? fallbackKey = !string.IsNullOrWhiteSpace(dbFallbackKey)
                ? dbFallbackKey
                : (string.IsNullOrWhiteSpace(_options.FallbackApiKey)
                    ? (Environment.GetEnvironmentVariable("CONDUIT_API_KEY", EnvironmentVariableTarget.Process)
                       ?? Environment.GetEnvironmentVariable("CONDUIT_API_KEY", EnvironmentVariableTarget.User)
                       ?? Environment.GetEnvironmentVariable("CONDUIT_API_KEY", EnvironmentVariableTarget.Machine))
                    : _options.FallbackApiKey);

            if (!string.IsNullOrWhiteSpace(fallbackKey) && !string.Equals(endpoint, _options.FallbackEndpoint, StringComparison.OrdinalIgnoreCase))
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
                    fallbackReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fallbackKey);
                    fallbackReq.Content = new StringContent(JsonSerializer.Serialize(fallbackPayload), Encoding.UTF8, "application/json");

                    using var fallbackResp = await _httpClient.SendAsync(fallbackReq, cancellationToken);
                    string fallbackContent = await fallbackResp.Content.ReadAsStringAsync(cancellationToken);

                    if (fallbackResp.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(fallbackContent);
                        string? messageContent = doc.RootElement
                            .GetProperty("choices")[0]
                            .GetProperty("message")
                            .GetProperty("content")
                            .GetString();

                        var validationResult = AiResponseValidator.ValidateAndDeserialize(messageContent);
                        if (validationResult.IsValid && validationResult.Value is not null)
                        {
                            _logger.LogInformation("Conduit fallback succeeded for text lesson generation in AiService.");
                            return validationResult.Value;
                        }
                    }
                }
                catch (Exception fbEx)
                {
                    _logger.LogError(fbEx, "Conduit fallback also failed in AiService text generation.");
                }
            }

            throw;
        }
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        const int maxAttempts = 4;
        HttpRequestException? lastTransportException = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            // HttpRequestMessage instances (including their content) cannot be sent more than once.
            using var retryRequest = await CloneRequestAsync(request, cancellationToken);
            try
            {
                HttpResponseMessage response = await _httpClient.SendAsync(retryRequest, cancellationToken);
                if (!IsTransientStatusCode(response.StatusCode) || attempt == maxAttempts)
                    return response;

                _logger.LogWarning(
                    "Vision provider '{Provider}' returned transient status {StatusCode} on attempt {Attempt}/{MaxAttempts}. Retrying.",
                    _options.Provider, (int)response.StatusCode, attempt, maxAttempts);
                response.Dispose();
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                lastTransportException = ex;
                _logger.LogWarning(ex,
                    "Transient transport error contacting Vision provider '{Provider}' on attempt {Attempt}/{MaxAttempts}. Retrying.",
                    _options.Provider, attempt, maxAttempts);
            }

            int delayMs = (int)(attempt * 2500);
            await Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
        }

        throw lastTransportException ?? new HttpRequestException("Vision request failed without a response.");
    }

    private static bool IsTransientStatusCode(System.Net.HttpStatusCode statusCode) =>
        statusCode == System.Net.HttpStatusCode.RequestTimeout ||
        (int)statusCode == 429 ||
        (int)statusCode >= 500;

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (request.Content is not null)
        {
            byte[] content = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(content);
            foreach (var header in request.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }

    private string? ResolveApiKey()
    {
        // 1. Configuration option
        if (!string.IsNullOrWhiteSpace(_options.ApiKey)) return _options.ApiKey;

        string providerKeyName = string.Equals(_options.Provider, "OpenCode", StringComparison.OrdinalIgnoreCase)
            ? "OPENCODE_API_KEY"
            : "OPENAI_API_KEY";

        // 2. Process environment variable. Gemini keys must not be sent to OpenAI-compatible providers.
        string? envKey = Environment.GetEnvironmentVariable(providerKeyName, EnvironmentVariableTarget.Process)
                         ?? Environment.GetEnvironmentVariable("AI_API_KEY", EnvironmentVariableTarget.Process);
        if (!string.IsNullOrWhiteSpace(envKey)) return envKey;

        // 3. User environment variable
        envKey = Environment.GetEnvironmentVariable(providerKeyName, EnvironmentVariableTarget.User)
                 ?? Environment.GetEnvironmentVariable("AI_API_KEY", EnvironmentVariableTarget.User);
        if (!string.IsNullOrWhiteSpace(envKey)) return envKey;

        // 4. Machine environment variable
        envKey = Environment.GetEnvironmentVariable(providerKeyName, EnvironmentVariableTarget.Machine)
                 ?? Environment.GetEnvironmentVariable("AI_API_KEY", EnvironmentVariableTarget.Machine);
        if (!string.IsNullOrWhiteSpace(envKey)) return envKey;

        return null;
    }
}
