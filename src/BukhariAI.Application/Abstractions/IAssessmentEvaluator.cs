using BukhariAI.Application.Assessments;

namespace BukhariAI.Application.Abstractions;

/// <summary>
/// Evaluates a student's answer using Gemini AI against the original source text,
/// lesson explanation, question rubric, and relevant educational memory.
/// Must evaluate conceptual understanding, not keyword matching alone.
/// </summary>
public interface IAssessmentEvaluator
{
    Task<AssessmentEvaluationResult> EvaluateAnswerAsync(
        AssessmentEvaluationInput input,
        CancellationToken cancellationToken = default);
}
