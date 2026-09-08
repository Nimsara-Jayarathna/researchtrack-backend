using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Persistence;

namespace ResearchTrack.GitHubService.Infrastructure;

public sealed class PublicAccessSourceStore : IPublicAccessSourceStore
{
    private readonly IDbContextFactory<GitHubDbContext> _dbContextFactory;

    public PublicAccessSourceStore(IDbContextFactory<GitHubDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<GitHubAvailableRepositoriesResponse> CreateAsync(
        Guid projectId,
        Guid userId,
        GitHubPublicRepository repository,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var activeKey = $"{projectId:N}:{GitHubAccessTypes.PublicUrl}:{repository.Id}";
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (await dbContext.AccessSources.AnyAsync(
                source => source.ActiveRepositoryKey == activeKey,
                cancellationToken))
        {
            throw DuplicateSource();
        }

        var source = new GitHubAccessSource
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            CreatedByUserId = userId,
            InstallationId = null,
            OwnerLogin = repository.OwnerLogin,
            OwnerType = repository.OwnerType,
            AccessType = GitHubAccessTypes.PublicUrl,
            Active = true,
            ActiveRepositoryKey = activeKey,
            CreatedAt = now,
            UpdatedAt = now
        };
        var storedRepository = new GitHubRepository
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            GitHubRepositoryId = repository.Id,
            FullName = repository.FullName,
            Name = repository.Name,
            OwnerLogin = repository.OwnerLogin,
            DefaultBranch = repository.DefaultBranch,
            Url = repository.HtmlUrl,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.AccessSources.Add(source);
        dbContext.Repositories.Add(storedRepository);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            throw DuplicateSource(exception);
        }

        return new GitHubAvailableRepositoriesResponse(
            source.Id,
            [new GitHubRepositoryOptionResponse(
                storedRepository.Id,
                storedRepository.GitHubRepositoryId,
                storedRepository.FullName,
                storedRepository.Name,
                storedRepository.OwnerLogin,
                storedRepository.DefaultBranch,
                storedRepository.Url)],
            1);
    }

    private static ApiException DuplicateSource(Exception? inner = null) => new(
        StatusCodes.Status409Conflict,
        ErrorCodes.Conflict,
        "An active public access source already exists for this repository and project.",
        innerException: inner);

    private static bool IsDuplicateKey(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
