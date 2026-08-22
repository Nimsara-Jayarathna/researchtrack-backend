using Microsoft.EntityFrameworkCore;
using ResearchTrack.ProjectService.Contracts;
using ResearchTrack.ProjectService.Domain;
using ResearchTrack.ProjectService.Persistence;

namespace ResearchTrack.ProjectService.Features.Projects;

public sealed class ProjectService : IProjectService
{
    private readonly IDbContextFactory<ProjectDbContext> _dbContextFactory;
    private readonly TimeProvider _timeProvider;

    public ProjectService(IDbContextFactory<ProjectDbContext> dbContextFactory, TimeProvider timeProvider)
    {
        _dbContextFactory = dbContextFactory;
        _timeProvider = timeProvider;
    }

    public async Task<ProjectResponse> CreateAsync(
        Guid supervisorUserId,
        CreateProjectRequest request,
        CancellationToken cancellationToken)
    {
        var normalized = ProjectRequestValidator.Validate(request);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Title = normalized.Title,
            Summary = normalized.Summary,
            Batch = normalized.Batch,
            Semester = normalized.Semester,
            LifecycleStatus = ProjectLifecycleStatuses.Planning,
            ProgressPercent = 0,
            SupervisorUserId = supervisorUserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(cancellationToken);
        return MapProject(project);
    }

    public async Task<IReadOnlyList<ProjectSummaryResponse>> GetOwnedProjectsAsync(
        Guid supervisorUserId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Projects
            .AsNoTracking()
            .Where(project => project.SupervisorUserId == supervisorUserId)
            .OrderByDescending(project => project.CreatedAt)
            .Select(project => new ProjectSummaryResponse(
                project.Id,
                project.Title,
                project.Summary,
                project.LifecycleStatus,
                project.Batch,
                project.Semester,
                null,
                project.ProgressPercent,
                0))
            .ToListAsync(cancellationToken);
    }

    public async Task<ProjectResponse?> GetOwnedProjectAsync(
        Guid supervisorUserId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var project = await dbContext.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == projectId && item.SupervisorUserId == supervisorUserId,
                cancellationToken);
        return project is null ? null : MapProject(project);
    }

    private static ProjectResponse MapProject(Project project) => new(
        project.Id,
        project.Title,
        project.Summary,
        project.LifecycleStatus,
        project.Batch,
        project.Semester,
        project.ProgressPercent,
        project.CreatedAt,
        project.UpdatedAt);
}
