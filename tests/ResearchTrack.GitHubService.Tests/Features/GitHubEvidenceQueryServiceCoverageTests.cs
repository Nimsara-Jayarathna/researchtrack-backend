using Microsoft.EntityFrameworkCore;
using NSubstitute;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Features;
using ResearchTrack.GitHubService.Infrastructure;
using ResearchTrack.GitHubService.Persistence;

namespace ResearchTrack.GitHubService.Tests.Features;

public sealed class GitHubEvidenceQueryServiceCoverageTests
{
    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task GetCommitsAsync_InvalidPaging_IsRejectedBeforeAuthorization(int page, int size)
    {
        var factory = new TestGitHubDbContextFactory();
        var auth = Substitute.For<IProjectAuthorizationClient>();
        var sut = new GitHubEvidenceQueryService(factory, auth);

        await Assert.ThrowsAsync<ApiValidationException>(() => sut.GetCommitsAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), page, size, TestContext.Current.CancellationToken));

        await auth.DidNotReceive().EnsureCanViewAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCommitsAsync_MissingLinkedRepository_ReturnsNotFoundAfterAuthorization()
    {
        var factory = new TestGitHubDbContextFactory();
        var auth = Substitute.For<IProjectAuthorizationClient>();
        var projectId = Guid.NewGuid();
        var sut = new GitHubEvidenceQueryService(factory, auth);

        var exception = await Assert.ThrowsAsync<ApiException>(() => sut.GetCommitsAsync(
            Guid.NewGuid(), projectId, Guid.NewGuid(), 1, 20, TestContext.Current.CancellationToken));

        Assert.Equal(404, exception.StatusCode);
        await auth.Received(1).EnsureCanViewAsync(projectId, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetPullRequestsAsync_InvalidStatus_IsRejectedForExistingRepository()
    {
        var factory = new TestGitHubDbContextFactory();
        var auth = Substitute.For<IProjectAuthorizationClient>();
        var projectId = Guid.NewGuid();
        var link = await SeedLinkAsync(factory, projectId);
        var sut = new GitHubEvidenceQueryService(factory, auth);

        await Assert.ThrowsAsync<ApiValidationException>(() => sut.GetPullRequestsAsync(
            Guid.NewGuid(), projectId, link.Id, 1, 20, "invalid", null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetPullRequestsAsync_TooLongSearch_IsRejectedForExistingRepository()
    {
        var factory = new TestGitHubDbContextFactory();
        var auth = Substitute.For<IProjectAuthorizationClient>();
        var projectId = Guid.NewGuid();
        var link = await SeedLinkAsync(factory, projectId);
        var sut = new GitHubEvidenceQueryService(factory, auth);

        await Assert.ThrowsAsync<ApiValidationException>(() => sut.GetPullRequestsAsync(
            Guid.NewGuid(), projectId, link.Id, 1, 20, "all", new string('x', 201), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetCommitsAsync_ExistingRepositoryWithNoCommits_ReturnsEmptyPage()
    {
        var factory = new TestGitHubDbContextFactory();
        var auth = Substitute.For<IProjectAuthorizationClient>();
        var projectId = Guid.NewGuid();
        var link = await SeedLinkAsync(factory, projectId);
        var sut = new GitHubEvidenceQueryService(factory, auth);

        var result = await sut.GetCommitsAsync(
            Guid.NewGuid(), projectId, link.Id, 1, 20, TestContext.Current.CancellationToken);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
        Assert.False(result.HasMore);
    }

    private static async Task<ProjectRepositoryLink> SeedLinkAsync(TestGitHubDbContextFactory factory, Guid projectId)
    {
        var link = new ProjectRepositoryLink
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            SourceId = Guid.NewGuid(),
            GitHubRepositoryId = Guid.NewGuid(),
            GitHubRepoId = 1001,
            LinkedByUserId = Guid.NewGuid(),
            AccessType = "PUBLIC",
            FullName = "owner/repo",
            Name = "repo",
            OwnerLogin = "owner",
            DefaultBranch = "main",
            Url = "https://github.com/owner/repo",
            Active = true,
            Primary = true,
            Enabled = true,
            LinkedAt = DateTime.UtcNow,
            SyncStatus = "SYNCED",
            UpdatedAt = DateTime.UtcNow
        };
        await using var db = factory.CreateDbContext();
        db.ProjectRepositoryLinks.Add(link);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return link;
    }

    private sealed class TestGitHubDbContextFactory : IDbContextFactory<GitHubDbContext>
    {
        private readonly DbContextOptions<GitHubDbContext> _options =
            new DbContextOptionsBuilder<GitHubDbContext>()
                .UseInMemoryDatabase($"github-evidence-{Guid.NewGuid():N}")
                .Options;
        public GitHubDbContext CreateDbContext() => new(_options);
        public Task<GitHubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
