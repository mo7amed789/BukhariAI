using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Png;
using Tesseract;
using UglyToad.PdfPig;
using Xunit;
using Xunit.Abstractions;

namespace BukhariAI.UnitTests;

/// <summary>
/// OCR upsampling benchmark for page 110 of s.bokhari.5.pdf.
/// Tests 1x / 2x / 3x Lanczos upsampling across PSM 3,4,6 and preprocessing variants.
/// Also tests header-crop impact on segmentation quality.
/// Does NOT modify any production code.
/// </summary>
public class OcrUpsamplingBenchmark
{
    // ──────────────────────────────────────────────────────────────────────────
    //  Configuration constants
    // ──────────────────────────────────────────────────────────────────────────
    private const string PdfPath = @"c:\Users\mohamed\Desktop\s.bokhari.5.pdf";
    private const int PageNum = 110;
    private const int NativeDpi = 200;   // confirmed from Leptonica metadata
    private const int OcrDpi = 300;      // always tell Tesseract 300 DPI regardless of scale

    // Benchmark output root (relative to test binary output dir — excluded from git)
    private static readonly string BenchmarkRoot =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ocr-benchmark", "page-110");

    // ──────────────────────────────────────────────────────────────────────────
    //  Metrics record (serialised to metadata.json per configuration)
    // ──────────────────────────────────────────────────────────────────────────
    public sealed record BenchmarkEntry
    {
        public string ConfigId        { get; init; } = "";
        public string ScaleLabel      { get; init; } = "";   // "1x", "2x", "3x"
        public int    ScaleFactor     { get; init; }
        public int    ImageWidth      { get; init; }
        public int    ImageHeight     { get; init; }
        public string PsmLabel        { get; init; } = "";
        public int    PsmInt          { get; init; }
        public string Preprocessing   { get; init; } = "";   // Gray | Otsu | Adaptive
        public bool   HeaderCropped   { get; init; }
        public string CropLabel       { get; init; } = "";   // "Full" | "Crop10" | "Crop15"
        public int    TotalChars      { get; init; }
        public int    TotalWords      { get; init; }
        public double ArabicCharRatio { get; init; }
        public double ArabicWordRatio { get; init; }
        public double SuspiciousRatio { get; init; }
        public double TesseractConf   { get; init; }
        public double QualityScore    { get; init; }
        public long   ElapsedMs       { get; init; }
        public string Excerpt         { get; init; } = "";
    }

    private readonly ITestOutputHelper _out;
    public OcrUpsamplingBenchmark(ITestOutputHelper output) => _out = output;

    // ──────────────────────────────────────────────────────────────────────────
    //  Entry point
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void RunUpsamplingBenchmark()
    {
        if (!File.Exists(PdfPath))
        {
            _out.WriteLine($"[SKIP] PDF not found: {PdfPath}");
            return;
        }

        Directory.CreateDirectory(BenchmarkRoot);

        // ── 1. Extract the raw JPEG bytes from page 110 ──────────────────────
        byte[] jpegBytes = ExtractPageJpeg(PdfPath, PageNum);
        _out.WriteLine($"[INFO] JPEG extracted: {jpegBytes.Length:N0} bytes");

        // ── 2. Build all scale variants using ImageSharp Lanczos ─────────────
        var scales = new (string Label, int Factor)[]
        {
            ("1x", 1),
            ("2x", 2),
            ("3x", 3),
        };

        // Pre-render all scale images once (expensive for 3x)
        var scaledImages = new Dictionary<string, (byte[] Png, int W, int H)>();
        foreach (var (label, factor) in scales)
        {
            var (png, w, h) = ScaleLanczos(jpegBytes, factor);
            scaledImages[label] = (png, w, h);
            string imgPath = Path.Combine(BenchmarkRoot, label);
            Directory.CreateDirectory(imgPath);
            File.WriteAllBytes(Path.Combine(imgPath, "source.png"), png);
            _out.WriteLine($"[INFO] {label}: {w}×{h} px saved.");
        }

        // ── 3. Tesseract engine ───────────────────────────────────────────────
        string tessdataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
        bool hasTessdata = File.Exists(Path.Combine(tessdataPath, "ara.traineddata"));
        _out.WriteLine($"[INFO] Tessdata: {tessdataPath}  exists={hasTessdata}");
        Assert.True(hasTessdata, "ara.traineddata must exist in tessdata/");

        using var engine = new TesseractEngine(tessdataPath, "ara", EngineMode.LstmOnly);

        var psms = new (string Label, PageSegMode Mode, int Int)[]
        {
            ("PSM3-Auto",          PageSegMode.Auto,         3),
            ("PSM4-SingleColumn",  PageSegMode.SingleColumn, 4),
            ("PSM6-SingleBlock",   PageSegMode.SingleBlock,  6),
        };

        var preprocs = new string[] { "Gray", "Otsu", "Adaptive" };

        var results = new List<BenchmarkEntry>();

        // ── 4. Main matrix: scale × PSM × preprocessing ──────────────────────
        foreach (var (scaleLabel, scaleFactor) in scales)
        {
            var (png, w, h) = scaledImages[scaleLabel];

            foreach (var (psmLabel, psmMode, psmInt) in psms)
            {
                foreach (var preproc in preprocs)
                {
                    string configId = $"{scaleLabel}_{psmLabel}_{preproc}_Full";
                    var entry = RunSinglePass(
                        engine, png, w, h, scaleFactor, scaleLabel,
                        psmMode, psmInt, psmLabel,
                        preproc, headerCropped: false, cropLabel: "Full",
                        configId);
                    results.Add(entry);
                    SaveEntry(entry, png, "Full");
                }
            }
        }

        // ── 5. Header crop experiment on the best-scale config ────────────────
        // Use the scale that achieved the highest quality score so far
        var bestBase = results.OrderByDescending(r => r.QualityScore).First();
        int bestScale = bestBase.ScaleFactor;
        string bestScaleLabel = bestBase.ScaleLabel;
        PageSegMode bestPsm = psms.First(p => p.Int == bestBase.PsmInt).Mode;

        _out.WriteLine($"\n[INFO] Header crop experiment on {bestScaleLabel}, PSM {bestBase.PsmInt}, {bestBase.Preprocessing}");

        var (bestPng, bestW, bestH) = scaledImages[bestScaleLabel];

        foreach (var cropLabel in new[] { "Crop10", "Crop15" })
        {
            double cropFraction = cropLabel == "Crop10" ? 0.10 : 0.15;
            byte[] croppedPng = CropTop(bestPng, cropFraction);
            int croppedH = (int)(bestH * (1.0 - cropFraction));

            foreach (var preproc in preprocs)
            {
                string configId = $"{bestScaleLabel}_{bestBase.PsmLabel}_{preproc}_{cropLabel}";
                var entry = RunSinglePass(
                    engine, croppedPng, bestW, croppedH,
                    bestScale, bestScaleLabel,
                    bestPsm, bestBase.PsmInt, bestBase.PsmLabel,
                    preproc, headerCropped: true, cropLabel,
                    configId);
                results.Add(entry);
                SaveEntry(entry, croppedPng, cropLabel);
            }
        }

        // ── 6. Print report ───────────────────────────────────────────────────
        PrintReport(results, bestBase);

        // ── 7. Assert at least one config is acceptable ───────────────────────
        Assert.True(results.Any(r => r.QualityScore > 0.70),
            "Expected at least one configuration to achieve quality score > 0.70");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Run one OCR pass and return a BenchmarkEntry
    // ──────────────────────────────────────────────────────────────────────────
    private static BenchmarkEntry RunSinglePass(
        TesseractEngine engine,
        byte[] png, int w, int h,
        int scaleFactor, string scaleLabel,
        PageSegMode psmMode, int psmInt, string psmLabel,
        string preproc, bool headerCropped, string cropLabel,
        string configId)
    {
        var sw = Stopwatch.StartNew();

        using var pix = Pix.LoadFromMemory(png);
        pix.XRes = OcrDpi;
        pix.YRes = OcrDpi;

        // Apply preprocessing
        Pix activePix;
        Pix? grayPix = null;
        Pix? threshPix = null;

        if (pix.Depth == 32 || pix.Depth == 24)
        {
            grayPix = pix.ConvertRGBToGray();
            grayPix.XRes = OcrDpi;
            grayPix.YRes = OcrDpi;
        }

        Pix baseGray = grayPix ?? pix;

        if (preproc == "Otsu")
        {
            threshPix = baseGray.BinarizeOtsuAdaptiveThreshold(200, 200, 0, 0, 0.1f);
            threshPix.XRes = OcrDpi;
            threshPix.YRes = OcrDpi;
            activePix = threshPix;
        }
        else if (preproc == "Adaptive")
        {
            // Tesseract 5.2.0 wrapper does not expose Sauvola.
            // Use a tighter Otsu tile (50×50) as the "Adaptive" variant —
            // smaller tiles better handle local illumination variation across the scan.
            threshPix = baseGray.BinarizeOtsuAdaptiveThreshold(50, 50, 0, 0, 0.1f);
            threshPix.XRes = OcrDpi;
            threshPix.YRes = OcrDpi;
            activePix = threshPix;
        }
        else
        {
            activePix = baseGray; // Gray
        }

        string rawText;
        float tessConf;
        {
            using var ocrPage = engine.Process(activePix, psmMode);
            rawText  = ocrPage.GetText()?.Trim() ?? string.Empty;
            tessConf = ocrPage.GetMeanConfidence();
        }

        grayPix?.Dispose();
        threshPix?.Dispose();

        sw.Stop();

        // Compute metrics
        var metrics = ComputeMetrics(rawText, tessConf);
        string excerpt = rawText.Length > 500 ? rawText[..500] : rawText;

        return new BenchmarkEntry
        {
            ConfigId        = configId,
            ScaleLabel      = scaleLabel,
            ScaleFactor     = scaleFactor,
            ImageWidth      = w,
            ImageHeight     = h,
            PsmLabel        = psmLabel,
            PsmInt          = psmInt,
            Preprocessing   = preproc,
            HeaderCropped   = headerCropped,
            CropLabel       = cropLabel,
            TotalChars      = rawText.Length,
            TotalWords      = rawText.Split([' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length,
            ArabicCharRatio = metrics.ArabicCharRatio,
            ArabicWordRatio = metrics.ArabicWordRatio,
            SuspiciousRatio = metrics.SuspiciousRatio,
            TesseractConf   = Math.Round(tessConf, 3),
            QualityScore    = metrics.QualityScore,
            ElapsedMs       = sw.ElapsedMilliseconds,
            Excerpt         = excerpt,
        };
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Save benchmark entry to disk
    // ──────────────────────────────────────────────────────────────────────────
    private static void SaveEntry(BenchmarkEntry entry, byte[] png, string cropLabel)
    {
        string dir = Path.Combine(BenchmarkRoot, entry.ScaleLabel, entry.CropLabel,
                                  $"{entry.PsmLabel}_{entry.Preprocessing}");
        Directory.CreateDirectory(dir);

        File.WriteAllBytes(Path.Combine(dir, "image.png"), png);
        File.WriteAllText(Path.Combine(dir, "raw.txt"),
            entry.Excerpt, Encoding.UTF8);
        File.WriteAllText(Path.Combine(dir, "metadata.json"),
            JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Lanczos upsampling using SixLabors.ImageSharp
    // ──────────────────────────────────────────────────────────────────────────
    private static (byte[] Png, int W, int H) ScaleLanczos(byte[] jpegBytes, int factor)
    {
        if (factor == 1)
        {
            // Return as PNG unchanged
            using var img1x = SixLabors.ImageSharp.Image.Load(jpegBytes);
            using var ms1x = new MemoryStream();
            img1x.Save(ms1x, new PngEncoder { CompressionLevel = PngCompressionLevel.BestSpeed });
            return (ms1x.ToArray(), img1x.Width, img1x.Height);
        }

        using var img = SixLabors.ImageSharp.Image.Load(jpegBytes);
        int newW = img.Width  * factor;
        int newH = img.Height * factor;

        // Lanczos3 is the highest quality resampler in ImageSharp
        img.Mutate(ctx => ctx.Resize(new ResizeOptions
        {
            Size    = new SixLabors.ImageSharp.Size(newW, newH),
            Sampler = KnownResamplers.Lanczos3,
            Mode    = ResizeMode.Stretch,
        }));

        using var ms = new MemoryStream();
        img.Save(ms, new PngEncoder { CompressionLevel = PngCompressionLevel.BestSpeed });
        return (ms.ToArray(), newW, newH);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Crop top N% of image using ImageSharp
    // ──────────────────────────────────────────────────────────────────────────
    private static byte[] CropTop(byte[] pngBytes, double topFraction)
    {
        using var img = SixLabors.ImageSharp.Image.Load(pngBytes);
        int cropPx = (int)(img.Height * topFraction);
        img.Mutate(ctx => ctx.Crop(new SixLabors.ImageSharp.Rectangle(
            0, cropPx, img.Width, img.Height - cropPx)));

        using var ms = new MemoryStream();
        img.Save(ms, new PngEncoder { CompressionLevel = PngCompressionLevel.BestSpeed });
        return ms.ToArray();
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Extract compressed JPEG from PDF page via PdfPig + ZLib decompression
    // ──────────────────────────────────────────────────────────────────────────
    private static byte[] ExtractPageJpeg(string pdfPath, int pageNum)
    {
        using var doc = PdfDocument.Open(pdfPath);
        var page = doc.GetPage(pageNum);
        var img = page.GetImages()
                      .OrderByDescending(i => i.WidthInSamples * i.HeightInSamples)
                      .First();

        byte[] raw = img.RawBytes.ToArray();

        // ZLib-wrapped JPEG (compound filter: /FlateDecode + /DCTDecode)
        bool isZLib = raw.Length > 2 && raw[0] == 0x78 &&
                      (raw[1] == 0x9C || raw[1] == 0xDA || raw[1] == 0x01 || raw[1] == 0x5E);

        if (isZLib)
        {
            using var rs = new MemoryStream(raw);
            using var zs = new ZLibStream(rs, CompressionMode.Decompress);
            using var os = new MemoryStream();
            zs.CopyTo(os);
            return os.ToArray();
        }

        return raw;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Heuristic metrics
    // ──────────────────────────────────────────────────────────────────────────
    private sealed record MetricsResult(
        double ArabicCharRatio, double ArabicWordRatio,
        double SuspiciousRatio, double QualityScore);

    private static MetricsResult ComputeMetrics(string text, float tessConf)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new MetricsResult(0, 0, 0, 0);

        int total   = text.Length;
        int arabicC = text.Count(IsArabic);
        int ws      = text.Count(char.IsWhiteSpace);
        int nonWs   = total - ws;

        double arabicCharRatio = nonWs > 0 ? (double)arabicC / nonWs : 0;

        // Arabic word ratio
        string[] words = text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);
        int arabicWords = words.Count(w => w.Any(IsArabic));
        double arabicWordRatio = words.Length > 0 ? (double)arabicWords / words.Length : 0;

        // Suspicious symbols: isolated digits, lone Latin chars, replacement chars
        int suspicious = text.Count(c =>
            c == '\uFFFD' ||
            (c >= 'A' && c <= 'Z') ||   // isolated Latin uppercase in Arabic text
            (c >= 'a' && c <= 'z'));     // isolated Latin lowercase in Arabic text
        double suspiciousRatio = nonWs > 0 ? (double)suspicious / nonWs : 0;

        // Length factor (saturates at 300+ non-whitespace chars)
        double lengthFactor = Math.Min(1.0, nonWs / 300.0);

        // Normalized confidence
        double normConf = Math.Clamp(tessConf, 0.0, 1.0);

        // Quality score (same formula as OcrQualityEvaluator)
        double score = (arabicCharRatio * 0.50)
                     + (normConf        * 0.35)
                     + (lengthFactor    * 0.15)
                     - (suspiciousRatio * 0.50);

        return new MetricsResult(
            Math.Round(arabicCharRatio, 4),
            Math.Round(arabicWordRatio, 4),
            Math.Round(suspiciousRatio, 4),
            Math.Round(Math.Clamp(score, 0.0, 1.0), 3));
    }

    private static bool IsArabic(char c) =>
        (c >= '\u0600' && c <= '\u06FF') ||
        (c >= '\u0750' && c <= '\u077F') ||
        (c >= '\uFB50' && c <= '\uFDFF') ||
        (c >= '\uFE70' && c <= '\uFEFF');

    // ──────────────────────────────────────────────────────────────────────────
    //  Print final report table to xUnit output
    // ──────────────────────────────────────────────────────────────────────────
    private void PrintReport(List<BenchmarkEntry> results, BenchmarkEntry baseline)
    {
        _out.WriteLine("\n");
        _out.WriteLine("==================================================================================================================================");
        _out.WriteLine("   OCR UPSAMPLING BENCHMARK — PAGE 110    (Lanczos resize, Tesseract 5.2.0 LSTM, ara, 300 DPI override)");
        _out.WriteLine("==================================================================================================================================");
        _out.WriteLine("| Scale | PSM | Prep | Crop | Size | Chars | Words | ArabicCh% | ArabicWd% | Susp% | Conf | Score | ms |");
        _out.WriteLine("| :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |");

        foreach (var r in results.OrderByDescending(r => r.QualityScore))
        {
            _out.WriteLine(
                $"| {r.ScaleLabel} | {r.PsmInt} | {r.Preprocessing} | {r.CropLabel} " +
                $"| {r.ImageWidth}×{r.ImageHeight} " +
                $"| {r.TotalChars} | {r.TotalWords} " +
                $"| {r.ArabicCharRatio:P1} | {r.ArabicWordRatio:P1} " +
                $"| {r.SuspiciousRatio:P1} | {r.TesseractConf:P1} " +
                $"| **{r.QualityScore:F3}** | {r.ElapsedMs} |");
        }

        _out.WriteLine("\n==================================================================================================================================");
        _out.WriteLine("   HEADER CROP IMPACT");
        _out.WriteLine("==================================================================================================================================");

        var cropResults = results.Where(r => r.HeaderCropped || r.CropLabel == "Full")
                                 .OrderBy(r => r.CropLabel)
                                 .ThenByDescending(r => r.QualityScore)
                                 .ToList();

        foreach (var r in cropResults.Take(12))
        {
            _out.WriteLine($"  {r.CropLabel,-8} | PSM {r.PsmInt} | {r.Preprocessing,-8} | Score {r.QualityScore:F3} | ArabicCh {r.ArabicCharRatio:P1} | Conf {r.TesseractConf:P1}");
        }

        var best = results.OrderByDescending(r => r.QualityScore).First();

        _out.WriteLine("\n==================================================================================================================================");
        _out.WriteLine("   BEST CONFIGURATION");
        _out.WriteLine("==================================================================================================================================");
        _out.WriteLine($"  Scale:        {best.ScaleLabel} ({best.ImageWidth}×{best.ImageHeight})");
        _out.WriteLine($"  PSM:          {best.PsmInt} ({best.PsmLabel})");
        _out.WriteLine($"  Preprocessing:{best.Preprocessing}");
        _out.WriteLine($"  Crop:         {best.CropLabel}");
        _out.WriteLine($"  Chars:        {best.TotalChars}");
        _out.WriteLine($"  ArabicCh:     {best.ArabicCharRatio:P1}");
        _out.WriteLine($"  ArabicWd:     {best.ArabicWordRatio:P1}");
        _out.WriteLine($"  Confidence:   {best.TesseractConf:P1}");
        _out.WriteLine($"  Quality:      {best.QualityScore:F3}");
        _out.WriteLine($"  Baseline:     {baseline.QualityScore:F3} ({baseline.ScaleLabel}, PSM {baseline.PsmInt})");
        _out.WriteLine($"  Improvement:  +{best.QualityScore - baseline.QualityScore:F3}");

        _out.WriteLine("\n----------------------------------------------------------------------------------------------------------------------------------");
        _out.WriteLine("   FIRST 500 CHARS OF BEST CONFIGURATION");
        _out.WriteLine("----------------------------------------------------------------------------------------------------------------------------------");
        _out.WriteLine(best.Excerpt);
        _out.WriteLine("==================================================================================================================================");
    }
}
