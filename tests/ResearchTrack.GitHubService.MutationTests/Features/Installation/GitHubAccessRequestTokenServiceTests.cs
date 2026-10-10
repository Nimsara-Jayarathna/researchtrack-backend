using ResearchTrack.GitHubService.Configuration;
using ResearchTrack.GitHubService.Features.Installation;

namespace ResearchTrack.GitHubService.MutationTests.Features.Installation;

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



    [Fact]
    public void Request_and_result_tokens_have_distinct_prefixes_and_stable_lowercase_hashes()
    {
        var service = CreateService();
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var request = service.CreateRequestToken(id);
        var result = service.CreateResultToken(id);

        Assert.StartsWith($"ga1.{id:N}.", request.Token);
        Assert.StartsWith($"gr1.{id:N}.", result.Token);
        Assert.Equal(64, request.Hash.Length);
        Assert.Equal(request.Hash, request.Hash.ToLowerInvariant());
        Assert.Equal(service.Hash(request.Token), request.Hash);
        Assert.NotEqual(request.Token, result.Token);
    }

    [Fact]
    public void Verification_rejects_wrong_request_id_nonce_and_token_purpose()
    {
        var service = CreateService();
        var id = Guid.NewGuid();
        var created = service.CreateRequestToken(id);

        Assert.True(service.IsWellFormed(created.Token, id, created.Nonce, resultToken: false));
        Assert.False(service.IsWellFormed(created.Token, Guid.NewGuid(), created.Nonce, resultToken: false));
        Assert.False(service.IsWellFormed(created.Token, id, created.Nonce + "x", resultToken: false));
        Assert.False(service.IsWellFormed(created.Token, id, created.Nonce, resultToken: true));
    }

    [Fact]
    public void Recreated_tokens_are_deterministic_for_same_context_and_change_when_nonce_changes()
    {
        var service = CreateService();
        var id = Guid.NewGuid();
        var created = service.CreateRequestToken(id);

        Assert.Equal(created.Token, service.RecreateRequestToken(id, created.Nonce));
        Assert.NotEqual(created.Token, service.RecreateRequestToken(id, created.Nonce + "x"));
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
