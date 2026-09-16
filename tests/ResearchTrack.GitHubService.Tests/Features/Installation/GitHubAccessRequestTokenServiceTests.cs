using ResearchTrack.GitHubService.Configuration;
using ResearchTrack.GitHubService.Features.Installation;

namespace ResearchTrack.GitHubService.Tests.Features.Installation;

public sealed class GitHubAccessRequestTokenServiceTests
{
    private static readonly Guid RequestId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void Request_token_can_be_recreated_and_verified_without_storing_plaintext()
    {
        var service = CreateService();

        var created = service.CreateRequestToken(RequestId);

        Assert.NotEmpty(created.Nonce);
        Assert.NotEmpty(created.Token);
        Assert.Equal(service.Hash(created.Token), created.Hash);
        Assert.Equal(created.Token, service.RecreateRequestToken(RequestId, created.Nonce));
        Assert.True(service.IsWellFormed(created.Token, RequestId, created.Nonce, resultToken: false));
    }

    [Fact]
    public void Result_token_has_a_separate_purpose_and_cannot_be_reused_as_request_token()
    {
        var service = CreateService();

        var result = service.CreateResultToken(RequestId);

        Assert.True(service.IsWellFormed(result.Token, RequestId, result.Nonce, resultToken: true));
        Assert.False(service.IsWellFormed(result.Token, RequestId, result.Nonce, resultToken: false));
    }

    [Fact]
    public void Tampered_token_is_rejected()
    {
        var service = CreateService();
        var created = service.CreateRequestToken(RequestId);
        var tampered = created.Token + "x";

        Assert.False(service.IsWellFormed(tampered, RequestId, created.Nonce, resultToken: false));
    }

    private static GitHubAccessRequestTokenService CreateService() => new(
        new GitHubAppOptions(
            12345,
            "researchtrack-test",
            "Iv1.test-client",
            "test-client-secret",
            "/tmp/test.pem",
            new Uri("https://api.example.test/api/github/access-source/install/callback"),
            new Uri("https://app.example.test/"),
            TimeSpan.FromMinutes(10),
            TimeSpan.FromHours(24)));
}
