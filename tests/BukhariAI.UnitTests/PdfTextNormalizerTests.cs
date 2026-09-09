using BukhariAI.Infrastructure.Pdf;
using FluentAssertions;
using Xunit;

namespace BukhariAI.UnitTests;

public class PdfTextNormalizerTests
{
    private readonly PdfTextNormalizer _normalizer;

    public PdfTextNormalizerTests()
    {
        _normalizer = new PdfTextNormalizer(new TextNormalizationOptions
        {
            RemoveDiacritics = false,
            RemoveHeadersFooters = true
        });
    }

    [Fact]
    public void Normalize_WhenTextHasMultipleSpacesAndNewlines_ShouldCollapseRedundantWhitespace()
    {
        // Arrange
        string input = "عمر    بن    الخطاب\n\n\n\nرضي    الله    عنه";

        // Act
        string result = _normalizer.Normalize(input);

        // Assert
        result.Should().Be("عمر بن الخطاب\n\nرضي الله عنه");
    }

    [Fact]
    public void Normalize_ShouldPreserveArabicDiacritics_ByDefault()
    {
        // Arrange
        string input = "إِنَّمَا الأَعْمَالُ بِالنِّيَّاتِ";

        // Act
        string result = _normalizer.Normalize(input);

        // Assert
        result.Should().Be("إِنَّمَا الأَعْمَالُ بِالنِّيَّاتِ");
    }

    [Fact]
    public void Normalize_WhenConfiguredToRemoveDiacritics_ShouldStripTashkeel()
    {
        // Arrange
        var diacriticsStrippingNormalizer = new PdfTextNormalizer(new TextNormalizationOptions
        {
            RemoveDiacritics = true,
            RemoveHeadersFooters = true
        });
        string input = "إِنَّمَا الأَعْمَالُ بِالنِّيَّاتِ";

        // Act
        string result = diacriticsStrippingNormalizer.Normalize(input);

        // Assert
        result.Should().Be("إنما الأعمال بالنيات");
    }

    [Fact]
    public void Normalize_WhenTextHasIsolatedRunningHeaders_ShouldRemoveThem()
    {
        // Arrange
        string input = """
            صحيح البخاري
            124
            حدثنا الحميدي قال حدثنا سفيان
            """;

        // Act
        string result = _normalizer.Normalize(input);

        // Assert
        result.Should().NotContain("صحيح البخاري");
        result.Should().NotContain("124");
        result.Should().Contain("حدثنا الحميدي قال حدثنا سفيان");
    }
}
