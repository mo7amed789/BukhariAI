namespace BukhariAI.Infrastructure.AI;

/// <summary>
/// Provides resilient model resolution and multi-model fallback chains for Google Gemini APIs.
/// Automatically falls back to high-quota models (gemini-3.5-flash-lite / gemini-3.5-flash)
/// when a specific model hits free-tier per-model limits (429) or transient 503 demand spikes.
/// </summary>
public static class GeminiModelFallback
{
    private static readonly string[] FallbackOrder = new[]
    {
        "gemini-3.7-flash",
        "gemini-3.5-flash-lite",
        "gemini-3.5-flash",
        "gemini-3.6-flash",
        "gemini-3.8-flash",
        "gemini-flash-latest"
    };

    public static List<string> GetCandidateModels(string? configuredModel)
    {
        var candidates = new List<string>();
        string clean = (configuredModel ?? string.Empty).Trim();

        // If configured model is valid and not deprecated (2.5 / 1.5), prioritize it first
        if (!string.IsNullOrWhiteSpace(clean) && !clean.Contains("2.5") && !clean.Contains("1.5"))
        {
            candidates.Add(clean);
        }

        foreach (var m in FallbackOrder)
        {
            if (!candidates.Contains(m, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(m);
            }
        }

        return candidates;
    }
}
