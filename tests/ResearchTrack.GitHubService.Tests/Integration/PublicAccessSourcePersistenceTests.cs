using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
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
}
