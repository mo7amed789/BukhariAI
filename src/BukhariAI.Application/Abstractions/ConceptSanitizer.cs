using System.Text.RegularExpressions;

namespace BukhariAI.Application.Abstractions;

/// <summary>
/// Authoritative sanitizer and validator for pedagogical Islamic concept titles.
/// Ensures all concept keys stored and displayed are concise, meaningful, and well-formed
/// topic titles (e.g. "حكم أموال الكعبة وخزانتها", "طهارة جلود الميتة بالدباغ")
/// rather than broken sentence fragments or raw text excerpts.
/// </summary>
public static partial class ConceptSanitizer
{
    private static readonly HashSet<string> DisallowedStartWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "و", "ف", "فلا", "فلما", "ثم", "أو", "أم", "أن", "إن", "بأن", "لأن", "بين", "وبين", "بينهما",
        "عن", "في", "من", "إلى", "على", "حتى", "كما", "لكن", "بل", "حيث", "إذ", "إذا", "لما",
        "روى", "وروى", "قال", "وقال", "هم", "وهم", "وكان", "كان", "ذكر", "وذكر", "أخبر", "بينما",
        "لله", "رضي", "عنه", "عنها", "عنهم", "صلى", "عليه", "وسلم", "أي", "يعني",
        "وهذا", "هذا", "هذه", "وهذه", "ذلك", "تلك", "وكذلك", "كذلك", "وهنا", "هنا", "هل", "وهل"
    };

    private static readonly HashSet<string> DisallowedEndWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "أن", "إن", "بأن", "لأن", "على", "في", "من", "إلى", "عن", "و", "أو", "أم", "ثم", "حتى", "مع", "بين"
    };

    private static readonly Regex InvalidCharsRegex = GeneratedInvalidCharsRegex();
    private static readonly Regex MultiSpaceRegex = GeneratedMultiSpaceRegex();
    private static readonly Regex LeadingConjunctionRegex = GeneratedLeadingConjunctionRegex();

    /// <summary>
    /// Validates whether a given string constitutes a clean, well-formed concept title.
    /// </summary>
    public static bool IsValidConcept(string? concept)
    {
        if (string.IsNullOrWhiteSpace(concept)) return false;

        string clean = CleanConceptKey(concept);
        if (clean.Length < 3 || clean.Length > 70) return false;

        // Must not contain sentence punctuation, quotation marks, or brackets
        if (InvalidCharsRegex.IsMatch(clean)) return false;

        var words = clean.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 1 || words.Length > 8) return false;

        // Check first word for broken fragments or narrative/conjunction starts
        string firstWord = words[0];
        if (firstWord.Length <= 1) return false; // Single isolated Arabic letters like "ن" or "و"
        if (firstWord.Length == 2 && (firstWord == "سة" || firstWord == "بة" || firstWord == "فة" || firstWord == "له" || firstWord == "عن" || firstWord == "في" || firstWord == "من" || firstWord == "او" || firstWord == "ان"))
        {
            return false;
        }

        if (DisallowedStartWords.Contains(firstWord))
        {
            return false;
        }

        // Disallow trailing prepositions / incomplete connectors
        string lastWord = words[^1];
        if (DisallowedEndWords.Contains(lastWord))
        {
            return false;
        }

        // Check if string contains typical broken narrative signatures (e.g. "لله عنه", "رضي الله عنه")
        if (clean.Contains("لله عنه", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("رضي الله", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("صلى الله", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Cleans and normalizes a candidate concept string, stripping surrounding quotes/punctuation/prefixes.
    /// Returns the cleaned concept if valid, or null if it cannot be salvaged into a valid concept title.
    /// </summary>
    public static string? CleanAndValidate(string? rawConcept)
    {
        if (string.IsNullOrWhiteSpace(rawConcept)) return null;

        string clean = CleanConceptKey(rawConcept);

        // Strip leading conjunction prefixes like "و" or "فـ" when attached to a noun (e.g. "وحكم" -> "حكم", "فمسألة" -> "مسألة")
        clean = LeadingConjunctionRegex.Replace(clean, string.Empty).Trim();

        // Strip prefixes like "بـ" or "كالـ"
        if (clean.StartsWith("بـ") && clean.Length > 3) clean = clean[2..].Trim();
        if (clean.StartsWith("كالـ") && clean.Length > 5) clean = "الـ" + clean[4..].Trim();
        if (clean.StartsWith("كال") && clean.Length > 4 && !clean.StartsWith("كلام")) clean = "ال" + clean[3..].Trim();

        if (clean.StartsWith("مسألة ") && clean.Length > 7)
        {
            clean = clean.Trim();
        }

        clean = MultiSpaceRegex.Replace(clean, " ").Trim();

        if (!IsValidConcept(clean)) return null;

        return clean;
    }

    /// <summary>
    /// Derives a clean, academic 2-5 word concept title from a raw problem/issue description.
    /// E.g. "حكم نقل كسوة الكعبة أو قسمتها والتحلية بالذهب" -> "حكم كسوة الكعبة وقسمتها".
    /// </summary>
    public static string DeriveTopicTitle(string? problemOrTitle, string fallback = "مسألة فقهية")
    {
        if (string.IsNullOrWhiteSpace(problemOrTitle)) return fallback;

        string text = problemOrTitle.Trim();

        // Convert interrogative constructs to scholarly topic title headers
        text = text.Replace("المسألة الفقهية:", "")
                   .Replace("المسألة:", "")
                   .Replace("موضوع المقطع:", "")
                   .Replace("حكم مسألة:", "حكم")
                   .Replace("ما حكم", "حكم")
                   .Replace("هل يجوز", "حكم")
                   .Replace("هل يصح", "حكم")
                   .Replace("هل يثبت", "حكم")
                   .Replace("هل يجب", "وجوب")
                   .Replace("هل", "")
                   .Trim();

        // Split at first punctuation or conjunction boundary if too long
        int breakIdx = text.IndexOfAny(['،', '؛', '.', '!', '؟', '?', '\n', '\r', '—', '-']);
        if (breakIdx > 0 && breakIdx <= 60)
        {
            text = text[..breakIdx].Trim();
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 7)
        {
            text = string.Join(" ", words.Take(5));
        }

        var cleaned = CleanAndValidate(text);
        if (!string.IsNullOrWhiteSpace(cleaned))
        {
            return cleaned;
        }

        return fallback;
    }

    private static string CleanConceptKey(string text)
    {
        return text.Trim()
            .Trim('،', '؛', ':', '.', '-', '—', '«', '»', '"', '\'', '[', ']', '(', ')', '{', '}', '*', '_', '`', ' ', '\t', '\n', '\r');
    }

    [GeneratedRegex(@"[،؛:\n\r\t!؟?|~«»""'\[\]\(\)\{\}\*_]")]
    private static partial Regex GeneratedInvalidCharsRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex GeneratedMultiSpaceRegex();

    [GeneratedRegex(@"^(?:و(?=[\p{IsArabic}]{3,})|فـ(?=[\p{IsArabic}]{3,})|ف(?=[\p{IsArabic}]{3,}))\s*")]
    private static partial Regex GeneratedLeadingConjunctionRegex();
}
