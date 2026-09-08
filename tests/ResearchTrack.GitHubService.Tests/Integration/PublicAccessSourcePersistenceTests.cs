using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Configuration;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Infrastructure;
using ResearchTrack.GitHubService.Persistence;
using ResearchTrack.Testing;

namespace ResearchTrack.GitHubService.Tests.Integration;

public sealed class PublicAccessSourcePersistenceTests : IAsyncLifetime
{
    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private ResearchTrackWebApplicationFactory<Program>? _factory;

    public async ValueTask InitializeAsync()
    {
        var connectionString = TestDatabaseConfiguration.GetRequiredConnectionString("GITHUB");
        _factory = new ResearchTrackWebApplicationFactory<Program>(connectionString);

        await using var dbContext = await CreateDbContextAsync();
        await dbContext.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await dbContext.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Persists_metadata_and_rejects_duplicate_active_source()
    {
        var store = new PublicAccessSourceStore(GetRequiredService<IDbContextFactory<GitHubDbContext>>());
        var repository = new GitHubPublicRepository(
            1296269,
            "openai",
            "ORG",
            "example",
            "openai/example",
            "https://github.com/openai/example",
            "main");
        var now = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

        var created = await store.CreateAsync(
            ProjectId,
            UserId,
            repository,
            now,
            TestContext.Current.CancellationToken);

        await using var dbContext = await CreateDbContextAsync();
        var source = await dbContext.AccessSources.SingleAsync(TestContext.Current.CancellationToken);
        var storedRepository = await dbContext.Repositories.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(GitHubAccessTypes.PublicUrl, source.AccessType);
        Assert.True(source.Active);
        Assert.Equal(UserId, source.CreatedByUserId);
        Assert.Equal(created.SourceId, storedRepository.SourceId);
        Assert.Equal(repository.Id, storedRepository.GitHubRepositoryId);
        Assert.Equal(repository.HtmlUrl, storedRepository.Url);

        var exception = await Assert.ThrowsAsync<ApiException>(() => store.CreateAsync(
            ProjectId,
            UserId,
            repository,
            now,
            TestContext.Current.CancellationToken));
        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Link_persists_validated_public_repository_metadata_and_rejects_duplicate()
    {
        var available = await CreatePublicSourceAsync();
        var repositoryId = Assert.Single(available.Items).Id;
        var store = CreateLinkStore();

        var result = await store.CreateLinksAsync(
            ProjectId,
            available.SourceId,
            UserId,
            [new LinkGitHubRepositoryRequestItem(repositoryId, "  Research repository  ", true)],
            Now,
            TestContext.Current.CancellationToken);

        var responseLink = Assert.Single(result.Response.Repositories);
        Assert.Equal("PUBLIC_URL", responseLink.AccessType);
        Assert.Equal(1296269, responseLink.GitHubRepoId);
        Assert.Equal("openai", responseLink.OwnerLogin);
        Assert.Equal("example", responseLink.Name);
        Assert.Equal("openai/example", responseLink.FullName);
        Assert.Equal("https://github.com/openai/example", responseLink.Url);
        Assert.Equal("main", responseLink.DefaultBranch);
        Assert.Equal("Research repository", responseLink.CustomName);
        Assert.True(responseLink.Enabled);
        Assert.True(responseLink.Primary);
        Assert.Equal("PENDING", responseLink.SyncStatus);
        Assert.Single(result.SyncRequests);

        await using var dbContext = await CreateDbContextAsync();
        var persisted = await dbContext.ProjectRepositoryLinks.SingleAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(available.SourceId, persisted.SourceId);
        Assert.Equal(repositoryId, persisted.GitHubRepositoryId);
        Assert.Equal(1296269, persisted.GitHubRepoId);
        Assert.Equal("openai", persisted.OwnerLogin);
        Assert.Equal("https://github.com/openai/example", persisted.Url);

        var exception = await Assert.ThrowsAsync<ApiException>(() => store.CreateLinksAsync(
            ProjectId,
            available.SourceId,
            UserId,
            [new LinkGitHubRepositoryRequestItem(repositoryId, null, false)],
            Now,
            TestContext.Current.CancellationToken));
        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Link_rejects_source_project_mismatch_and_manipulated_repository_id()
    {
        var available = await CreatePublicSourceAsync();
        var store = CreateLinkStore();
        var otherProjectId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        var mismatch = await Assert.ThrowsAsync<ApiException>(() => store.CreateLinksAsync(
            otherProjectId,
            available.SourceId,
            UserId,
            [new LinkGitHubRepositoryRequestItem(Assert.Single(available.Items).Id, null, true)],
            Now,
            TestContext.Current.CancellationToken));
        Assert.Equal(StatusCodes.Status409Conflict, mismatch.StatusCode);

        var manipulated = await Assert.ThrowsAsync<ApiException>(() => store.CreateLinksAsync(
            ProjectId,
            available.SourceId,
            UserId,
            [new LinkGitHubRepositoryRequestItem(Guid.NewGuid(), null, true)],
            Now,
            TestContext.Current.CancellationToken));
        Assert.Equal(StatusCodes.Status404NotFound, manipulated.StatusCode);

        await using var dbContext = await CreateDbContextAsync();
        Assert.Empty(await dbContext.ProjectRepositoryLinks.ToListAsync(
            TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Concurrent_duplicate_link_requests_create_only_one_active_link()
    {
        var available = await CreatePublicSourceAsync();
        var repositoryId = Assert.Single(available.Items).Id;

        async Task<Exception?> TryLinkAsync()
        {
            try
            {
                await CreateLinkStore().CreateLinksAsync(
                    ProjectId,
                    available.SourceId,
                    UserId,
                    [new LinkGitHubRepositoryRequestItem(repositoryId, null, true)],
                    Now,
                    TestContext.Current.CancellationToken);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        var results = await Task.WhenAll(TryLinkAsync(), TryLinkAsync());

        Assert.Single(results, result => result is null);
        var conflict = Assert.IsType<ApiException>(Assert.Single(results, result => result is not null));
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        await using var dbContext = await CreateDbContextAsync();
        Assert.Equal(1, await dbContext.ProjectRepositoryLinks.CountAsync(
            link => link.Active,
            TestContext.Current.CancellationToken));
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private T GetRequiredService<T>() where T : notnull =>
        (_factory ?? throw new InvalidOperationException("Factory is unavailable."))
        .Services.GetRequiredService<T>();

    private async Task<GitHubDbContext> CreateDbContextAsync() =>
        await GetRequiredService<IDbContextFactory<GitHubDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);

    private RepositoryLinkStore CreateLinkStore() => new(
        GetRequiredService<IDbContextFactory<GitHubDbContext>>(),
        new GitHubRepositoryLinkOptions(5, 5));

    private async Task<GitHubAvailableRepositoriesResponse> CreatePublicSourceAsync()
    {
        var store = new PublicAccessSourceStore(GetRequiredService<IDbContextFactory<GitHubDbContext>>());
        return await store.CreateAsync(
            ProjectId,
            UserId,
            PublicRepository,
            Now,
            TestContext.Current.CancellationToken);
    }

    private static DateTime Now => new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private static GitHubPublicRepository PublicRepository => new(
        1296269,
        "openai",
        "ORG",
        "example",
        "openai/example",
        "https://github.com/openai/example",
        "main");
}
