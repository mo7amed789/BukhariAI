using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Lessons.Biography;
using BukhariAI.Domain.Entities;
using BukhariAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BukhariAI.Infrastructure.AI;

public sealed class PersonBiographyService : IPersonBiographyService
{
    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;
    private readonly BukhariDbContext _dbContext;
    private readonly BiographyPromptBuilder _promptBuilder;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<PersonBiographyService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public PersonBiographyService(
        HttpClient httpClient,
        IOptions<AiOptions> options,
        BukhariDbContext dbContext,
        BiographyPromptBuilder promptBuilder,
        ISettingsService settingsService,
        ILogger<PersonBiographyService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _dbContext = dbContext;
        _promptBuilder = promptBuilder;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<PersonBiographyDto> GetPersonBiographyAsync(
        GetPersonBiographyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("اسم الشخصية أو الراوي مطلوب.", nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();

        string cleanName = request.Name.Trim();
        string normalizedQuery = NormalizeKey(cleanName);

        // 1. Check if cached in database
        var existingPerson = await _dbContext.People
            .FirstOrDefaultAsync(p => p.Name == cleanName || p.Name.Contains(cleanName) || cleanName.Contains(p.Name), cancellationToken);

        if (!request.ForceRefresh && existingPerson != null && !string.IsNullOrWhiteSpace(existingPerson.DetailedBiographyJson))
        {
            try
            {
                var cachedDto = JsonSerializer.Deserialize<PersonBiographyDto>(existingPerson.DetailedBiographyJson, JsonOpts);
                if (cachedDto != null && !string.IsNullOrWhiteSpace(cachedDto.SiyarBiography) && !IsPlaceholderFallback(cachedDto))
                {
                    cachedDto.PersonId = existingPerson.Id;
                    if (string.IsNullOrWhiteSpace(cachedDto.Name)) cachedDto.Name = existingPerson.Name;
                    _logger.LogInformation("Returning cached biography for person '{Name}' from database.", existingPerson.Name);
                    return cachedDto;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize cached biography for '{Name}'. Regenerating from AI.", cleanName);
            }
        }

        // 2. Build Prompts
        string systemPrompt = _promptBuilder.BuildSystemPrompt();
        string userPrompt = _promptBuilder.BuildUserPrompt(request);

        string? customInstructions = await _settingsService.GetAsync("AI:CustomResponseInstructions", cancellationToken);
        if (!string.IsNullOrWhiteSpace(customInstructions))
        {
            systemPrompt += $"\n\n==================================================\nتوجيهات إضافية خاصة بالمستخدم:\n{customInstructions}\n==================================================";
        }

        // 3. Call AI with fallback
        string? rawJson = null;
        try
        {
            rawJson = await CallPrimaryAiAsync(systemPrompt, userPrompt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Primary AI provider failed for biography generation of '{Name}'. Attempting fallback.", cleanName);
        }

        if (string.IsNullOrWhiteSpace(rawJson))
        {
            rawJson = await CallFallbackAiAsync(systemPrompt, userPrompt, cancellationToken);
        }

        PersonBiographyDto? resultDto = null;
        if (!string.IsNullOrWhiteSpace(rawJson))
        {
            string cleanedJson = CleanJsonFences(rawJson);
            try
            {
                resultDto = JsonSerializer.Deserialize<PersonBiographyDto>(cleanedJson, JsonOpts);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize AI biography JSON response for '{Name}'.", cleanName);
            }
        }

        // Fallback default DTO if parsing failed completely
        if (resultDto == null || string.IsNullOrWhiteSpace(resultDto.SiyarBiography))
        {
            resultDto = new PersonBiographyDto
            {
                Name = existingPerson?.Name ?? cleanName,
                Title = "عَلَم من الأعلام",
                Summary = existingPerson?.Description ?? request.ContextDescription ?? "عَلَم ورد ذكره في سياق الحديث والرواية.",
                SiyarBiography = $"ترجمة وسيرة {cleanName}: عَلَم ورجل من رجال الحديث والعلم، ورد ذكره في كتب السنة والآثار، ويُرجع في تفصيل مناقبه ومروياته إلى كتاب سير أعلام النبلاء للإمام الذهبي والمصادر المعتمدة.",
                Sources = ["سير أعلام النبلاء - للإمام الذهبي", "تهذيب الكمال - للحافظ المزي", "الإصابة في تمييز الصحابة - للحافظ ابن حجر"]
            };
        }

        if (string.IsNullOrWhiteSpace(resultDto.Name))
        {
            resultDto.Name = existingPerson?.Name ?? cleanName;
        }

        if (resultDto.Sources.Count == 0)
        {
            resultDto.Sources.Add("سير أعلام النبلاء - للإمام الذهبي");
            resultDto.Sources.Add("تهذيب الكمال في أسماء الرجال - للحافظ المزي");
        }

        // 4. Cache in Database only if we got a real biography (do not permanently cache placeholder fallbacks)
        bool isPlaceholder = IsPlaceholderFallback(resultDto);
        if (!isPlaceholder)
        {
            try
            {
                string serialized = JsonSerializer.Serialize(resultDto, JsonOpts);
                if (existingPerson != null)
                {
                    existingPerson.DetailedBiographyJson = serialized;
                    if (string.IsNullOrWhiteSpace(existingPerson.Description) && !string.IsNullOrWhiteSpace(resultDto.Summary))
                    {
                        existingPerson.Description = resultDto.Summary;
                    }
                }
                else
                {
                    existingPerson = new Person
                    {
                        Id = Guid.NewGuid(),
                        Name = resultDto.Name,
                        Description = !string.IsNullOrWhiteSpace(resultDto.Summary) ? resultDto.Summary : (request.ContextDescription ?? string.Empty),
                        DetailedBiographyJson = serialized,
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    _dbContext.People.Add(existingPerson);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                resultDto.PersonId = existingPerson.Id;
                _logger.LogInformation("Successfully cached detailed biography for '{Name}' in database.", resultDto.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not save cached biography for '{Name}' into database.", cleanName);
            }
        }
        else if (existingPerson != null)
        {
            resultDto.PersonId = existingPerson.Id;
        }

        return resultDto;
    }

    private static bool IsPlaceholderFallback(PersonBiographyDto? dto)
    {
        if (dto == null) return true;
        if (string.IsNullOrWhiteSpace(dto.SiyarBiography)) return true;
        if (dto.SiyarBiography.Contains("ويُرجع في تفصيل مناقبه ومروياته إلى كتاب سير أعلام النبلاء") &&
            (dto.Teachers == null || dto.Teachers.Count == 0) &&
            (dto.Students == null || dto.Students.Count == 0) &&
            (dto.ScholarlyPraise == null || dto.ScholarlyPraise.Count == 0))
        {
            return true;
        }
        return false;
    }

    private async Task<string?> CallPrimaryAiAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        string? apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("No primary API key configured for biography generation.");
            return null;
        }

        bool isGemini = string.Equals(_options.Provider, "Google", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(_options.Provider, "Gemini", StringComparison.OrdinalIgnoreCase);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(60));

        if (isGemini)
        {
            string rawModel = _options.Model;
            string model = string.IsNullOrWhiteSpace(rawModel) || rawModel.Contains("2.5") ? "gemini-3.6-flash" : rawModel;
            string endpoint = string.IsNullOrWhiteSpace(_options.Endpoint)
                ? $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent"
                : _options.Endpoint;

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

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Add("x-goog-api-key", apiKey);
            req.Content = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json");

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
            else
            {
                _logger.LogWarning("Gemini API returned status {Status}: {Body}", resp.StatusCode, body);
            }
        }
        else
        {
            string model = string.IsNullOrWhiteSpace(_options.Model) ? "gpt-4o" : _options.Model;
            string endpoint = string.IsNullOrWhiteSpace(_options.Endpoint)
                ? "https://api.openai.com/v1/chat/completions"
                : _options.Endpoint;

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

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json");

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
            else
            {
                _logger.LogWarning("OpenAI-compatible API returned status {Status}: {Body}", resp.StatusCode, body);
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

        if (string.IsNullOrWhiteSpace(fallbackKey))
        {
            _logger.LogWarning("No fallback API key configured for biography generation.");
            return null;
        }

        string fallbackModel = !string.IsNullOrWhiteSpace(dbFallbackModel)
            ? dbFallbackModel
            : (string.IsNullOrWhiteSpace(_options.FallbackModel) ? "claude-sonnet-4-5" : _options.FallbackModel);

        string fallbackEndpoint = string.IsNullOrWhiteSpace(_options.FallbackEndpoint)
            ? "https://conduit.ozdoev.net/v1/chat/completions"
            : _options.FallbackEndpoint;

        try
        {
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

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(60));

            using var req = new HttpRequestMessage(HttpMethod.Post, fallbackEndpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fallbackKey);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

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
            else
            {
                _logger.LogWarning("Conduit Fallback API call for biography failed with status {StatusCode}: {Body}", (int)resp.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallback AI provider also failed for biography generation.");
        }

        return null;
    }

    private string? ResolveApiKey()
    {
        string? key = _options.ApiKey;
        if (string.IsNullOrWhiteSpace(key))
            key = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
            key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
            key = Environment.GetEnvironmentVariable("OPENCODE_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
            key = Environment.GetEnvironmentVariable("CONDUIT_API_KEY");
        return key;
    }

    private static string CleanJsonFences(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[7..];
        else if (trimmed.StartsWith("```", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[3..];
        if (trimmed.EndsWith("```", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^3];
        return trimmed.Trim();
    }

    private static string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return string.Empty;
        var cleaned = Regex.Replace(key, @"[\u064B-\u065F\u0670]", "");
        cleaned = Regex.Replace(cleaned, @"[^\u0600-\u06FFa-zA-Z0-9\s]", "");
        return Regex.Replace(cleaned, @"\s+", " ").Trim().ToLowerInvariant();
    }
}
