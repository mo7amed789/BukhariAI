using UglyToad.PdfPig;
using Xunit;
using Xunit.Abstractions;

namespace BukhariAI.UnitTests;

public class PdfStructureDiagnosticTests
{
    private readonly ITestOutputHelper _output;

    public PdfStructureDiagnosticTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void InspectPdfStructure()
    {
        string pdfPath = @"c:\Users\mohamed\Desktop\s.bokhari.5.pdf";
        if (!File.Exists(pdfPath)) return;

        using var doc = PdfDocument.Open(pdfPath);
        _output.WriteLine($"PDF NumberOfPages: {doc.NumberOfPages}");

        for (int p = 1; p <= Math.Min(10, doc.NumberOfPages); p++)
        {
            var page = doc.GetPage(p);
            int images = page.GetImages().Count();
            int letters = page.Letters.Count;
            _output.WriteLine($"Page {p}: DirectText='{page.Text.Trim()}', LetterCount={letters}, ImageCount={images}, Size=({page.Width}x{page.Height})");
        }

        // Also check page 500 if exists
        if (doc.NumberOfPages >= 500)
        {
            var p500 = doc.GetPage(500);
            int images500 = p500.GetImages().Count();
            int letters500 = p500.Letters.Count;
            _output.WriteLine($"Page 500: DirectText='{p500.Text.Trim()}', LetterCount={letters500}, ImageCount={images500}, Size=({p500.Width}x{p500.Height})");
        }
    }
}
