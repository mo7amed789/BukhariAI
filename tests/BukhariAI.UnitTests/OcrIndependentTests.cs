using System.IO.Compression;
using Tesseract;
using UglyToad.PdfPig;
using Xunit;
using Xunit.Abstractions;

namespace BukhariAI.UnitTests;

public class OcrIndependentTests
{
    private readonly ITestOutputHelper _output;

    public OcrIndependentTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void TestOcrOnPage111_RealPdf()
    {
        string pdfPath = @"c:\Users\mohamed\Desktop\s.bokhari.5.pdf";
        if (!File.Exists(pdfPath)) return;

        using var doc = PdfDocument.Open(pdfPath);
        var page111 = doc.GetPage(111);

        var images = page111.GetImages().ToList();
        _output.WriteLine($"[PDF] Page 111 image count: {images.Count}");
        Assert.NotEmpty(images);

        var img = images[0];
        _output.WriteLine($"[PDF] Image dimensions: {img.WidthInSamples}x{img.HeightInSamples}, BitsPerComponent: {img.BitsPerComponent}");

        byte[] rawBytes = img.RawBytes.ToArray();
        byte[] imageBytes = rawBytes;

        if (rawBytes.Length > 2 && rawBytes[0] == 0x78)
        {
            using var rawStream = new MemoryStream(rawBytes);
            using var zlib = new ZLibStream(rawStream, CompressionMode.Decompress);
            using var outStream = new MemoryStream();
            zlib.CopyTo(outStream);
            imageBytes = outStream.ToArray();
        }

        Assert.NotNull(imageBytes);

        // Find tessdata path
        string[] searchPaths =
        [
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata"),
            @"c:\Users\mohamed\source\repos\BukhariAI\tessdata",
            @"c:\Users\mohamed\source\repos\BukhariAI\src\BukhariAI.Api\tessdata",
            @"C:\Program Files\Tesseract-OCR\tessdata"
        ];

        string? tessdataPath = searchPaths.FirstOrDefault(p => File.Exists(Path.Combine(p, "ara.traineddata")));
        _output.WriteLine($"[OCR] Tessdata directory located at: '{tessdataPath}'");
        Assert.NotNull(tessdataPath);

        using var engine = new TesseractEngine(tessdataPath, "ara", EngineMode.Default);
        using var pix = Pix.LoadFromMemory(imageBytes);
        using var page = engine.Process(pix);

        string text = page.GetText()?.Trim() ?? string.Empty;
        float confidence = page.GetMeanConfidence();

        _output.WriteLine($"[OCR] Exit status: Success");
        _output.WriteLine($"[OCR] Confidence: {confidence:P1}");
        _output.WriteLine($"[OCR] Text length: {text.Length} characters");
        _output.WriteLine("==================== [OCR EXTRACTED TEXT PREVIEW (First 300 chars)] ====================");
        _output.WriteLine(text.Length > 300 ? text[..300] : text);
        _output.WriteLine("=========================================================================================");

        Assert.True(text.Length > 0, "OCR should extract text from page 111");
    }

    [Fact]
    public void TestQuarterOcrOnPage302_RealPdf()
    {
        string pdfPath = @"c:\Users\mohamed\Desktop\s.bokhari.5.pdf";
        if (!File.Exists(pdfPath)) return;

        using var doc = PdfDocument.Open(pdfPath);
        var page302 = doc.GetPage(302);
        var img = page302.GetImages().First();

        byte[] rawBytes = img.RawBytes.ToArray();
        byte[] imageBytes = rawBytes;
        if (rawBytes.Length > 2 && rawBytes[0] == 0x78)
        {
            using var rawStream = new MemoryStream(rawBytes);
            using var zlib = new ZLibStream(rawStream, CompressionMode.Decompress);
            using var outStream = new MemoryStream();
            zlib.CopyTo(outStream);
            imageBytes = outStream.ToArray();
        }

        string tessdataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
        using var engine = new TesseractEngine(tessdataPath, "ara", EngineMode.LstmOnly);
        using var pix = Pix.LoadFromMemory(imageBytes);
        pix.XRes = 300;
        pix.YRes = 300;

        using var grayPix = pix.Depth == 32 || pix.Depth == 24 ? pix.ConvertRGBToGray() : pix.Clone();
        grayPix.XRes = 300;
        grayPix.YRes = 300;

        // 1. Full-page pass
        string fullText;
        float fullConf;
        using (var fullPage = engine.Process(grayPix, PageSegMode.Auto))
        {
            fullText = fullPage.GetText()?.Trim() ?? string.Empty;
            fullConf = fullPage.GetMeanConfidence();
        }

        _output.WriteLine($"[FULL PAGE OCR]: Length={fullText.Length}, Conf={fullConf:P1}");
        _output.WriteLine("==================== FULL PAGE PREVIEW ====================");
        _output.WriteLine(fullText.Length > 400 ? fullText[..400] : fullText);

        // 2. Quarter-by-quarter (4 horizontal quarters) pass
        int quarterCount = 4;
        int sliceHeight = grayPix.Height / quarterCount;
        int overlap = (int)(sliceHeight * 0.02); // 2% overlap
        var quarters = new List<string>();
        float sumConf = 0;

        for (int i = 0; i < quarterCount; i++)
        {
            int y = Math.Max(0, i * sliceHeight - (i > 0 ? overlap : 0));
            int h = (i == quarterCount - 1)
                ? (grayPix.Height - y)
                : (sliceHeight + (i > 0 ? overlap : 0) + overlap);
            h = Math.Min(h, grayPix.Height - y);

            var rect = new Rect(0, y, grayPix.Width, h);
            using (var qPage = engine.Process(grayPix, rect, PageSegMode.SingleBlock))
            {
                string qText = qPage.GetText()?.Trim() ?? string.Empty;
                float qConf = qPage.GetMeanConfidence();
                sumConf += qConf;

                _output.WriteLine($"--- Quarter {i + 1} (y={y}, h={h}): Length={qText.Length}, Conf={qConf:P1} ---");
                _output.WriteLine(qText);
                if (!string.IsNullOrWhiteSpace(qText))
                {
                    quarters.Add(qText);
                }
            }
        }

        // 3. 2x2 Grid Quadrants (Arabic reading order: Top-Right -> Top-Left -> Bottom-Right -> Bottom-Left)
        int midX = grayPix.Width / 2;
        int midY = grayPix.Height / 2;

        var quadrantRects = new (string Name, Rect Rect)[]
        {
            ("Top-Right", new Rect(midX, 0, grayPix.Width - midX, midY)),
            ("Top-Left", new Rect(0, 0, midX, midY)),
            ("Bottom-Right", new Rect(midX, midY, grayPix.Width - midX, grayPix.Height - midY)),
            ("Bottom-Left", new Rect(0, midY, midX, grayPix.Height - midY))
        };

        _output.WriteLine("==================== 2x2 QUADRANTS OCR ====================");
        foreach (var (qName, qRect) in quadrantRects)
        {
            using var qp = engine.Process(grayPix, qRect, PageSegMode.Auto);
            string qt = qp.GetText()?.Trim() ?? string.Empty;
            _output.WriteLine($"[{qName}] (x={qRect.X1}, y={qRect.Y1}, w={qRect.Width}, h={qRect.Height}): {qt.Length} chars, Conf={qp.GetMeanConfidence():P1}");
            _output.WriteLine(qt);
        }
        _output.WriteLine("============================================================");
    }
}
