using System.Text.Json;
using System.Text.Json.Serialization;
using BukhariAI.Application.Lessons.GenerateLesson;

namespace BukhariAI.Infrastructure.AI;

public sealed class AiValidationResult
{
    public bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
    public GenerateLessonResponse? Value { get; init; }
}

public static class AiResponseValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static AiValidationResult ValidateAndDeserialize(string? rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return new AiValidationResult
            {
                IsValid = false,
                ErrorMessage = "AI response was empty or null."
            };
        }

        string cleanedJson = CleanJsonString(rawResponse);
        if (string.IsNullOrWhiteSpace(cleanedJson))
        {
            return new AiValidationResult
            {
                IsValid = false,
                ErrorMessage = "AI response did not contain valid JSON."
            };
        }

        GenerateLessonResponse? lesson = null;
        try
        {
            using var doc = JsonDocument.Parse(cleanedJson);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                int arrayLength = root.GetArrayLength();
                if (arrayLength == 0)
                {
                    return new AiValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = "AI response returned an empty JSON array."
                    };
                }

                // If multiple lesson elements were returned in an array, deserialize each and merge
                if (arrayLength == 1)
                {
                    lesson = JsonSerializer.Deserialize<GenerateLessonResponse>(root[0].GetRawText(), JsonOptions);
                }
                else
                {
                    var lessons = JsonSerializer.Deserialize<List<GenerateLessonResponse>>(root.GetRawText(), JsonOptions);
                    if (lessons != null && lessons.Count > 0)
                    {
                        var primary = lessons[0];
                        for (int i = 1; i < lessons.Count; i++)
                        {
                            var secondary = lessons[i];
                            if (secondary.Hadiths?.Count > 0)
                            {
                                primary.Hadiths.AddRange(secondary.Hadiths);
                            }
                            if (secondary.SourcePages?.Count > 0)
                            {
                                primary.SourcePages.AddRange(secondary.SourcePages);
                            }
                            if (secondary.Connections?.Count > 0)
                            {
                                primary.Connections.AddRange(secondary.Connections);
                            }
                            if (secondary.ReviewQuestions?.Count > 0)
                            {
                                primary.ReviewQuestions.AddRange(secondary.ReviewQuestions);
                            }
                            if (secondary.AssessmentQuestions?.Count > 0)
                            {
                                primary.AssessmentQuestions.AddRange(secondary.AssessmentQuestions);
                            }
                        }

                        // Deduplicate source pages
                        var distinctPages = primary.SourcePages.Distinct().OrderBy(p => p).ToList();
                        primary.SourcePages.Clear();
                        primary.SourcePages.AddRange(distinctPages);

                        lesson = primary;
                    }
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                // Check if wrapped in an outer object, e.g. { "lesson": { ... } } or { "lessons": [ ... ] } or { "data": { ... } }
                if (root.TryGetProperty("lesson", out var innerLesson) && innerLesson.ValueKind == JsonValueKind.Object)
                {
                    lesson = JsonSerializer.Deserialize<GenerateLessonResponse>(innerLesson.GetRawText(), JsonOptions);
                }
                else if (root.TryGetProperty("data", out var innerData) && innerData.ValueKind == JsonValueKind.Object)
                {
                    lesson = JsonSerializer.Deserialize<GenerateLessonResponse>(innerData.GetRawText(), JsonOptions);
                }
                else if (root.TryGetProperty("lessons", out var innerLessons) && innerLessons.ValueKind == JsonValueKind.Array && innerLessons.GetArrayLength() > 0)
                {
                    lesson = JsonSerializer.Deserialize<GenerateLessonResponse>(innerLessons[0].GetRawText(), JsonOptions);
                }
                else
                {
                    lesson = JsonSerializer.Deserialize<GenerateLessonResponse>(root.GetRawText(), JsonOptions);
                }
            }
            else
            {
                return new AiValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"AI response JSON root is of unexpected type: {root.ValueKind}."
                };
            }
        }
        catch (JsonException ex)
        {
            return new AiValidationResult
            {
                IsValid = false,
                ErrorMessage = $"Failed to parse AI JSON response: {ex.Message}"
            };
        }

        if (lesson is null)
        {
            return new AiValidationResult
            {
                IsValid = false,
                ErrorMessage = "AI response deserialized to null."
            };
        }

        // Validate required educational schema components
        if (string.IsNullOrWhiteSpace(lesson.Title))
        {
            return new AiValidationResult
            {
                IsValid = false,
                ErrorMessage = "AI response is missing a valid 'Title'."
            };
        }

        if (string.IsNullOrWhiteSpace(lesson.Overview))
        {
            return new AiValidationResult
            {
                IsValid = false,
                ErrorMessage = "AI response is missing an 'Overview'."
            };
        }

        if (lesson.Hadiths == null || lesson.Hadiths.Count == 0)
        {
            return new AiValidationResult
            {
                IsValid = false,
                ErrorMessage = "AI response must contain at least one hadith explanation."
            };
        }

        foreach (var hadith in lesson.Hadiths)
        {
            if (string.IsNullOrWhiteSpace(hadith.Reference))
            {
                return new AiValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Hadith explanation item is missing a 'Reference'."
                };
            }

            if (string.IsNullOrWhiteSpace(hadith.Problem))
            {
                return new AiValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Hadith '{hadith.Reference}' is missing a 'Problem' statement."
                };
            }

            if (string.IsNullOrWhiteSpace(hadith.Conclusion))
            {
                return new AiValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Hadith '{hadith.Reference}' is missing a 'Conclusion'."
                };
            }

            if (string.IsNullOrWhiteSpace(hadith.EasyExplanation))
            {
                return new AiValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Hadith '{hadith.Reference}' is missing an 'EasyExplanation'."
                };
            }

            if (hadith.Evidence != null)
            {
                foreach (var ev in hadith.Evidence)
                {
                    if (string.IsNullOrWhiteSpace(ev.Text))
                    {
                        return new AiValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = $"Hadith '{hadith.Reference}' contains an evidence item missing 'Text'."
                        };
                    }
                }
            }
        }

        return new AiValidationResult
        {
            IsValid = true,
            Value = lesson
        };
    }

    private static string CleanJsonString(string text)
    {
        string trimmed = text.Trim();

        // Strip markdown fences ```json ... ``` if present
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[7..];
        }
        else if (trimmed.StartsWith("```"))
        {
            trimmed = trimmed[3..];
        }

        if (trimmed.EndsWith("```"))
        {
            trimmed = trimmed[..^3];
        }

        trimmed = trimmed.Trim();

        // If there is still text outside the outermost JSON structure (e.g. conversational preamble/postscript), locate first '{' or '[' and last '}' or ']'
        int firstObj = trimmed.IndexOf('{');
        int firstArr = trimmed.IndexOf('[');

        int start = -1;
        int end = -1;

        if (firstObj >= 0 && firstArr >= 0)
        {
            if (firstObj < firstArr)
            {
                start = firstObj;
                end = trimmed.LastIndexOf('}');
            }
            else
            {
                start = firstArr;
                end = trimmed.LastIndexOf(']');
            }
        }
        else if (firstObj >= 0)
        {
            start = firstObj;
            end = trimmed.LastIndexOf('}');
        }
        else if (firstArr >= 0)
        {
            start = firstArr;
            end = trimmed.LastIndexOf(']');
        }

        if (start >= 0 && end >= start)
        {
            trimmed = trimmed.Substring(start, end - start + 1);
        }

        return trimmed.Trim();
    }
}
