namespace BukhariAI.Domain.Entities;

/// <summary>
/// A persisted key-value setting entry.
/// Keys follow the ASP.NET configuration path convention, e.g. "AI:AssessmentSystemPrompt".
/// </summary>
public sealed class AppSetting
{
    public int Id { get; set; }

    /// <summary>Unique setting key, e.g. "AI:AssessmentSystemPrompt".</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The stored value (text, may be large for system prompts).</summary>
    public string Value { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
