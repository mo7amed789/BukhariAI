/// Minimal standalone OCR diagnostic tool for Page 110.
/// Purpose: Extract raw image from page 110, save it, report dimensions,
/// then run Tesseract in-process and report full OCR output.
/// This does NOT touch the lesson endpoint.

using System.IO.Compression;
using Tesseract;
using UglyToad.PdfPig;

const string PdfPath = @"c:\Users\mohamed\Desktop\s.bokhari.5.pdf";
const string OutputDir = @"c:\Users\mohamed\Desktop\ocr-diagnostic-page110";
const int PageNum = 110;
const string TessdataPath = @"c:\Users\mohamed\source\repos\BukhariAI\src\BukhariAI.Api\bin\Debug\net10.0\tessdata";

Directory.CreateDirectory(OutputDir);

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("=============================================================");
Console.WriteLine("  BUKHARI AI — OCR DIAGNOSTIC: Page 110");
Console.WriteLine("=============================================================");
Console.WriteLine();

// ── STEP 1: Open PDF ──────────────────────────────────────────────────────────
if (!File.Exists(PdfPath))
{
    Console.WriteLine($"[ERROR] PDF not found at: {PdfPath}");
    return;
}

Console.WriteLine($"PDF:         {PdfPath}");
Console.WriteLine($"Page:        {PageNum}");
Console.WriteLine($"Tessdata:    {TessdataPath}");
Console.WriteLine($"Output dir:  {OutputDir}");
Console.WriteLine();

using var doc = PdfDocument.Open(PdfPath);
Console.WriteLine($"Total pages in PDF: {doc.NumberOfPages}");
var page = doc.GetPage(PageNum);

// ── STEP 2: Extract images from the page ──────────────────────────────────────
var images = page.GetImages().ToList();
Console.WriteLine($"Images on page {PageNum}: {images.Count}");

if (images.Count == 0)
{
    Console.WriteLine("[FATAL] No images found on this page. Native text mode only.");
    Console.WriteLine($"Native text length: {page.Text?.Length ?? 0}");
    return;
}

// Take largest image by pixel area
var img = images.OrderByDescending(i => i.WidthInSamples * i.HeightInSamples).First();
Console.WriteLine();
Console.WriteLine($"Largest image on page:");
Console.WriteLine($"  Width in samples:  {img.WidthInSamples}");
Console.WriteLine($"  Height in samples: {img.HeightInSamples}");
Console.WriteLine($"  Bits per component:{img.BitsPerComponent}");

// ── STEP 3: Extract and decode raw bytes ───────────────────────────────────────
byte[] rawBytes = img.RawBytes.ToArray();
Console.WriteLine();
Console.WriteLine($"Raw bytes from PdfPig: {rawBytes.Length:N0} bytes");

// Inspect first 4 bytes to identify format
string header4 = BitConverter.ToString(rawBytes.Take(4).ToArray());
Console.WriteLine($"First 4 bytes (hex):   {header4}");

bool isZLib = rawBytes.Length > 2 &&
              rawBytes[0] == 0x78 &&
              (rawBytes[1] == 0x9C || rawBytes[1] == 0xDA || rawBytes[1] == 0x01 || rawBytes[1] == 0x5E);

bool isJpeg = rawBytes.Length > 2 && rawBytes[0] == 0xFF && rawBytes[1] == 0xD8;
bool isPng  = rawBytes.Length > 4 && rawBytes[0] == 0x89 && rawBytes[1] == 0x50;

Console.WriteLine($"Detected format:       {(isZLib ? "ZLib-wrapped" : isJpeg ? "JPEG" : isPng ? "PNG" : "Unknown")}");

byte[] imageBytes;

if (isZLib)
{
    Console.WriteLine("Decompressing ZLib stream...");
    using var rawStream = new MemoryStream(rawBytes);
    using var zlib = new ZLibStream(rawStream, CompressionMode.Decompress);
    using var outStream = new MemoryStream();
    zlib.CopyTo(outStream);
    imageBytes = outStream.ToArray();
    Console.WriteLine($"Decompressed size:     {imageBytes.Length:N0} bytes");

    string header4After = BitConverter.ToString(imageBytes.Take(4).ToArray());
    Console.WriteLine($"First 4 bytes after decompress (hex): {header4After}");
    bool isJpegAfter = imageBytes[0] == 0xFF && imageBytes[1] == 0xD8;
    Console.WriteLine($"Format after decompress: {(isJpegAfter ? "JPEG" : "Unknown")}");
}
else
{
    imageBytes = rawBytes;
    Console.WriteLine("No ZLib decompression needed.");
}

// ── STEP 4: Try TryGetPng ─────────────────────────────────────────────────────
byte[]? pngBytes = null;
if (img.TryGetPng(out byte[]? tryPngBytes) && tryPngBytes is { Length: > 0 })
{
    pngBytes = tryPngBytes;
    Console.WriteLine($"TryGetPng() succeeded: {pngBytes.Length:N0} bytes");
}
else
{
    Console.WriteLine("TryGetPng() returned null/empty — using decompressed bytes.");
}

byte[] finalImageBytes = pngBytes ?? imageBytes;

// ── STEP 5: Save image to disk for visual inspection ──────────────────────────
string ext = (finalImageBytes.Length > 2 && finalImageBytes[0] == 0x89 && finalImageBytes[1] == 0x50) ? "png"
           : (finalImageBytes.Length > 2 && finalImageBytes[0] == 0xFF && finalImageBytes[1] == 0xD8) ? "jpg"
           : "bin";

string imageFilePath = Path.Combine(OutputDir, $"page-{PageNum}-raw.{ext}");
File.WriteAllBytes(imageFilePath, finalImageBytes);
Console.WriteLine();
Console.WriteLine($"[SAVED] Image: {imageFilePath} ({new FileInfo(imageFilePath).Length:N0} bytes)");

// ── STEP 6: Load with Leptonica to get true pixel dimensions ─────────────────
Console.WriteLine();
Console.WriteLine("Loading image into Leptonica Pix...");
using var rawPix = Pix.LoadFromMemory(finalImageBytes);
Console.WriteLine($"Leptonica Pix dimensions:  {rawPix.Width} x {rawPix.Height} pixels");
Console.WriteLine($"Leptonica Pix depth (bpp): {rawPix.Depth}");
Console.WriteLine($"Leptonica native XRes:     {rawPix.XRes} DPI");
Console.WriteLine($"Leptonica native YRes:     {rawPix.YRes} DPI");

// Inject 300 DPI (scanned images often embed 0 or 72 DPI)
rawPix.XRes = 300;
rawPix.YRes = 300;

// ── STEP 7: Convert to grayscale ─────────────────────────────────────────────
using var grayPix = rawPix.Depth == 32 || rawPix.Depth == 24
    ? rawPix.ConvertRGBToGray()
    : rawPix.Clone();
grayPix.XRes = 300;
grayPix.YRes = 300;

// Save grayscale image too
string grayPath = Path.Combine(OutputDir, $"page-{PageNum}-gray.png");
grayPix.Save(grayPath);
Console.WriteLine($"[SAVED] Grayscale image: {grayPath}");

// ── STEP 8: Run Tesseract in-process ─────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("=============================================================");
Console.WriteLine("  TESSERACT IN-PROCESS OCR");
Console.WriteLine("=============================================================");
Console.WriteLine($"Tessdata path: {TessdataPath}");

bool tessdataExists = File.Exists(Path.Combine(TessdataPath, "ara.traineddata"));
Console.WriteLine($"ara.traineddata exists: {tessdataExists}");
if (!tessdataExists)
{
    Console.WriteLine("[FATAL] ara.traineddata NOT FOUND. OCR cannot proceed.");
    return;
}

Console.WriteLine("PSM: Auto (3)");
Console.WriteLine("Engine mode: LSTM only");
Console.WriteLine();

using var engine = new TesseractEngine(TessdataPath, "ara", EngineMode.LstmOnly);

// Pass 1: raw grayscale — must be disposed before Pass 2
string rawText1;
float conf1;
{
    using var ocrPage1 = engine.Process(grayPix, PageSegMode.Auto);
    rawText1 = ocrPage1.GetText()?.Trim() ?? string.Empty;
    conf1    = ocrPage1.GetMeanConfidence();
}
Console.WriteLine($"Pass 1 (Grayscale, PSM Auto):  {rawText1.Length} chars, confidence {conf1:P1}");
Console.WriteLine();

// Pass 2: Otsu binarization
using var binaryPix = grayPix.BinarizeOtsuAdaptiveThreshold(200, 200, 0, 0, 0.1f);
binaryPix.XRes = 300;
binaryPix.YRes = 300;
string binaryPath = Path.Combine(OutputDir, $"page-{PageNum}-otsu.png");
binaryPix.Save(binaryPath);
Console.WriteLine($"[SAVED] Otsu binarized image: {binaryPath}");

string rawText2;
float conf2;
{
    using var ocrPage2 = engine.Process(binaryPix, PageSegMode.SingleColumn);
    rawText2 = ocrPage2.GetText()?.Trim() ?? string.Empty;
    conf2    = ocrPage2.GetMeanConfidence();
}
Console.WriteLine($"Pass 2 (Otsu, PSM SingleColumn): {rawText2.Length} chars, confidence {conf2:P1}");
Console.WriteLine();

// Choose the best pass
string bestText = rawText1.Length >= rawText2.Length ? rawText1 : rawText2;
float bestConf  = rawText1.Length >= rawText2.Length ? conf1 : conf2;

// ── STEP 9: Compute Arabic character ratio ─────────────────────────────────────
int arabicChars = bestText.Count(c =>
    (c >= '\u0600' && c <= '\u06FF') ||
    (c >= '\u0750' && c <= '\u077F') ||
    (c >= '\uFB50' && c <= '\uFDFF') ||
    (c >= '\uFE70' && c <= '\uFEFF'));
int nonWs = bestText.Count(c => !char.IsWhiteSpace(c));
double arabicRatio = nonWs > 0 ? (double)arabicChars / nonWs : 0;

// Save raw text
string rawTextPath = Path.Combine(OutputDir, "raw-ocr.txt");
File.WriteAllText(rawTextPath, bestText, System.Text.Encoding.UTF8);

// ── STEP 10: Print the diagnostic report ──────────────────────────────────────
Console.WriteLine("=============================================================");
Console.WriteLine("  DIAGNOSTIC REPORT");
Console.WriteLine("=============================================================");
Console.WriteLine($"OCR engine:              Tesseract 5.2.0 (.NET wrapper, in-process)");
Console.WriteLine($"Tesseract executable:    None — runs via P/Invoke (tesseract50.dll)");
Console.WriteLine($"Language:                ara");
Console.WriteLine($"DPI used:                300");
Console.WriteLine($"Image dimensions:        {rawPix.Width} x {rawPix.Height} pixels");
Console.WriteLine($"PSM (best pass):         Auto (Pass 1) + SingleColumn (Pass 2)");
Console.WriteLine($"Exit code:               N/A (in-process, no subprocess)");
Console.WriteLine($"Raw text length:         {bestText.Length} characters");
Console.WriteLine($"Arabic character ratio:  {arabicRatio:P1}");
Console.WriteLine($"Tesseract confidence:    {bestConf:P1}");
Console.WriteLine();
Console.WriteLine("First 500 characters of raw OCR output:");
Console.WriteLine("---");
Console.WriteLine(bestText.Length > 500 ? bestText[..500] : bestText);
Console.WriteLine("---");
Console.WriteLine();
Console.WriteLine("=============================================================");
Console.WriteLine("  IMAGES SAVED FOR VISUAL INSPECTION");
Console.WriteLine("=============================================================");
Console.WriteLine($"  {imageFilePath}");
Console.WriteLine($"  {grayPath}");
Console.WriteLine($"  {binaryPath}");
Console.WriteLine($"  {rawTextPath}");
Console.WriteLine();
Console.WriteLine("Open the saved images to verify whether the rendered image is");
Console.WriteLine("visually clear before diagnosing Tesseract configuration.");
