using System.Text.Json;
using ResearchTrack.GitHubService.Contracts;

namespace ResearchTrack.GitHubService.Tests.Contracts;

public sealed class GitHubInstallStartResponseSerializationTests
{
    [Fact]
    public void Serialize_uses_stable_github_authorize_url_contract_name()
    {
        var response = new GitHubInstallStartResponse(
            Guid.Parse("a3e65862-60d1-484f-bc95-e1b0fccccd5f"),
            "https://github.com/apps/research-track/installations/new?state=safe",
            "INSTALLATION_DIRECT",
            new DateTime(2026, 9, 13, 3, 49, 19, DateTimeKind.Utc));

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);

        Assert.True(document.RootElement.TryGetProperty("githubAuthorizeUrl", out var authorizeUrl));
        Assert.Equal(
            "https://github.com/apps/research-track/installations/new?state=safe",
            authorizeUrl.GetString());
        Assert.False(document.RootElement.TryGetProperty("gitHubAuthorizeUrl", out _));
    }
}
