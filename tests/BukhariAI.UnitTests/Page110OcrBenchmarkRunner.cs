using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BukhariAI.Infrastructure.Ocr;
using Tesseract;
using UglyToad.PdfPig;
using Xunit;
using Xunit.Abstractions;

namespace BukhariAI.UnitTests;

public class Page110OcrBenchmarkRunner
{
    private readonly ITestOutputHelper _output;

    public Page110OcrBenchmarkRunner(ITestOutputHelper output)
    {
        _output = output;
    }

    public sealed class BenchmarkMetadata
    {
        public string ConfigName { get; set; } = string.Empty;
        public int Dpi { get; set; }
        public string Psm { get; set; } = string.Empty;
        public string Preprocessing { get; set; } = string.Empty;
        public int TotalCharacters { get; set; }
        public int TotalWords { get; set; }
        public double ArabicCharRatio { get; set; }
        public double ArabicWordRatio { get; set; }
        public double SuspiciousSymbolRatio { get; set; }
        public int IsolatedLatinChars { get; set; }
        public int IsolatedDigits { get; set; }
        public double AverageWordLength { get; set; }
        public float TesseractConfidence { get; set; }
        public double HeuristicQualityScore { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public string Excerpt { get; set; } = string.Empty;
    }

    [Fact]
    public void RunComprehensivePage110Benchmark()
    {
        string pdfPath = @"c:\Users\mohamed\Desktop\s.bokhari.5.pdf";
        if (!File.Exists(pdfPath)) return;

        string tessdataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
        string benchmarkOutputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ocr-benchmark", "page-110");
        Directory.CreateDirectory(benchmarkOutputDir);

        using var doc = PdfDocument.Open(pdfPath);
        var page = doc.GetPage(110);
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

        List<BenchmarkMetadata> results = [];

        using var engine = new TesseractEngine(tessdataPath, "ara", EngineMode.LstmOnly);

        // Define matrix of configurations
        var configurations = new (string Name, PageSegMode Psm, string Preproc, int Dpi)[]
        {
            // Part 1: PSM comparison (Baseline raw 300 DPI)
            ("PSM 3 (Auto - Raw 300DPI)", PageSegMode.Auto, "raw", 300),
            ("PSM 4 (SingleColumn - Raw 300DPI)", PageSegMode.SingleColumn, "raw", 300),
            ("PSM 6 (SingleBlock - Raw 300DPI)", PageSegMode.SingleBlock, "raw", 300),

            // Part 2: Grayscale preprocessing
            ("PSM 3 (Grayscale 300DPI)", PageSegMode.Auto, "gray", 300),
            ("PSM 4 (Grayscale 300DPI)", PageSegMode.SingleColumn, "gray", 300),
            ("PSM 6 (Grayscale 300DPI)", PageSegMode.SingleBlock, "gray", 300),

            // Part 3: Grayscale + Deskew
            ("PSM 3 (Grayscale+Deskew 300DPI)", PageSegMode.Auto, "gray_deskew", 300),
            ("PSM 4 (Grayscale+Deskew 300DPI)", PageSegMode.SingleColumn, "gray_deskew", 300),
            ("PSM 6 (Grayscale+Deskew 300DPI)", PageSegMode.SingleBlock, "gray_deskew", 300),

            // Part 4: Grayscale + Otsu Adaptive Binarization
            ("PSM 3 (Otsu Adaptive 300DPI)", PageSegMode.Auto, "otsu", 300),
            ("PSM 4 (Otsu Adaptive 300DPI)", PageSegMode.SingleColumn, "otsu", 300),
            ("PSM 6 (Otsu Adaptive 300DPI)", PageSegMode.SingleBlock, "otsu", 300),

            // Part 5: DPI variations (200, 300, 400 DPI) on best configurations
            ("DPI 200 (Grayscale - PSM 6)", PageSegMode.SingleBlock, "gray", 200),
            ("DPI 300 (Grayscale - PSM 6)", PageSegMode.SingleBlock, "gray", 300),
            ("DPI 400 (Grayscale - PSM 6)", PageSegMode.SingleBlock, "gray", 400),
            ("DPI 200 (Otsu - PSM 4)", PageSegMode.SingleColumn, "otsu", 200),
            ("DPI 300 (Otsu - PSM 4)", PageSegMode.SingleColumn, "otsu", 300),
            ("DPI 400 (Otsu - PSM 4)", PageSegMode.SingleColumn, "otsu", 400),
        };

        foreach (var (name, psm, preproc, dpi) in configurations)
        {
            var sw = Stopwatch.StartNew();
            using var pix = PreparePix(jpegBytes, preproc, dpi);
            using var ocrPage = engine.Process(pix, psm);

            string rawText = ocrPage.GetText()?.Trim() ?? string.Empty;
            float confidence = ocrPage.GetMeanConfidence();
            sw.Stop();

            string normalizedText = ArabicTextNormalizer.Normalize(rawText);

            var meta = ComputeMetrics(name, dpi, psm.ToString(), preproc, rawText, confidence, sw.ElapsedMilliseconds);
            results.Add(meta);

            // Save to benchmark directory
            string safeDirName = SanitizeFolderName(name);
            string configFolder = Path.Combine(benchmarkOutputDir, safeDirName);
            Directory.CreateDirectory(configFolder);

            File.WriteAllText(Path.Combine(configFolder, "raw.txt"), rawText, Encoding.UTF8);
            File.WriteAllText(Path.Combine(configFolder, "normalized.txt"), normalizedText, Encoding.UTF8);
            File.WriteAllText(Path.Combine(configFolder, "metadata.json"), JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        }

        // Print Structured Markdown Table to Test Output
        _output.WriteLine("\n==========================================================================================================================");
        _output.WriteLine("                                    PAGE 110 OCR BENCHMARK RESULTS TABLE");
        _output.WriteLine("==========================================================================================================================");
        _output.WriteLine("| Configuration | DPI | PSM | Chars | Words | Arabic Char % | Arabic Word % | Suspicious % | Conf | Score | Time (ms) |");
        _output.WriteLine("| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |");

        foreach (var r in results)
        {
            _output.WriteLine($"| {r.ConfigName} | {r.Dpi} | {r.Psm} | {r.TotalCharacters} | {r.TotalWords} | {r.ArabicCharRatio:P1} | {r.ArabicWordRatio:P1} | {r.SuspiciousSymbolRatio:P1} | {r.TesseractConfidence:P1} | **{r.HeuristicQualityScore:F2}** | {r.ElapsedMilliseconds}ms |");
        }

        _output.WriteLine("\n==========================================================================================================================");
        _output.WriteLine("                                         SAMPLE RECOGNITION EXCERPTS");
        _output.WriteLine("==========================================================================================================================");
        foreach (var r in results.Take(6))
        {
            _output.WriteLine($"\n--- [{r.ConfigName}] ---");
            _output.WriteLine(r.Excerpt);
        }

        Assert.NotEmpty(results);
    }

    private static Pix PreparePix(byte[] jpegBytes, string preproc, int dpi)
    {
        var rawPix = Pix.LoadFromMemory(jpegBytes);
        rawPix.XRes = dpi;
        rawPix.YRes = dpi;

        if (preproc == "raw")
        {
            return rawPix;
        }

        var grayPix = rawPix.Depth == 32 || rawPix.Depth == 24 ? rawPix.ConvertRGBToGray() : rawPix.Clone();
        grayPix.XRes = dpi;
        grayPix.YRes = dpi;
        rawPix.Dispose();

        if (preproc == "gray")
        {
            return grayPix;
        }

        if (preproc == "gray_deskew")
        {
            var deskewed = grayPix.Deskew();
            if (deskewed != null)
            {
                deskewed.XRes = dpi;
                deskewed.YRes = dpi;
                grayPix.Dispose();
                return deskewed;
            }
            return grayPix;
        }

        if (preproc == "otsu")
        {
            var binarized = grayPix.BinarizeOtsuAdaptiveThreshold(200, 200, 0, 0, 0.1f);
            binarized.XRes = dpi;
            binarized.YRes = dpi;
            grayPix.Dispose();
            return binarized;
        }

        return grayPix;
    }

    private static BenchmarkMetadata ComputeMetrics(
        string configName,
        int dpi,
        string psm,
        string preproc,
        string rawText,
        float confidence,
        long elapsedMs)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return new BenchmarkMetadata
            {
                ConfigName = configName,
                Dpi = dpi,
                Psm = psm,
                Preprocessing = preproc,
                ElapsedMilliseconds = elapsedMs
            };
        }

        string trimmed = rawText.Trim();
        var words = trimmed.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        int totalWords = words.Length;
        int totalChars = trimmed.Length;
        int nonWsChars = trimmed.Count(c => !char.IsWhiteSpace(c));

        int arabicChars = trimmed.Count(IsArabicChar);
        double arabicCharRatio = nonWsChars > 0 ? (double)arabicChars / nonWsChars : 0;

        int arabicWords = words.Count(w => w.Any(IsArabicChar));
        double arabicWordRatio = totalWords > 0 ? (double)arabicWords / totalWords : 0;

        int isolatedLatin = words.Count(w => w.Length == 1 && ((w[0] >= 'a' && w[0] <= 'z') || (w[0] >= 'A' && w[0] <= 'Z')));
        int isolatedDigits = words.Count(w => w.Length == 1 && char.IsDigit(w[0]));
        int suspiciousSymbols = trimmed.Count(c => c == '\uFFFD' || c == '~' || c == '^' || c == '|' || c == '§' || c == '©' || c == '«' && false);

        double suspiciousRatio = nonWsChars > 0 ? (double)(isolatedLatin + isolatedDigits + suspiciousSymbols) / nonWsChars : 0;
        double avgWordLength = totalWords > 0 ? (double)nonWsChars / totalWords : 0;

        // Heuristic quality score: 0.0 to 1.0
        double normConf = Math.Clamp(confidence, 0.0, 1.0);
        double score = (arabicCharRatio * 0.45) + (arabicWordRatio * 0.25) + (normConf * 0.20) + Math.Min(0.10, totalChars / 20000.0) - (suspiciousRatio * 0.50);
        score = Math.Clamp(score, 0.0, 1.0);

        string excerpt = trimmed.Length > 280 ? trimmed[..280].Replace("\r", "").Replace("\n", " ") : trimmed;

        return new BenchmarkMetadata
        {
            ConfigName = configName,
            Dpi = dpi,
            Psm = psm,
            Preprocessing = preproc,
            TotalCharacters = totalChars,
            TotalWords = totalWords,
            ArabicCharRatio = Math.Round(arabicCharRatio, 3),
            ArabicWordRatio = Math.Round(arabicWordRatio, 3),
            SuspiciousSymbolRatio = Math.Round(suspiciousRatio, 3),
            IsolatedLatinChars = isolatedLatin,
            IsolatedDigits = isolatedDigits,
            AverageWordLength = Math.Round(avgWordLength, 2),
            TesseractConfidence = confidence,
            HeuristicQualityScore = Math.Round(score, 3),
            ElapsedMilliseconds = elapsedMs,
            Excerpt = excerpt
        };
    }

    private static bool IsArabicChar(char c)
    {
        return (c >= '\u0600' && c <= '\u06FF') ||
               (c >= '\u0750' && c <= '\u077F') ||
               (c >= '\uFB50' && c <= '\uFDFF') ||
               (c >= '\uFE70' && c <= '\uFEFF');
    }

    private static string SanitizeFolderName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (char c in name)
        {
            sb.Append(invalid.Contains(c) || c == ' ' || c == '(' || c == ')' || c == '+' ? '_' : c);
        }
        return sb.ToString().Trim('_');
    }
}
