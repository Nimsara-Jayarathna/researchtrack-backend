using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using ResearchTrack.SubmissionService.Configuration;
using ResearchTrack.SubmissionService.Infrastructure;

namespace ResearchTrack.SubmissionService.Tests.Unit;

public sealed class S3DownloadGrantTests
{
    [Theory]
    [InlineData(true, "inline")]
    [InlineData(false, "attachment")]
    public async Task Presigned_url_contains_safe_content_disposition(bool inline, string expectedDisposition)
    {
        // Presigning is local cryptographic work; this test never calls the S3 endpoint.
        using var s3 = new AmazonS3Client(
            new BasicAWSCredentials("fake-test-access-key", "fake-test-secret-key"),
            RegionEndpoint.USEast1
        );
        var options = new StorageOptions { Bucket = "test-bucket", PresignedUrlExpirySeconds = 300 };
        var storage = new S3ObjectStorageService(s3, options, TimeProvider.System);
        const string original = "Group 26 - ResearchTrack — Sprint 01 - Presentation.pdf";

        var grant = await storage.CreateDownloadGrantAsync(
            "projects/abc/presentation.pdf",
            original,
            inline,
            TestContext.Current.CancellationToken
        );

        var uri = new Uri(grant.Url);
        const string key = "response-content-disposition=";
        var parameter = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Single(item => item.StartsWith(key, StringComparison.OrdinalIgnoreCase));
        var headerValue = Uri.UnescapeDataString(parameter[key.Length..]);

        Assert.StartsWith($"{expectedDisposition}; filename=\"Group 26 - ResearchTrack - Sprint 01 - Presentation.pdf\"", headerValue);
        Assert.Contains("filename*=UTF-8''Group%2026", headerValue);
        Assert.True(headerValue.All(character => character <= 127));
    }
}
