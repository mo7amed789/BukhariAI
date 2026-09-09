using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Xunit;
using Xunit.Abstractions;

namespace BukhariAI.UnitTests;

public class ImageFormatDiagnosticTests
{
    private readonly ITestOutputHelper _output;

    public ImageFormatDiagnosticTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void InspectImageFiltersAndBytes()
    {
        string pdfPath = @"c:\Users\mohamed\Desktop\s.bokhari.5.pdf";
        if (!File.Exists(pdfPath)) return;

        using var doc = PdfDocument.Open(pdfPath);
        var page111 = doc.GetPage(111);
        var img = page111.GetImages().First();

        _output.WriteLine($"Image Type: {img.GetType().FullName}");
        _output.WriteLine($"Width: {img.WidthInSamples}, Height: {img.HeightInSamples}");
        bool hasPng = img.TryGetPng(out byte[]? pngBytes);
        _output.WriteLine($"TryGetPng: {hasPng} (Length: {pngBytes?.Length ?? 0})");
        _output.WriteLine($"RawBytes length: {img.RawBytes.Length}");

        byte[] rawArr = img.RawBytes.ToArray();
        string rawHex = string.Join(" ", rawArr.Take(30).Select(b => b.ToString("X2")));
        _output.WriteLine($"RawBytes header hex: {rawHex}");

        if (img.ImageDictionary != null)
        {
            foreach (var kvp in img.ImageDictionary.Data)
            {
                _output.WriteLine($"Dict Key: {kvp.Key} -> {kvp.Value}");
            }
        }
    }
}
