using ResearchTrack.SubmissionService.Infrastructure;

namespace ResearchTrack.SubmissionService.Tests.Unit;

public sealed class DownloadContentDispositionTests
{
    [Fact]
    public void Em_dash_in_pdf_filename_has_ascii_fallback_and_unicode_name()
    {
        const string original = "Group 26 - ResearchTrack — Sprint 01 - Presentation.pdf";

        var disposition = DownloadContentDisposition.Create(original, inline: true);

        Assert.StartsWith(
            "inline; filename=\"Group 26 - ResearchTrack - Sprint 01 - Presentation.pdf\"; filename*=UTF-8''",
            disposition
        );
        Assert.Equal(original, ExtendedFileName(disposition));
        Assert.True(disposition.All(character => character <= 127));
    }

    [Fact]
    public void Download_uses_attachment_disposition()
    {
        var disposition = DownloadContentDisposition.Create("report.pdf", inline: false);

        Assert.Equal("attachment; filename=\"report.pdf\"; filename*=UTF-8''report.pdf", disposition);
    }

    [Theory]
    [InlineData("වාර්තාව.pdf", "download.pdf")]
    [InlineData("café – final.docx", "cafe - final.docx")]
    [InlineData("研究報告.zip", "download.zip")]
    [InlineData("🚀-slides.pptx", "slides.pptx")]
    public void International_names_are_preserved_without_non_ascii_headers(string original, string fallback)
    {
        var disposition = DownloadContentDisposition.Create(original, inline: false);

        Assert.Contains($"filename=\"{fallback}\"", disposition);
        Assert.Equal(original, ExtendedFileName(disposition));
        Assert.True(disposition.All(character => character <= 127));
    }

    [Fact]
    public void Path_components_do_not_escape_into_the_download_filename()
    {
        var disposition = DownloadContentDisposition.Create(@"C:\uploads\draft\report.pdf", inline: false);

        Assert.Contains("filename=\"report.pdf\"", disposition);
        Assert.Equal("report.pdf", ExtendedFileName(disposition));
    }

    [Fact]
    public void Header_metacharacters_and_newlines_cannot_inject_response_headers()
    {
        var disposition = DownloadContentDisposition.Create(
            "draft\"; evil=1\r\nX-Injected: yes.pdf",
            inline: true
        );

        Assert.True(disposition.All(character => character <= 127 && character is not '\r' and not '\n'));
        Assert.StartsWith("inline; filename=\"draft evil1X-Injected yes.pdf\"; filename*=", disposition);
        Assert.Equal("draft\"; evil=1X-Injected: yes.pdf", ExtendedFileName(disposition));
        Assert.DoesNotContain("\r", disposition);
        Assert.DoesNotContain("\n", disposition);
    }

    [Theory]
    [InlineData("", "download")]
    [InlineData("../", "download")]
    [InlineData("..", "download")]
    public void Empty_or_invalid_names_have_a_safe_fallback(string original, string expected)
    {
        var disposition = DownloadContentDisposition.Create(original, inline: false);

        Assert.Equal(expected, ExtendedFileName(disposition));
        Assert.Contains($"filename=\"{expected}\"", disposition);
    }

    [Fact]
    public void Unpaired_surrogate_does_not_break_rfc_5987_encoding()
    {
        var disposition = DownloadContentDisposition.Create("report\uD800.pdf", inline: true);

        Assert.Equal("report.pdf", ExtendedFileName(disposition));
    }

    private static string ExtendedFileName(string disposition)
    {
        const string prefix = "filename*=UTF-8''";
        var offset = disposition.IndexOf(prefix, StringComparison.Ordinal);
        Assert.True(offset >= 0);
        return Uri.UnescapeDataString(disposition[(offset + prefix.Length)..]);
    }
}
