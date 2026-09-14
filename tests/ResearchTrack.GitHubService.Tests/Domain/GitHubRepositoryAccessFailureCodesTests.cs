using ResearchTrack.GitHubService.Domain;

namespace ResearchTrack.GitHubService.Tests.Domain;

public sealed class GitHubRepositoryAccessFailureCodesTests
{
    [Theory]
    [InlineData(GitHubRepositoryAccessFailureCodes.Cancelled)]
    [InlineData(GitHubRepositoryAccessFailureCodes.InvalidInstallation)]
    [InlineData(GitHubRepositoryAccessFailureCodes.RepositoryNotAccessible)]
    [InlineData(GitHubRepositoryAccessFailureCodes.RequestExpired)]
    [InlineData(GitHubRepositoryAccessFailureCodes.CompletionInconsistent)]
    public void Allow_list_accepts_known_non_sensitive_failure_codes(string failureCode)
    {
        Assert.True(GitHubRepositoryAccessFailureCodes.IsSafe(failureCode));
        Assert.Equal(failureCode, GitHubRepositoryAccessFailureCodes.RequireSafe(failureCode));
    }

    [Theory]
    [InlineData("raw-github-provider-error: secret diagnostic")]
    [InlineData("token=owner-grant-secret")]
    [InlineData("authorization_code=github-code")]
    [InlineData("")]
    public void Allow_list_rejects_arbitrary_or_sensitive_failure_text(string failureCode)
    {
        Assert.False(GitHubRepositoryAccessFailureCodes.IsSafe(failureCode));
        Assert.Throws<ArgumentException>(() => GitHubRepositoryAccessFailureCodes.RequireSafe(failureCode));
    }
}
