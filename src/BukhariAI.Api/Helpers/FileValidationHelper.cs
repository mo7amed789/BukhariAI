namespace BukhariAI.Api.Helpers;

public static class FileValidationHelper
{
    private static readonly byte[] PdfMagicBytes = [0x25, 0x50, 0x44, 0x46, 0x2D]; // %PDF-
    private static readonly byte[] JpegMagicBytes = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngMagicBytes = [0x89, 0x50, 0x4E, 0x47];

    public static bool IsValidPdf(Stream stream)
    {
        if (stream == null || stream.Length < 5) return false;

        long originalPosition = stream.CanSeek ? stream.Position : 0;
        try
        {
            Span<byte> header = stackalloc byte[5];
            int read = stream.Read(header);
            if (read < 5) return false;

            return header.SequenceEqual(PdfMagicBytes);
        }
        finally
        {
            if (stream.CanSeek) stream.Position = originalPosition;
        }
    }

    public static bool IsValidImage(Stream stream)
    {
        if (stream == null || stream.Length < 4) return false;

        long originalPosition = stream.CanSeek ? stream.Position : 0;
        try
        {
            Span<byte> header = stackalloc byte[4];
            int read = stream.Read(header);
            if (read < 4) return false;

            if (header.Slice(0, 3).SequenceEqual(JpegMagicBytes)) return true;
            if (header.SequenceEqual(PngMagicBytes)) return true;

            return false;
        }
        finally
        {
            if (stream.CanSeek) stream.Position = originalPosition;
        }
    }
}
