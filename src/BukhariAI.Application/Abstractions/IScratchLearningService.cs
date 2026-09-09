using BukhariAI.Application.ScratchLearning;

namespace BukhariAI.Application.Abstractions;

public interface IScratchLearningService
{
    /// <summary>
    /// Returns the curated pre-defined foundational learning tracks.
    /// </summary>
    IReadOnlyList<CuratedScratchTrack> GetCuratedTracks();

    /// <summary>
    /// Generates or retrieves a full multi-level learning roadmap for a topic.
    /// </summary>
    Task<ScratchRoadmapResponse> GenerateRoadmapAsync(
        ScratchRoadmapRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates or retrieves a deep step-by-step explanation with analogies and quiz for a specific milestone.
    /// </summary>
    Task<ScratchStepExplanation> ExplainStepAsync(
        ExplainScratchStepRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers a question as a friendly, ELI5-focused foundational tutor.
    /// </summary>
    Task<AskScratchTutorResponse> AskTutorAsync(
        AskScratchTutorRequest request,
        CancellationToken cancellationToken = default);
}
