namespace BukhariAI.Infrastructure.AI;

/// <summary>
/// Exposes the built-in default system prompt for assessment evaluation,
/// so it can be referenced without instantiating the evaluator.
/// </summary>
public static class AiAssessmentDefaults
{
    /// <summary>Returns the built-in Arabic system prompt for assessment evaluation.</summary>
    public static string GetDefaultSystemPrompt() => AiAssessmentEvaluator.GetBuiltInSystemPrompt();
}