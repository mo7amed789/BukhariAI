using BukhariAI.Application.Abstractions;
using BukhariAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BukhariAI.Infrastructure.Persistence;

/// <summary>
/// Self-healing background service that scans and sanitizes existing concept records,
/// cleaning or removing broken mid-sentence extractions and restoring high-quality scholarly concept titles.
/// </summary>
public static class ConceptCleanupService
{
    public static async Task CleanupBrokenConceptsAsync(
        BukhariDbContext dbContext,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger?.LogInformation("Running database ConceptCleanupService to sanitize corrupted concept titles...");

            // 1. Fetch Lessons with Hadiths for fallback title derivation
            var lessons = await dbContext.Lessons.AsNoTracking()
                .Include(l => l.Hadiths)
                .ToListAsync(cancellationToken);
            var lessonMap = lessons.ToDictionary(l => l.Id);

            // 2. Clean StudentConceptMasteries
            var masteries = await dbContext.StudentConceptMasteries
                .ToListAsync(cancellationToken);

            var masteriesByBook = masteries.GroupBy(m => m.BookId).ToList();

            foreach (var bookGroup in masteriesByBook)
            {
                var bookId = bookGroup.Key;
                var existingKeys = new Dictionary<string, StudentConceptMastery>(StringComparer.OrdinalIgnoreCase);

                // First pass: register already valid concepts
                var items = bookGroup.ToList();
                foreach (var m in items)
                {
                    string? validClean = ConceptSanitizer.CleanAndValidate(m.ConceptKey);
                    if (validClean != null && validClean == m.ConceptKey)
                    {
                        string norm = NormalizeKey(validClean);
                        if (!existingKeys.ContainsKey(norm))
                        {
                            existingKeys[norm] = m;
                        }
                    }
                }

                // Second pass: clean, salvage, or remove broken ones
                foreach (var m in items)
                {
                    string? validClean = ConceptSanitizer.CleanAndValidate(m.ConceptKey);

                    if (validClean != null)
                    {
                        // Cleaned string was valid
                        m.ConceptKey = validClean;
                        string norm = NormalizeKey(validClean);
                        if (existingKeys.TryGetValue(norm, out var existing) && existing.Id != m.Id)
                        {
                            // Merge into existing
                            existing.ExposureCount += m.ExposureCount;
                            existing.AssessmentCount += m.AssessmentCount;
                            existing.CorrectAnswerCount += m.CorrectAnswerCount;
                            existing.MasteryScore = Math.Max(existing.MasteryScore, m.MasteryScore);
                            existing.LearningLevel = (LearningLevel)Math.Max((int)existing.LearningLevel, (int)m.LearningLevel);
                            dbContext.StudentConceptMasteries.Remove(m);
                        }
                        else
                        {
                            existingKeys[norm] = m;
                        }
                    }
                    else
                    {
                        // Concept is invalid / broken sentence fragment
                        logger?.LogWarning("Found corrupted concept record '{CorruptedKey}' (ID {Id}). Salvaging...", m.ConceptKey, m.Id);

                        Guid? lessonId = m.LastAssessedLessonId ?? m.FirstIntroducedLessonId;
                        Lesson? linkedLesson = lessonId.HasValue && lessonMap.TryGetValue(lessonId.Value, out var l) ? l : null;

                        string? salvagedTitle = null;
                        if (linkedLesson != null && linkedLesson.Hadiths.Count > 0)
                        {
                            // Find hadith problem or conclusion that matches partial text, or use first problem
                            var matchingHadith = linkedLesson.Hadiths.FirstOrDefault(h =>
                                (!string.IsNullOrWhiteSpace(h.Problem) && m.ConceptKey.Contains(h.Problem[..Math.Min(10, h.Problem.Length)])) ||
                                (!string.IsNullOrWhiteSpace(h.EasyExplanation) && h.EasyExplanation.Contains(m.ConceptKey[..Math.Min(10, m.ConceptKey.Length)])))
                                ?? linkedLesson.Hadiths.FirstOrDefault();

                            if (matchingHadith != null && !string.IsNullOrWhiteSpace(matchingHadith.Problem))
                            {
                                salvagedTitle = ConceptSanitizer.DeriveTopicTitle(matchingHadith.Problem);
                            }
                            else if (!string.IsNullOrWhiteSpace(linkedLesson.Title))
                            {
                                salvagedTitle = ConceptSanitizer.DeriveTopicTitle(linkedLesson.Title);
                            }
                        }

                        if (salvagedTitle != null && ConceptSanitizer.IsValidConcept(salvagedTitle))
                        {
                            string norm = NormalizeKey(salvagedTitle);
                            if (existingKeys.TryGetValue(norm, out var existing) && existing.Id != m.Id)
                            {
                                existing.ExposureCount += m.ExposureCount;
                                existing.AssessmentCount += m.AssessmentCount;
                                existing.CorrectAnswerCount += m.CorrectAnswerCount;
                                existing.MasteryScore = Math.Max(existing.MasteryScore, m.MasteryScore);
                                existing.LearningLevel = (LearningLevel)Math.Max((int)existing.LearningLevel, (int)m.LearningLevel);
                                dbContext.StudentConceptMasteries.Remove(m);
                            }
                            else
                            {
                                m.ConceptKey = salvagedTitle;
                                existingKeys[norm] = m;
                            }
                        }
                        else
                        {
                            // Unsalvageable corrupt entry -> remove
                            dbContext.StudentConceptMasteries.Remove(m);
                        }
                    }
                }
            }

            // 3. Clean KnownTerms
            var knownTerms = await dbContext.KnownTerms.ToListAsync(cancellationToken);
            foreach (var kt in knownTerms)
            {
                string? cleanTerm = ConceptSanitizer.CleanAndValidate(kt.Term);
                if (cleanTerm == null)
                {
                    dbContext.KnownTerms.Remove(kt);
                }
                else
                {
                    kt.Term = cleanTerm;
                }
            }

            // 4. Clean ReviewRecommendations
            var recommendations = await dbContext.ReviewRecommendations.ToListAsync(cancellationToken);
            foreach (var r in recommendations)
            {
                string? cleanKey = ConceptSanitizer.CleanAndValidate(r.ConceptKey);
                if (cleanKey == null)
                {
                    dbContext.ReviewRecommendations.Remove(r);
                }
                else
                {
                    r.ConceptKey = cleanKey;
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            logger?.LogInformation("ConceptCleanupService completed successfully.");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error occurred during ConceptCleanupService execution.");
        }
    }

    private static string NormalizeKey(string text)
    {
        return text.Trim()
            .Replace("أ", "ا")
            .Replace("إ", "ا")
            .Replace("آ", "ا")
            .Replace("ة", "ه")
            .Replace("ى", "ي");
    }
}
