using System.IO.Compression;
using System.Text.RegularExpressions;
using Tesseract;
using UglyToad.PdfPig;
using Xunit;
using Xunit.Abstractions;

namespace BukhariAI.UnitTests;

public class OcrOptimizationBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public OcrOptimizationBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void BenchmarkOcrQualityOnPages110And111()
    {
        string pdfPath = @"c:\Users\mohamed\Desktop\s.bokhari.5.pdf";
        if (!File.Exists(pdfPath)) return;

        using var doc = PdfDocument.Open(pdfPath);
        string tessdataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");

        int[] testPages = [110, 111];
        PageSegMode[] psmModes = [PageSegMode.Auto, PageSegMode.SingleColumn, PageSegMode.SingleBlock];

        foreach (int pageNum in testPages)
        {
            _output.WriteLine($"\n================================================================================");
            _output.WriteLine($"                          PAGE {pageNum} BENCHMARK");
            _output.WriteLine($"================================================================================");

            var page = doc.GetPage(pageNum);
            var img = page.GetImages().First();

            byte[] rawBytes = img.RawBytes.ToArray();
            byte[] jpegBytes;
            using (var rawStream = new MemoryStream(rawBytes))
            using (var zlib = new ZLibStream(rawStream, CompressionMode.Decompress))
            using (var outStream = new MemoryStream())
            {
                zlib.CopyTo(outStream);
                jpegBytes = outStream.ToArray();
            }

            using var engine = new TesseractEngine(tessdataPath, "ara", EngineMode.LstmOnly);

            foreach (var psm in psmModes)
            {
                // Test 1: Raw with 300 DPI metadata
                using (var pix = Pix.LoadFromMemory(jpegBytes))
                {
                    pix.XRes = 300;
                    pix.YRes = 300;

                    using var ocrPage = engine.Process(pix, psm);
                    string text = ocrPage.GetText()?.Trim() ?? string.Empty;
                    float conf = ocrPage.GetMeanConfidence();
                    double arabicRatio = CalculateArabicRatio(text);

                    _output.WriteLine($"\n--- [PSM: {psm}, Preprocessing: None (300 DPI)] ---");
                    _output.WriteLine($"Length: {text.Length} chars | Confidence: {conf:P1} | ArabicRatio: {arabicRatio:P1}");
                    _output.WriteLine($"Preview (first 250 chars):\n{(text.Length > 250 ? text[..250] : text)}");
                }

                // Test 2: Grayscale + Deskew with 300 DPI
                using (var pix = Pix.LoadFromMemory(jpegBytes))
                {
                    pix.XRes = 300;
                    pix.YRes = 300;

                    using var grayPix = pix.Depth == 32 || pix.Depth == 24 ? pix.ConvertRGBToGray() : pix.Clone();
                    grayPix.XRes = 300;
                    grayPix.YRes = 300;

                    using var ocrPage = engine.Process(grayPix, psm);
                    string text = ocrPage.GetText()?.Trim() ?? string.Empty;
                    float conf = ocrPage.GetMeanConfidence();
                    double arabicRatio = CalculateArabicRatio(text);

                    _output.WriteLine($"\n--- [PSM: {psm}, Preprocessing: Grayscale+300DPI] ---");
                    _output.WriteLine($"Length: {text.Length} chars | Confidence: {conf:P1} | ArabicRatio: {arabicRatio:P1}");
                    _output.WriteLine($"Preview (first 250 chars):\n{(text.Length > 250 ? text[..250] : text)}");
                }

                // Test 3: Otsu Adaptive Binarization
                using (var pix = Pix.LoadFromMemory(jpegBytes))
                {
                    pix.XRes = 300;
                    pix.YRes = 300;

                    using var grayPix = pix.Depth == 32 || pix.Depth == 24 ? pix.ConvertRGBToGray() : pix.Clone();
                    using var binarizedPix = grayPix.BinarizeOtsuAdaptiveThreshold(200, 200, 0, 0, 0.1f);
                    binarizedPix.XRes = 300;
                    binarizedPix.YRes = 300;

                    using var ocrPage = engine.Process(binarizedPix, psm);
                    string text = ocrPage.GetText()?.Trim() ?? string.Empty;
                    float conf = ocrPage.GetMeanConfidence();
                    double arabicRatio = CalculateArabicRatio(text);

                    _output.WriteLine($"\n--- [PSM: {psm}, Preprocessing: OtsuAdaptiveThreshold+300DPI] ---");
                    _output.WriteLine($"Length: {text.Length} chars | Confidence: {conf:P1} | ArabicRatio: {arabicRatio:P1}");
                    _output.WriteLine($"Preview (first 250 chars):\n{(text.Length > 250 ? text[..250] : text)}");
                }
            }
        }
    }

    private static double CalculateArabicRatio(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        int arabicCount = text.Count(c => c >= '\u0600' && c <= '\u06FF' || c >= '\u0750' && c <= '\u077F' || c >= '\uFB50' && c <= '\uFEFF');
        int meaningfulCount = text.Count(c => char.IsLetterOrDigit(c) || char.IsPunctuation(c));
        return meaningfulCount > 0 ? (double)arabicCount / meaningfulCount : 0;
    }
}
