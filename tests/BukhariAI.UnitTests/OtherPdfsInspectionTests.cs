using UglyToad.PdfPig;
using Xunit;
using Xunit.Abstractions;

namespace BukhariAI.UnitTests;

public class OtherPdfsInspectionTests
{
    private readonly ITestOutputHelper _output;

    public OtherPdfsInspectionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void InspectOtherPdfs()
    {
        string[] pdfs =
        [
            @"c:\Users\mohamed\Desktop\asd.pdf",
            @"c:\Users\mohamed\Desktop\تفسير القرآن العظيم تفسير ابن كثير.pdf",
            @"c:\Users\mohamed\Desktop\شرح وحفظ وجه سورة النساء الأول.pdf",
            @"c:\Users\mohamed\Desktop\كتاب_الشرح_الامتحان_عربي_3ث_2023.pdf",
            @"c:\Users\mohamed\Downloads\رمز الويب JSON (JWT) - GeeksforGeeks.pdf"
        ];

        foreach (var file in pdfs)
        {
            if (!File.Exists(file)) continue;
            try
            {
                using var doc = PdfDocument.Open(file);
                var p1 = doc.GetPage(1);
                string text = p1.Text.Trim();
                _output.WriteLine($"FILE: {Path.GetFileName(file)} | Pages: {doc.NumberOfPages} | P1 DirectTextLength: {text.Length} | Preview: {(text.Length > 80 ? text[..80] : text)}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"FILE: {Path.GetFileName(file)} | ERROR: {ex.Message}");
            }
        }
    }
}
