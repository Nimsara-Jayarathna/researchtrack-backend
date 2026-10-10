using ResearchTrack.GitHubService.Features.Synchronization;

namespace ResearchTrack.GitHubService.Tests.Services;

public sealed class GitHubReconciliationPolicyTests
{
    [Theory]
    [InlineData("main", "abc123", "main", "abc123")]
    [InlineData("main", "ABC123", "main", "abc123")]
    public void HasRemoteChange_UnchangedRepository_ReturnsFalse(
        string localBranch,
        string localHead,
        string remoteBranch,
        string remoteHead)
    {
        Assert.False(GitHubReconciliationPolicy.HasRemoteChange(
            localBranch, localHead, remoteBranch, remoteHead));
    }

    [Theory]
    [InlineData("main", "abc123", "develop", "abc123")]
    [InlineData("main", "abc123", "main", "def456")]
    [InlineData("main", "abc123", "develop", "def456")]
    public void HasRemoteChange_ChangedBranchOrHead_ReturnsTrue(
        string localBranch,
        string localHead,
        string remoteBranch,
        string remoteHead)
    {
        Assert.True(GitHubReconciliationPolicy.HasRemoteChange(
            localBranch, localHead, remoteBranch, remoteHead));
    }

    [Fact]
    public void HasRemoteChange_MissingLocalSnapshot_ReturnsTrue()
    {
        Assert.True(GitHubReconciliationPolicy.HasRemoteChange(null, null, "main", "abc123"));
        Assert.True(GitHubReconciliationPolicy.HasRemoteChange("main", null, "main", "abc123"));
        Assert.True(GitHubReconciliationPolicy.HasRemoteChange(null, "abc123", "main", "abc123"));
    }

    [Fact]
    public void HasRemoteChange_DefaultBranchComparison_IsCaseSensitive()
    {
        Assert.True(GitHubReconciliationPolicy.HasRemoteChange(
            "Main", "abc123", "main", "ABC123"));
    }
}
