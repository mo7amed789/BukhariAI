using System.Text;
using BukhariAI.Api.Helpers;
using Xunit;

namespace BukhariAI.UnitTests;

public class FileValidationSecurityTests
{
    [Fact]
    public void IsValidPdf_ValidPdfHeader_ReturnsTrue()
    {
        byte[] validPdf = Encoding.ASCII.GetBytes("%PDF-1.7\nSample content");
        using var ms = new MemoryStream(validPdf);

        bool isValid = FileValidationHelper.IsValidPdf(ms);
        Assert.True(isValid);
        Assert.Equal(0, ms.Position); // verifies stream position was preserved
    }

    [Fact]
    public void IsValidPdf_FakeExtensionWithInvalidBytes_ReturnsFalse()
    {
        // Attacker disguised a bash script or binary as a PDF
        byte[] fakePdf = Encoding.ASCII.GetBytes("#!/bin/bash\necho 'Malicious script'");
        using var ms = new MemoryStream(fakePdf);

        bool isValid = FileValidationHelper.IsValidPdf(ms);
        Assert.False(isValid);
    }

    [Fact]
    public void IsValidPdf_EmptyOrTooShortStream_ReturnsFalse()
    {
        using var emptyMs = new MemoryStream([]);
        Assert.False(FileValidationHelper.IsValidPdf(emptyMs));

        using var shortMs = new MemoryStream([0x25, 0x50]);
        Assert.False(FileValidationHelper.IsValidPdf(shortMs));
    }

    [Fact]
    public void IsValidImage_JpegHeader_ReturnsTrue()
    {
        byte[] jpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];
        using var ms = new MemoryStream(jpegBytes);

        bool isValid = FileValidationHelper.IsValidImage(ms);
        Assert.True(isValid);
        Assert.Equal(0, ms.Position);
    }

    [Fact]
    public void IsValidImage_PngHeader_ReturnsTrue()
    {
        byte[] pngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        using var ms = new MemoryStream(pngBytes);

        bool isValid = FileValidationHelper.IsValidImage(ms);
        Assert.True(isValid);
        Assert.Equal(0, ms.Position);
    }

    [Fact]
    public void IsValidImage_DisguisedExeOrText_ReturnsFalse()
    {
        byte[] exeBytes = [0x4D, 0x5A, 0x90, 0x00]; // MZ DOS header
        using var ms = new MemoryStream(exeBytes);

        bool isValid = FileValidationHelper.IsValidImage(ms);
        Assert.False(isValid);
    }
}
