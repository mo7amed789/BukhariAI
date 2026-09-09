using BukhariAI.Application.Quran;

namespace BukhariAI.Application.Abstractions;

public interface IQuranAnalysisService
{
    Task<QuranSurahAnalysisResponse> AnalyzeSurahAsync(
        AnalyzeQuranRequest request,
        CancellationToken cancellationToken = default);
}
