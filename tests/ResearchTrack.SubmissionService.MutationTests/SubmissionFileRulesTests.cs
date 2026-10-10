using ResearchTrack.SubmissionService.Features;

namespace ResearchTrack.SubmissionService.MutationTests;

public sealed class SubmissionFileRulesTests
{
    [Theory]
    [InlineData("report.pdf", "pdf")]
    [InlineData("chapter.DOCX", "docx")]
    [InlineData("slides.pptx", "pptx")]
    [InlineData("archive.zip", "zip")]
    public void ExtensionFromFileName_normalizes_supported_extensions(string fileName, string expected)
    {
        Assert.Equal(expected, SubmissionFileRules.ExtensionFromFileName(fileName));
    }

    [Theory]
    [InlineData("pdf", "application/pdf")]
    [InlineData("docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    [InlineData("zip", "application/zip")]
    [InlineData("zip", "application/x-zip-compressed")]
    public void ContentTypeMatchesExtension_accepts_known_pairs(string extension, string contentType)
    {
        Assert.True(SubmissionFileRules.ContentTypeMatchesExtension(extension, contentType));
    }

    [Fact]
    public void ContentTypeMatchesExtension_rejects_mismatched_content_type()
    {
        Assert.False(SubmissionFileRules.ContentTypeMatchesExtension("pdf", "application/zip"));
    }

    [Fact]
    public void Allowed_type_serialization_is_normalized_and_round_trips()
    {
        var serialized = SubmissionFileRules.SerializeAllowedTypes([".PDF", "docx", "pdf"]);
        Assert.Equal("docx,pdf", serialized);
        Assert.Equal(["docx", "pdf"], SubmissionFileRules.ParseAllowedTypes(serialized));
    }
}
