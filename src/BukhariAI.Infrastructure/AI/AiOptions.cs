namespace BukhariAI.Infrastructure.AI;

public sealed class AiOptions
{
    public const string SectionName = "AI";

    public string Provider { get; set; } = "Gemini";

    public string? ApiKey { get; set; }

    public string Model { get; set; } = "gemini-3.6-flash";

    public string? Endpoint { get; set; }

    // ── Conduit Fallback ────────────────────────────────────────────────────
    // When the primary provider fails after all retries the system
    // automatically retries via this OpenAI-compatible endpoint.

    /// <summary>API key for the fallback Conduit proxy (or any OpenAI-compatible service).</summary>
    public string? FallbackApiKey { get; set; }

    /// <summary>Full chat/completions endpoint for the fallback provider. Defaults to Conduit.</summary>
    public string FallbackEndpoint { get; set; } = "https://conduit.ozdoev.net/v1/chat/completions";

    /// <summary>Model name to use when calling the fallback provider.</summary>
    public string FallbackModel { get; set; } = "claude-sonnet-4-5";
}
