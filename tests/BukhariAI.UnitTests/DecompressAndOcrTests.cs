using System.IO.Compression;
using Tesseract;
using UglyToad.PdfPig;
using Xunit;
using Xunit.Abstractions;

namespace BukhariAI.UnitTests;

public class DecompressAndOcrTests
{
    private readonly ITestOutputHelper _output;

    public DecompressAndOcrTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void DecompressAndOcrPage111()
    {
        string pdfPath = @"c:\Users\mohamed\Desktop\s.bokhari.5.pdf";
        if (!File.Exists(pdfPath)) return;

        using var doc = PdfDocument.Open(pdfPath);
        var page111 = doc.GetPage(111);
        var img = page111.GetImages().First();

        byte[] rawBytes = img.RawBytes.ToArray();
        _output.WriteLine($"RawBytes Length: {rawBytes.Length}");

        byte[] decompressedJpeg;
        using (var rawStream = new MemoryStream(rawBytes))
        using (var zlib = new ZLibStream(rawStream, CompressionMode.Decompress))
        using (var outStream = new MemoryStream())
        {
            zlib.CopyTo(outStream);
            decompressedJpeg = outStream.ToArray();
        }

        _output.WriteLine($"Decompressed JPEG Length: {decompressedJpeg.Length}");
        string jpegHex = string.Join(" ", decompressedJpeg.Take(10).Select(b => b.ToString("X2")));
        _output.WriteLine($"JPEG Header Hex: {jpegHex}"); // Should be FF D8 FF ...

        Assert.Equal(0xFF, decompressedJpeg[0]);
        Assert.Equal(0xD8, decompressedJpeg[1]);

        string tessdataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
        _output.WriteLine($"TessdataPath: {tessdataPath}");

        using var engine = new TesseractEngine(tessdataPath, "ara", EngineMode.Default);
        using var pix = Pix.LoadFromMemory(decompressedJpeg);
        string text;
        float confidence;
        using (var page = engine.Process(pix))
        {
            text = page.GetText()?.Trim() ?? string.Empty;
            confidence = page.GetMeanConfidence();
        }

        _output.WriteLine($"[OCR Exit Status]: Success");
        _output.WriteLine($"[OCR Confidence]: {confidence:P1}");
        _output.WriteLine($"[OCR Text Length]: {text.Length} characters");
        _output.WriteLine("==================== [OCR EXTRACTED TEXT FROM PAGE 111] ====================");
        _output.WriteLine(text.Length > 400 ? text[..400] : text);
        _output.WriteLine("============================================================================");

        Assert.NotEmpty(text);

        // Also test page 112
        var page112 = doc.GetPage(112);
        var img112 = page112.GetImages().First();
        byte[] decomp112;
        using (var raw112 = new MemoryStream(img112.RawBytes.ToArray()))
        using (var z112 = new ZLibStream(raw112, CompressionMode.Decompress))
        using (var out112 = new MemoryStream())
        {
            z112.CopyTo(out112);
            decomp112 = out112.ToArray();
        }

        using var pix112 = Pix.LoadFromMemory(decomp112);
        using var ocrP112 = engine.Process(pix112);
        string text112 = ocrP112.GetText()?.Trim() ?? string.Empty;
        _output.WriteLine($"[OCR Page 112 Text Length]: {text112.Length} characters");
        _output.WriteLine("==================== [OCR EXTRACTED TEXT FROM PAGE 112] ====================");
        _output.WriteLine(text112.Length > 400 ? text112[..400] : text112);
        _output.WriteLine("============================================================================");
        Assert.NotEmpty(text112);
    }
}
