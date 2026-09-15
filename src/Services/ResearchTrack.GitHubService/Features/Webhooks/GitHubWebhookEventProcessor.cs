using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Features.Installation;
using ResearchTrack.GitHubService.Infrastructure.GitHubApp;
using ResearchTrack.GitHubService.Persistence;

namespace ResearchTrack.GitHubService.Features.Webhooks;

public sealed class GitHubWebhookEventProcessor : IGitHubWebhookEventProcessor
{
    private readonly IDbContextFactory<GitHubDbContext> _dbContextFactory;
    private readonly IGitHubInstallationRepositoryInventoryService _inventoryService;
    private readonly IGitHubAppClient _gitHubAppClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GitHubWebhookEventProcessor> _logger;

    public GitHubWebhookEventProcessor(
        IDbContextFactory<GitHubDbContext> dbContextFactory,
        IGitHubInstallationRepositoryInventoryService inventoryService,
        IGitHubAppClient gitHubAppClient,
        TimeProvider timeProvider,
        ILogger<GitHubWebhookEventProcessor> logger)
    {
        _dbContextFactory = dbContextFactory;
        _inventoryService = inventoryService;
        _gitHubAppClient = gitHubAppClient;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<GitHubWebhookProcessingPlan> ProcessAsync(
        GitHubWebhookDelivery delivery,
        CancellationToken cancellationToken)
    {
        using var document = Parse(delivery.PayloadJson);
        var root = document.RootElement;
        var eventType = delivery.EventType.ToLowerInvariant();

        return eventType switch
        {
            "ping" => GitHubWebhookProcessingPlan.Ignore("ping"),
            "push" => await ProcessPushAsync(delivery, root, cancellationToken),
            "pull_request" or "pull_request_review" or "pull_request_review_comment" =>
                await ProcessRepositorySyncEventAsync(delivery, root, cancellationToken),
            "repository" => await ProcessRepositoryEventAsync(delivery, root, cancellationToken),
            "installation_repositories" => await ProcessInstallationRepositoriesAsync(delivery, root, cancellationToken),
            "installation" => await ProcessInstallationAsync(delivery, root, cancellationToken),
            _ => GitHubWebhookProcessingPlan.Ignore($"unsupported_event:{eventType}")
        };
    }

    private async Task<GitHubWebhookProcessingPlan> ProcessPushAsync(
        GitHubWebhookDelivery delivery,
        JsonElement root,
        CancellationToken cancellationToken)
    {
        var installationId = RequireInstallationId(delivery);
        var repository = RequireRepository(root);
        await UpdateRepositoryMetadataFromWebhookAsync(installationId, repository, cancellationToken);

        var reference = ReadString(root, "ref");
        if (string.IsNullOrWhiteSpace(reference) || !reference.StartsWith("refs/heads/", StringComparison.Ordinal))
        {
            return GitHubWebhookProcessingPlan.Ignore("push_without_branch_ref");
        }

        var links = await LoadEligibleLinksAsync(installationId, repository.Id, cancellationToken);
        var selected = links
            .Where(link => !string.IsNullOrWhiteSpace(link.DefaultBranch)
                && string.Equals(reference, $"refs/heads/{link.DefaultBranch}", StringComparison.Ordinal))
            .Select(link => link.Id)
            .Distinct()
            .ToList();

        return selected.Count == 0
            ? GitHubWebhookProcessingPlan.Ignore("non_default_branch_or_unlinked_repository")
            : GitHubWebhookProcessingPlan.Process(selected);
    }

    private async Task<GitHubWebhookProcessingPlan> ProcessRepositorySyncEventAsync(
        GitHubWebhookDelivery delivery,
        JsonElement root,
        CancellationToken cancellationToken)
    {
        var installationId = RequireInstallationId(delivery);
        var repository = RequireRepository(root);
        await UpdateRepositoryMetadataFromWebhookAsync(installationId, repository, cancellationToken);
        var links = await LoadEligibleLinksAsync(installationId, repository.Id, cancellationToken);
        return links.Count == 0
            ? GitHubWebhookProcessingPlan.Ignore("repository_not_linked_or_not_enabled")
            : GitHubWebhookProcessingPlan.Process(links.Select(link => link.Id).Distinct().ToList());
    }

    private async Task<GitHubWebhookProcessingPlan> ProcessRepositoryEventAsync(
        GitHubWebhookDelivery delivery,
        JsonElement root,
        CancellationToken cancellationToken)
    {
        var installationId = RequireInstallationId(delivery);
        var repository = RequireRepository(root);
        var action = delivery.Action?.ToLowerInvariant();

        if (action == "deleted")
        {
            await MarkRepositoryUnavailableAsync(installationId, repository.Id, cancellationToken);
            return GitHubWebhookProcessingPlan.Ignore("repository_deleted");
        }

        await UpdateRepositoryMetadataFromWebhookAsync(installationId, repository, cancellationToken);
        var links = await LoadEligibleLinksAsync(installationId, repository.Id, cancellationToken);
        return links.Count == 0
            ? GitHubWebhookProcessingPlan.Ignore("repository_not_linked_or_not_enabled")
            : GitHubWebhookProcessingPlan.Process(links.Select(link => link.Id).Distinct().ToList());
    }

    private async Task<GitHubWebhookProcessingPlan> ProcessInstallationRepositoriesAsync(
        GitHubWebhookDelivery delivery,
        JsonElement root,
        CancellationToken cancellationToken)
    {
        var installationId = RequireInstallationId(delivery);
        // installation_repositories is not authoritative for the installation
        // lifecycle itself. In particular, an older repository-scope delivery
        // must never resurrect a source that a newer installation.deleted or
        // installation.suspend event already marked unavailable. Inventory
        // refresh below only operates on sources that are currently CONNECTED.
        var addedIds = ReadRepositoryIdArray(root, "repositories_added");
        var removedIds = ReadRepositoryIdArray(root, "repositories_removed");
        foreach (var repositoryId in removedIds)
        {
            await MarkRepositoryUnavailableAsync(installationId, repositoryId, cancellationToken);
        }

        await RefreshInstallationInventoriesAsync(installationId, cancellationToken);
        if (addedIds.Count == 0)
        {
            return GitHubWebhookProcessingPlan.Ignore(removedIds.Count > 0
                ? "repository_access_removed"
                : "installation_repository_scope_unchanged");
        }

        var links = await LoadEligibleLinksAsync(installationId, repositoryId: null, cancellationToken);
        var added = addedIds.ToHashSet();
        var selected = links
            .Where(link => added.Contains(link.GitHubRepoId))
            .Select(link => link.Id)
            .Distinct()
            .ToList();
        return selected.Count == 0
            ? GitHubWebhookProcessingPlan.Ignore("added_repositories_not_linked")
            : GitHubWebhookProcessingPlan.Process(selected);
    }

    private async Task<GitHubWebhookProcessingPlan> ProcessInstallationAsync(
        GitHubWebhookDelivery delivery,
        JsonElement root,
        CancellationToken cancellationToken)
    {
        var installationId = RequireInstallationId(delivery);
        var action = delivery.Action?.ToLowerInvariant()
            ?? throw new GitHubWebhookPermanentException("Installation webhook is missing its action.");

        switch (action)
        {
            case "deleted":
                await SetInstallationConnectionStatusAsync(
                    installationId,
                    GitHubConnectionStatuses.Removed,
                    clearInstallationKey: true,
                    cancellationToken);
                await MarkAllInstallationRepositoriesUnavailableAsync(installationId, cancellationToken);
                return GitHubWebhookProcessingPlan.Ignore("installation_deleted");

            case "suspend":
                await SetInstallationConnectionStatusAsync(
                    installationId,
                    GitHubConnectionStatuses.Suspended,
                    clearInstallationKey: false,
                    cancellationToken);
                return GitHubWebhookProcessingPlan.Ignore("installation_suspended");

            case "unsuspend":
            case "new_permissions_accepted":
            case "created":
                // Verify the installation against GitHub before changing local
                // lifecycle state. This prevents a delayed/duplicated lifecycle
                // event from reopening a source after the installation has
                // already been deleted at GitHub.
                var installation = await _gitHubAppClient.GetInstallationAsync(installationId, cancellationToken);
                if (installation.Suspended)
                {
                    await SetInstallationConnectionStatusAsync(
                        installationId,
                        GitHubConnectionStatuses.Suspended,
                        clearInstallationKey: false,
                        cancellationToken);
                    return GitHubWebhookProcessingPlan.Ignore("installation_still_suspended");
                }

                await SetInstallationConnectionStatusAsync(
                    installationId,
                    GitHubConnectionStatuses.Connected,
                    clearInstallationKey: false,
                    cancellationToken);
                var refreshed = await RefreshInstallationInventoriesAsync(installationId, cancellationToken);
                if (!refreshed)
                {
                    return GitHubWebhookProcessingPlan.Ignore("installation_not_managed_by_researchtrack");
                }
                var links = await LoadEligibleLinksAsync(installationId, repositoryId: null, cancellationToken);
                return links.Count == 0
                    ? GitHubWebhookProcessingPlan.Ignore("installation_has_no_enabled_links")
                    : GitHubWebhookProcessingPlan.Process(links.Select(link => link.Id).Distinct().ToList());

            default:
                _logger.LogDebug(
                    "Ignoring GitHub installation action. InstallationId={InstallationId} Action={Action}",
                    installationId,
                    action);
                return GitHubWebhookProcessingPlan.Ignore($"unsupported_installation_action:{action}");
        }
    }

    private async Task UpdateRepositoryMetadataFromWebhookAsync(
        long installationId,
        RepositoryPayload repository,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var repositories = await (
            from item in db.Repositories
            join source in db.AccessSources on item.SourceId equals source.Id
            where source.Active
                && source.InstallationId == installationId
                && item.GitHubRepositoryId == repository.Id
            select item).ToListAsync(cancellationToken);

        foreach (var item in repositories)
        {
            item.FullName = repository.FullName;
            item.Name = repository.Name;
            item.OwnerLogin = repository.OwnerLogin;
            item.DefaultBranch = repository.DefaultBranch;
            item.Url = repository.HtmlUrl;
            // Activity/metadata webhook deliveries can arrive out of order. They
            // are therefore not allowed to restore repository availability. Only
            // the authoritative installation-repository inventory can do that.
            item.UpdatedAt = now;
        }

        var links = await (
            from link in db.ProjectRepositoryLinks
            join source in db.AccessSources on link.SourceId equals source.Id
            where link.Active
                && source.Active
                && source.InstallationId == installationId
                && link.GitHubRepoId == repository.Id
            select link).ToListAsync(cancellationToken);
        foreach (var link in links)
        {
            link.FullName = repository.FullName;
            link.Name = repository.Name;
            link.OwnerLogin = repository.OwnerLogin;
            link.DefaultBranch = repository.DefaultBranch;
            link.Url = repository.HtmlUrl;
            link.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkRepositoryUnavailableAsync(
        long installationId,
        long repositoryId,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var repositories = await (
            from item in db.Repositories
            join source in db.AccessSources on item.SourceId equals source.Id
            where source.Active
                && source.InstallationId == installationId
                && item.GitHubRepositoryId == repositoryId
            select item).ToListAsync(cancellationToken);
        foreach (var repository in repositories)
        {
            repository.Available = false;
            repository.UpdatedAt = now;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkAllInstallationRepositoriesUnavailableAsync(
        long installationId,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var repositories = await (
            from item in db.Repositories
            join source in db.AccessSources on item.SourceId equals source.Id
            where source.Active && source.InstallationId == installationId
            select item).ToListAsync(cancellationToken);
        foreach (var repository in repositories)
        {
            repository.Available = false;
            repository.UpdatedAt = now;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SetInstallationConnectionStatusAsync(
        long installationId,
        string status,
        bool clearInstallationKey,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var sources = await db.AccessSources
            .Where(source => source.Active && source.InstallationId == installationId)
            .ToListAsync(cancellationToken);
        foreach (var source in sources)
        {
            source.ConnectionStatus = status;
            if (clearInstallationKey) source.ActiveInstallationKey = null;
            source.UpdatedAt = now;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> RefreshInstallationInventoriesAsync(
        long installationId,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var sourceIds = await db.AccessSources.AsNoTracking()
            .Where(source => source.Active
                && source.InstallationId == installationId
                && source.ConnectionStatus == GitHubConnectionStatuses.Connected)
            .Select(source => source.Id)
            .ToListAsync(cancellationToken);
        if (sourceIds.Count == 0) return false;

        foreach (var sourceId in sourceIds)
        {
            await _inventoryService.TryRefreshAsync(sourceId, cancellationToken);
        }
        return true;
    }

    private async Task<List<EligibleLink>> LoadEligibleLinksAsync(
        long installationId,
        long? repositoryId,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query =
            from link in db.ProjectRepositoryLinks.AsNoTracking()
            join source in db.AccessSources.AsNoTracking() on link.SourceId equals source.Id
            join repository in db.Repositories.AsNoTracking() on link.GitHubRepositoryId equals repository.Id
            where link.Active
                && link.Enabled
                && source.Active
                && source.InstallationId == installationId
                && source.ConnectionStatus == GitHubConnectionStatuses.Connected
                && repository.Available
            select new EligibleLink(link.Id, link.GitHubRepoId, link.DefaultBranch);
        if (repositoryId.HasValue)
        {
            query = query.Where(link => link.GitHubRepoId == repositoryId.Value);
        }
        return await query.ToListAsync(cancellationToken);
    }

    private static JsonDocument Parse(string payload)
    {
        try
        {
            return JsonDocument.Parse(payload);
        }
        catch (JsonException exception)
        {
            throw new GitHubWebhookPermanentException($"Stored GitHub webhook JSON is invalid: {exception.Message}");
        }
    }

    private static long RequireInstallationId(GitHubWebhookDelivery delivery) =>
        delivery.InstallationId is > 0
            ? delivery.InstallationId.Value
            : throw new GitHubWebhookPermanentException("GitHub webhook does not include a valid installation id.");

    private static RepositoryPayload RequireRepository(JsonElement root)
    {
        if (!root.TryGetProperty("repository", out var repository)
            || repository.ValueKind != JsonValueKind.Object)
        {
            throw new GitHubWebhookPermanentException("GitHub webhook does not include repository metadata.");
        }
        var id = ReadInt64(repository, "id")
            ?? throw new GitHubWebhookPermanentException("GitHub repository id is missing.");
        var name = ReadString(repository, "name")
            ?? throw new GitHubWebhookPermanentException("GitHub repository name is missing.");
        var fullName = ReadString(repository, "full_name")
            ?? throw new GitHubWebhookPermanentException("GitHub repository full name is missing.");
        var htmlUrl = ReadString(repository, "html_url")
            ?? throw new GitHubWebhookPermanentException("GitHub repository URL is missing.");
        var defaultBranch = ReadString(repository, "default_branch");
        string ownerLogin;
        if (repository.TryGetProperty("owner", out var owner)
            && owner.ValueKind == JsonValueKind.Object)
        {
            ownerLogin = ReadString(owner, "login")
                ?? throw new GitHubWebhookPermanentException("GitHub repository owner login is missing.");
        }
        else
        {
            var slash = fullName.IndexOf('/');
            ownerLogin = slash > 0
                ? fullName[..slash]
                : throw new GitHubWebhookPermanentException("GitHub repository owner metadata is missing.");
        }
        return new RepositoryPayload(id, name, fullName, ownerLogin, defaultBranch, htmlUrl);
    }

    private static List<long> ReadRepositoryIdArray(JsonElement root, string property)
    {
        var result = new List<long>();
        if (!root.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return result;
        }
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("id", out var id)
                && id.TryGetInt64(out var value)
                && value > 0)
            {
                result.Add(value);
            }
        }
        return result;
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? ReadInt64(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt64(out var result)
            ? result
            : null;

    private sealed record RepositoryPayload(
        long Id,
        string Name,
        string FullName,
        string OwnerLogin,
        string? DefaultBranch,
        string HtmlUrl);

    private sealed record EligibleLink(Guid Id, long GitHubRepoId, string? DefaultBranch);
}
