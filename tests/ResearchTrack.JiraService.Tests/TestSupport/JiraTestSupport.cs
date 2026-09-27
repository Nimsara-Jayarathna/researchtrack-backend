using Microsoft.EntityFrameworkCore;
using ResearchTrack.JiraService.Infrastructure;
using ResearchTrack.JiraService.Persistence;

namespace ResearchTrack.JiraService.Tests.TestSupport;

internal sealed class TestJiraDbContextFactory : IDbContextFactory<JiraDbContext>
{
    private readonly DbContextOptions<JiraDbContext> _options;

    public TestJiraDbContextFactory(string? databaseName = null)
    {
        _options = new DbContextOptionsBuilder<JiraDbContext>()
            .UseInMemoryDatabase(databaseName ?? $"jira-tests-{Guid.NewGuid():N}")
            .Options;
    }

    public JiraDbContext CreateDbContext() => new(_options);

    public Task<JiraDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());
}

internal sealed class RecordingProjectAuthorizationClient : IProjectAuthorizationClient
{
    public List<Guid> AccessChecks { get; } = [];
    public List<Guid> ManageChecks { get; } = [];
    public Exception? AccessFailure { get; set; }
    public Exception? ManageFailure { get; set; }

    public Task EnsureCanManageAsync(Guid projectId, CancellationToken cancellationToken)
    {
        ManageChecks.Add(projectId);
        if (ManageFailure is not null) throw ManageFailure;
        return Task.CompletedTask;
    }

    public Task EnsureCanAccessAsync(Guid projectId, CancellationToken cancellationToken)
    {
        AccessChecks.Add(projectId);
        if (AccessFailure is not null) throw AccessFailure;
        return Task.CompletedTask;
    }
}

internal sealed class StubTokenProtector : IJiraTokenProtector
{
    public string Protect(string value) => "protected:" + value;
    public string Unprotect(string value) => value.StartsWith("protected:", StringComparison.Ordinal) ? value[10..] : value;
    public bool RequiresReprotection(string protectedValue) => false;
}

internal sealed class RecordingSyncScheduler : ResearchTrack.JiraService.Features.IJiraSyncScheduler
{
    public sealed record Request(Guid ProjectId, string Reason, DateTimeOffset AvailableAt, string? EntityId);
    public List<Request> Requests { get; } = [];

    public Task RequestAsync(Guid projectId, string reason, DateTimeOffset availableAt, string? entityId, CancellationToken ct)
    {
        Requests.Add(new(projectId, reason, availableAt, entityId));
        return Task.CompletedTask;
    }
}
