namespace BukhariAI.Infrastructure.Pdf;

public sealed class PdfExtractionOptions
{
    public const string SectionName = "PdfExtraction";

    public int MaximumPageRange { get; set; } = 20;

    public int RenderWidth { get; set; } = 1800;

    public int RenderHeight { get; set; } = 2400;

    /// <summary>JPEG quality used for Vision page images (1-100).</summary>
    public int VisionImageQuality { get; set; } = 85;
}
