using ResearchTrack.SubmissionService.Features;

namespace ResearchTrack.SubmissionService.Tests.Unit;

public sealed class SubmissionFileRulesBoundaryTests
{
    [Theory]
    [InlineData(" .PDF ", "pdf")]
    [InlineData("..DOCX", "docx")]
    [InlineData("zip", "zip")]
    public void NormalizeExtension_StripsDotsWhitespaceAndNormalizesCase(string input, string expected)
    {
        Assert.Equal(expected, SubmissionFileRules.NormalizeExtension(input));
    }

    [Theory]
    [InlineData("README")]
    [InlineData("file.")]
    public void ExtensionFromFileName_ReturnsNullWhenUsableExtensionIsMissing(string fileName)
    {
        Assert.Null(SubmissionFileRules.ExtensionFromFileName(fileName));
    }

    [Theory]
    [InlineData("pdf", "application/pdf")]
    [InlineData("DOCX", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    [InlineData("zip", "application/zip")]
    public void CanonicalContentType_ReturnsExpectedMimeType(string extension, string expected)
    {
        Assert.Equal(expected, SubmissionFileRules.CanonicalContentType(extension));
    }

    [Fact]
    public void CanonicalContentType_ReturnsNullForUnknownExtension()
    {
        Assert.Null(SubmissionFileRules.CanonicalContentType("exe"));
    }

    [Theory]
    [InlineData("pdf", " APPLICATION/PDF ")]
    [InlineData("ZIP", " application/x-zip-compressed ")]
    public void ContentTypeMatchesExtension_NormalizesContentTypeAndExtension(string extension, string contentType)
    {
        Assert.True(SubmissionFileRules.ContentTypeMatchesExtension(extension, contentType));
    }

    [Theory]
    [InlineData("pdf", "application/octet-stream")]
    [InlineData("exe", "application/octet-stream")]
    [InlineData("docx", "application/pdf")]
    public void ContentTypeMatchesExtension_RejectsUnknownOrMismatchedPairs(string extension, string contentType)
    {
        Assert.False(SubmissionFileRules.ContentTypeMatchesExtension(extension, contentType));
    }

    [Fact]
    public void ParseAllowedTypes_RemovesEmptyDuplicatesAndSortsDeterministically()
    {
        Assert.Equal(
            ["docx", "pdf", "zip"],
            SubmissionFileRules.ParseAllowedTypes(" .PDF,docx,pdf, ,ZIP "));
    }

    [Fact]
    public void SerializeAllowedTypes_RemovesEmptyDuplicatesAndSortsDeterministically()
    {
        Assert.Equal(
            "docx,pdf,zip",
            SubmissionFileRules.SerializeAllowedTypes(["ZIP", "", ".pdf", "docx", "PDF"]));
    }

    [Fact]
    public void SafeOriginalFileName_StripsPathAndHonorsExactLengthBoundary()
    {
        Assert.Equal("report.pdf", SubmissionFileRules.SafeOriginalFileName(" /tmp/report.pdf ", 20));
        Assert.Equal("12345", SubmissionFileRules.SafeOriginalFileName("12345", 5));
        Assert.Equal("12345", SubmissionFileRules.SafeOriginalFileName("123456789", 5));
    }
}
