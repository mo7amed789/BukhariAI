using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Lessons.GenerateLesson;
using BukhariAI.Infrastructure.AI;
using Microsoft.AspNetCore.Mvc;

namespace BukhariAI.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class SettingsController : ControllerBase
{
    // Keys the frontend is allowed to read and write.
    private static readonly HashSet<string> AllowedKeys =
    [
        "AI:CustomResponseInstructions",
        "AI:AssessmentSystemPrompt",
        "AI:LessonSystemPrompt",
        "AI:FallbackApiKey",
        "AI:FallbackModel",
    ];

    private readonly ISettingsService _settings;
    private readonly LessonPromptBuilder _promptBuilder;
    private readonly AiOptions _aiOptions;

    public SettingsController(
        ISettingsService settings,
        LessonPromptBuilder promptBuilder,
        Microsoft.Extensions.Options.IOptions<AiOptions> aiOptions)
    {
        _settings = settings;
        _promptBuilder = promptBuilder;
        _aiOptions = aiOptions.Value;
    }

    /// <summary>Returns all user-configurable settings. Default values are provided when not yet saved.</summary>
    [HttpGet]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken)
    {
        var stored = await _settings.GetAllAsync(cancellationToken);

        var rawFallbackKey = stored.GetValueOrDefault("AI:FallbackApiKey") 
                             ?? _aiOptions.FallbackApiKey 
                             ?? string.Empty;

        var result = new Dictionary<string, string>
        {
            ["AI:CustomResponseInstructions"] = stored.GetValueOrDefault("AI:CustomResponseInstructions") ?? string.Empty,
            ["AI:AssessmentSystemPrompt"]     = stored.GetValueOrDefault("AI:AssessmentSystemPrompt")
                                                 ?? AiAssessmentDefaults.GetDefaultSystemPrompt(),
            ["AI:LessonSystemPrompt"]         = stored.GetValueOrDefault("AI:LessonSystemPrompt")
                                                 ?? _promptBuilder.BuildSystemPrompt(),
            ["AI:FallbackApiKey"]             = MaskSecret(rawFallbackKey),
            ["AI:FallbackModel"]              = stored.GetValueOrDefault("AI:FallbackModel") 
                                                 ?? _aiOptions.FallbackModel 
                                                 ?? "claude-sonnet-4-5",
        };

        return Ok(result);
    }

    private static string MaskSecret(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) return string.Empty;
        if (secret.Length <= 8) return "********";
        return $"{secret[..3]}****{secret[^4..]}";
    }

    /// <summary>Saves one or more settings values. Only allowed keys are accepted.</summary>
    [HttpPut]
    public async Task<IActionResult> SaveSettings([FromBody] Dictionary<string, string> body,
        CancellationToken cancellationToken)
    {
        if (body is null || body.Count == 0)
            return BadRequest(new { error = "No settings provided." });

        var filtered = body
            .Where(kv => AllowedKeys.Contains(kv.Key))
            .Where(kv => !(kv.Key == "AI:FallbackApiKey" && kv.Value.Contains("****")))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        if (filtered.Count == 0)
            return BadRequest(new { error = "None of the provided keys are allowed." });

        await _settings.SaveAsync(filtered, cancellationToken);
        return Ok(new { saved = filtered.Count });
    }
}