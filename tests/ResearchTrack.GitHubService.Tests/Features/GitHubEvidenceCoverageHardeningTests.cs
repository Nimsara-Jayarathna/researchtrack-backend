using Microsoft.EntityFrameworkCore;
using NSubstitute;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Features;
using ResearchTrack.GitHubService.Infrastructure;
using ResearchTrack.GitHubService.Persistence;

namespace ResearchTrack.GitHubService.Tests.Features;

public sealed class GitHubEvidenceCoverageHardeningTests
{
    [Fact]
    public async Task GetCommitsAsync_ReturnsOrderedPagedCommits()
    {
        var factory = new TestGitHubDbContextFactory();
        var auth = Substitute.For<IProjectAuthorizationClient>();
        var projectId = Guid.NewGuid();
        var link = await SeedLinkAsync(factory, projectId);
        await SeedCommitsAsync(factory, link.Id, 3);
        var sut = new GitHubEvidenceQueryService(factory, auth);

        var result = await sut.GetCommitsAsync(Guid.NewGuid(), projectId, link.Id, 1, 2, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Total);
        Assert.Equal(2, result.Items.Count);
        Assert.True(result.HasMore);
        await auth.Received(1).EnsureCanViewAsync(projectId, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetContributorsAsync_ReturnsContributorsForLinkedRepository()
    {
        var factory = new TestGitHubDbContextFactory();
        var auth = Substitute.For<IProjectAuthorizationClient>();
        var projectId = Guid.NewGuid();
        var link = await SeedLinkAsync(factory, projectId);
        await using (var db = factory.CreateDbContext())
        {
            db.Contributors.Add(new GitHubContributor
            {
                Id = Guid.NewGuid(), RepositoryLinkId = link.Id, GitHubUserId = 42,
                Login = "octocat", GitHubContributionCount = 42, ObservedCommitCount = 10,
                AvatarUrl = "https://example.test/avatar.png", ProfileUrl = "https://github.com/octocat",
                LastSyncedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var sut = new GitHubEvidenceQueryService(factory, auth);

        var result = await sut.GetContributorsAsync(Guid.NewGuid(), projectId, link.Id, 1, 20,
            TestContext.Current.CancellationToken);

        Assert.Single(result.Items);
        Assert.Equal("octocat", result.Items[0].Login);
    }

    private static async Task<ProjectRepositoryLink> SeedLinkAsync(TestGitHubDbContextFactory factory, Guid projectId)
    {
        var link = new ProjectRepositoryLink
        {
            Id = Guid.NewGuid(), ProjectId = projectId, SourceId = Guid.NewGuid(), GitHubRepositoryId = Guid.NewGuid(),
            GitHubRepoId = 1001, LinkedByUserId = Guid.NewGuid(), AccessType = "PUBLIC",
            FullName = "owner/repo", Name = "repo", OwnerLogin = "owner", DefaultBranch = "main",
            Url = "https://github.com/owner/repo", Active = true, Primary = true, Enabled = true,
            LinkedAt = DateTime.UtcNow, SyncStatus = "SYNCED", UpdatedAt = DateTime.UtcNow
        };
        await using var db = factory.CreateDbContext();
        db.ProjectRepositoryLinks.Add(link);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return link;
    }

    private static async Task SeedCommitsAsync(TestGitHubDbContextFactory factory, Guid repositoryLinkId, int count)
    {
        await using var db = factory.CreateDbContext();
        for (var i = 0; i < count; i++)
        {
            db.Commits.Add(new GitHubCommit
            {
                Id = Guid.NewGuid(), RepositoryLinkId = repositoryLinkId, Sha = $"sha-{i}",
                Message = $"Commit {i}", AuthorLogin = "octocat", AuthorName = "Octo Cat",
                AuthorEmail = "octo@example.com", AuthoredAt = DateTime.UtcNow.AddMinutes(-i),
                CommittedAt = DateTime.UtcNow.AddMinutes(-i), HtmlUrl = $"https://github.com/owner/repo/commit/{i}",
                FirstSeenAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private sealed class TestGitHubDbContextFactory : IDbContextFactory<GitHubDbContext>
    {
        private readonly DbContextOptions<GitHubDbContext> _options =
            new DbContextOptionsBuilder<GitHubDbContext>()
                .UseInMemoryDatabase($"github-evidence-hardening-{Guid.NewGuid():N}")
                .Options;
        public GitHubDbContext CreateDbContext() => new(_options);
        public Task<GitHubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
