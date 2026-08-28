using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.ProjectService.Contracts;
using ResearchTrack.ProjectService.Domain;
using ResearchTrack.ProjectService.Infrastructure;
using ResearchTrack.ProjectService.Persistence;

namespace ResearchTrack.ProjectService.Features.Projects;

public sealed class ProjectService : IProjectService
{
    private readonly IDbContextFactory<ProjectDbContext> _dbContextFactory;
    private readonly IAuthUserDirectoryClient _userDirectoryClient;
    private readonly TimeProvider _timeProvider;

    public ProjectService(
        IDbContextFactory<ProjectDbContext> dbContextFactory,
        IAuthUserDirectoryClient userDirectoryClient,
        TimeProvider timeProvider)
    {
        _dbContextFactory = dbContextFactory;
        _userDirectoryClient = userDirectoryClient;
        _timeProvider = timeProvider;
    }

    // ============================================================
    // CREATE PROJECT
    // ============================================================

    public async Task<CreateProjectResponse> CreateAsync(
        Guid supervisorUserId,
        CreateProjectRequest request,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);

        var normalized = ProjectRequestValidator.Validate(
            request,
            today);

        var supervisor =
            await _userDirectoryClient.GetCurrentUserAsync(
                cancellationToken);

        if (supervisor.Id != supervisorUserId ||
            !string.Equals(
                supervisor.Role,
                AuthSecurityConstants.Roles.Supervisor,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiException(
                StatusCodes.Status403Forbidden,
                ErrorCodes.Forbidden,
                "Supervisor access is required.");
        }

        var students =
            await _userDirectoryClient.ResolveStudentsAsync(
                normalized.StudentIds,
                cancellationToken);

        ProjectRequestValidator.ValidateResolvedStudents(
            normalized.StudentIds,
            students.Select(student => student.Id).ToArray());

        var studentsById = students.ToDictionary(
            student => student.Id);

        var orderedStudents = normalized.StudentIds
            .Select(id => studentsById[id])
            .ToArray();

        var projectId = Guid.NewGuid();

        var earliestMilestoneDate =
            normalized.Milestones.Min(
                milestone => milestone.DueDate);

        var project = new Project
        {
            Id = projectId,
            Title = normalized.Title,
            Summary = normalized.Summary,
            Batch = normalized.Batch,
            Semester = normalized.Semester,
            LifecycleStatus =
                ProjectLifecycleStatuses.Planning,
            ProgressPercent = 0,
            SupervisorUserId = supervisorUserId,
            LeaderStudentUserId =
                normalized.LeaderStudentId,
            MilestoneDate = earliestMilestoneDate,
            LastActivityAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        var members = new List<ProjectMember>(
            orderedStudents.Length + 1);

        members.Add(
            CreateMember(
                projectId,
                supervisor,
                ProjectMemberRoles.Supervisor,
                now));

        members.AddRange(
            orderedStudents.Select(student =>
                CreateMember(
                    projectId,
                    student,
                    ProjectMemberRoles.Student,
                    now)));

        var milestones = normalized.Milestones
            .Select((milestone, index) =>
                new ProjectMilestone
                {
                    Id = Guid.NewGuid(),
                    ProjectId = projectId,
                    Title = milestone.Title,
                    Description = milestone.Description,
                    DueDate = milestone.DueDate,
                    Status =
                        ProjectMilestoneStatuses.Planned,
                    SequenceNo = index + 1,
                    CreatedByUserId = supervisorUserId,
                    CreatedAt = now,
                    UpdatedAt = now
                })
            .ToArray();

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        dbContext.Projects.Add(project);
        dbContext.ProjectMembers.AddRange(members);
        dbContext.ProjectMilestones.AddRange(milestones);

        await dbContext.SaveChangesAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);

        var leader = normalized.LeaderStudentId is Guid leaderId
            ? orderedStudents.FirstOrDefault(
                student => student.Id == leaderId)
            : null;

        return new CreateProjectResponse(
            project.Id,
            project.Title,
            project.Summary,
            project.Batch,
            project.Semester,
            project.LifecycleStatus,
            project.ProgressPercent,
            project.MilestoneDate ?? earliestMilestoneDate,
            orderedStudents
                .Select(MapUser)
                .ToArray(),
            leader is null
                ? null
                : MapUser(leader),
            milestones
                .Select(MapMilestone)
                .ToArray());
    }

    // ============================================================
    // GET ALL ACCESSIBLE PROJECTS
    // ============================================================

    public async Task<IReadOnlyList<ProjectSummaryResponse>>
        GetAccessibleProjectsAsync(
            Guid userId,
            string role,
            CancellationToken cancellationToken)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        IQueryable<Project> projects =
            dbContext.Projects.AsNoTracking();

        if (string.Equals(
                role,
                AuthSecurityConstants.Roles.Supervisor,
                StringComparison.OrdinalIgnoreCase))
        {
            projects = projects.Where(
                project =>
                    project.SupervisorUserId == userId);
        }
        else if (string.Equals(
                     role,
                     AuthSecurityConstants.Roles.Student,
                     StringComparison.OrdinalIgnoreCase))
        {
            projects = projects.Where(project =>
                dbContext.ProjectMembers.Any(member =>
                    member.ProjectId == project.Id &&
                    member.UserId == userId &&
                    member.MemberRole ==
                        ProjectMemberRoles.Student));
        }
        else
        {
            return [];
        }

        return await projects
            .OrderByDescending(
                project => project.CreatedAt)
            .Select(project =>
                new ProjectSummaryResponse(
                    project.Id,
                    project.Title,
                    project.Summary,
                    project.LifecycleStatus,
                    project.Batch,
                    project.Semester,
                    project.MilestoneDate,
                    project.LastActivityAt,
                    project.ProgressPercent,
                    dbContext.ProjectMembers.Count(
                        member =>
                            member.ProjectId == project.Id),
                    dbContext.ProjectMembers
                        .Where(member =>
                            member.ProjectId == project.Id &&
                            member.MemberRole ==
                                ProjectMemberRoles.Supervisor)
                        .Select(member =>
                            member.FirstName + " " +
                            member.LastName)
                        .FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }

    // ============================================================
    // GET PROJECT BY ID
    // ============================================================

    public async Task<ProjectResponse?>
        GetAccessibleProjectAsync(
            Guid userId,
            string role,
            Guid projectId,
            CancellationToken cancellationToken)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var project =
            await dbContext.Projects
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    project => project.Id == projectId,
                    cancellationToken);

        if (project is null)
        {
            return null;
        }

        var canAccess = false;

        if (string.Equals(
                role,
                AuthSecurityConstants.Roles.Supervisor,
                StringComparison.OrdinalIgnoreCase))
        {
            canAccess =
                project.SupervisorUserId == userId;
        }
        else if (string.Equals(
                     role,
                     AuthSecurityConstants.Roles.Student,
                     StringComparison.OrdinalIgnoreCase))
        {
            canAccess =
                await dbContext.ProjectMembers
                    .AsNoTracking()
                    .AnyAsync(
                        member =>
                            member.ProjectId == projectId &&
                            member.UserId == userId &&
                            member.MemberRole ==
                                ProjectMemberRoles.Student,
                        cancellationToken);
        }

        if (!canAccess)
        {
            return null;
        }

        var members =
            await dbContext.ProjectMembers
                .AsNoTracking()
                .Where(member =>
                    member.ProjectId == projectId)
                .OrderBy(member =>
                    member.CreatedAt)
                .Select(member =>
                    new ProjectMemberResponse(
                        member.UserId,
                        member.FirstName,
                        member.LastName,
                        member.Email,
                        member.RegistrationNumber,
                        member.MemberRole))
                .ToListAsync(cancellationToken);

        var milestones =
            await dbContext.ProjectMilestones
                .AsNoTracking()
                .Where(milestone =>
                    milestone.ProjectId == projectId)
                .OrderBy(milestone =>
                    milestone.SequenceNo)
                .Select(milestone =>
                    new ProjectMilestoneResponse(
                        milestone.Id,
                        milestone.Title,
                        milestone.Description,
                        milestone.DueDate,
                        milestone.Status,
                        milestone.SequenceNo))
                .ToListAsync(cancellationToken);

        return CreateProjectResponse(
            project,
            members,
            milestones);
    }

    // ============================================================
    // UPDATE PROJECT
    // ============================================================

    public async Task<ProjectResponse> UpdateAsync(
        Guid supervisorUserId,
        Guid projectId,
        UpdateProjectRequest request,
        CancellationToken cancellationToken)
    {
        var title = request.Title?.Trim();
        var summary = request.Summary?.Trim();
        var batch = request.Batch?.Trim();
        var semester = request.Semester?.Trim();

        var lifecycleStatus =
            request.LifecycleStatus?
                .Trim()
                .ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                ErrorCodes.ValidationError,
                "Project title is required.");
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                ErrorCodes.ValidationError,
                "Project description is required.");
        }

        if (string.IsNullOrWhiteSpace(batch))
        {
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                ErrorCodes.ValidationError,
                "Project batch is required.");
        }

        if (string.IsNullOrWhiteSpace(semester))
        {
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                ErrorCodes.ValidationError,
                "Project semester is required.");
        }

        if (!string.IsNullOrWhiteSpace(lifecycleStatus) &&
            lifecycleStatus != ProjectLifecycleStatuses.Planning &&
            lifecycleStatus != ProjectLifecycleStatuses.Active &&
            lifecycleStatus != ProjectLifecycleStatuses.AtRisk &&
            lifecycleStatus != ProjectLifecycleStatuses.Behind &&
            lifecycleStatus != ProjectLifecycleStatuses.Completed)
        {
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                ErrorCodes.ValidationError,
                "Invalid project lifecycle status.");
        }

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var project =
            await dbContext.Projects
                .SingleOrDefaultAsync(
                    project => project.Id == projectId,
                    cancellationToken);

        if (project is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "The requested project was not found.");
        }

        if (project.SupervisorUserId != supervisorUserId)
        {
            throw new ApiException(
                StatusCodes.Status403Forbidden,
                ErrorCodes.Forbidden,
                "Only the owning Supervisor can update this project.");
        }

        var now =
            _timeProvider.GetUtcNow().UtcDateTime;

        project.Title = title;
        project.Summary = summary;
        project.Batch = batch;
        project.Semester = semester;

        if (!string.IsNullOrWhiteSpace(lifecycleStatus))
        {
            project.LifecycleStatus = lifecycleStatus;
        }

        project.UpdatedAt = now;
        project.LastActivityAt = now;

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return await BuildProjectResponseAsync(
            dbContext,
            project,
            cancellationToken);
    }

    // ============================================================
    // UPDATE PROJECT LEADER
    // ============================================================

    public async Task<ProjectResponse> UpdateLeaderAsync(
        Guid supervisorUserId,
        Guid projectId,
        UpdateProjectLeaderRequest request,
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var project =
            await dbContext.Projects
                .SingleOrDefaultAsync(
                    project => project.Id == projectId,
                    cancellationToken);

        if (project is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "The requested project was not found.");
        }

        if (project.SupervisorUserId != supervisorUserId)
        {
            throw new ApiException(
                StatusCodes.Status403Forbidden,
                ErrorCodes.Forbidden,
                "Only the owning Supervisor can update the project leader.");
        }

        if (request.LeaderStudentId is Guid leaderStudentId)
        {
            var isProjectStudent =
                await dbContext.ProjectMembers
                    .AsNoTracking()
                    .AnyAsync(
                        member =>
                            member.ProjectId == projectId &&
                            member.UserId == leaderStudentId &&
                            member.MemberRole == ProjectMemberRoles.Student,
                        cancellationToken);

            if (!isProjectStudent)
            {
                throw new ApiException(
                    StatusCodes.Status400BadRequest,
                    ErrorCodes.ValidationError,
                    "The project leader must be an active Student member of this project.");
            }
        }

        var now =
            _timeProvider.GetUtcNow().UtcDateTime;

        project.LeaderStudentUserId = request.LeaderStudentId;
        project.UpdatedAt = now;
        project.LastActivityAt = now;

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return await BuildProjectResponseAsync(
            dbContext,
            project,
            cancellationToken);
    }

    // ============================================================
    // ADD PROJECT MEMBERS
    // ============================================================

    public async Task<ProjectResponse> AddMembersAsync(
        Guid supervisorUserId,
        Guid projectId,
        AddProjectMembersRequest request,
        CancellationToken cancellationToken)
    {
        var studentIds =
            ProjectRequestValidator.ValidateMemberStudentIds(request);

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var project =
            await dbContext.Projects
                .SingleOrDefaultAsync(
                    project => project.Id == projectId,
                    cancellationToken);

        if (project is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "The requested project was not found.");
        }

        if (project.SupervisorUserId != supervisorUserId)
        {
            throw new ApiException(
                StatusCodes.Status403Forbidden,
                ErrorCodes.Forbidden,
                "Only the owning Supervisor can manage project members.");
        }

        var students =
            await _userDirectoryClient.ResolveStudentsAsync(
                studentIds,
                cancellationToken);

        ProjectRequestValidator.ValidateResolvedStudents(
            studentIds,
            students.Select(student => student.Id).ToArray());

        var existingStudentIds =
            await dbContext.ProjectMembers
                .AsNoTracking()
                .Where(member =>
                    member.ProjectId == projectId &&
                    studentIds.Contains(member.UserId))
                .Select(member => member.UserId)
                .ToListAsync(cancellationToken);

        if (existingStudentIds.Count > 0)
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "One or more selected students are already members of this project.");
        }

        var studentsById = students.ToDictionary(
            student => student.Id);

        var now =
            _timeProvider.GetUtcNow().UtcDateTime;

        dbContext.ProjectMembers.AddRange(
            studentIds.Select(studentId =>
                CreateMember(
                    projectId,
                    studentsById[studentId],
                    ProjectMemberRoles.Student,
                    now)));

        project.UpdatedAt = now;
        project.LastActivityAt = now;

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return await BuildProjectResponseAsync(
            dbContext,
            project,
            cancellationToken);
    }

    // ============================================================
    // REMOVE PROJECT STUDENT
    // ============================================================

    public async Task<ProjectResponse> RemoveStudentAsync(
        Guid supervisorUserId,
        Guid projectId,
        Guid studentId,
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var project =
            await dbContext.Projects
                .SingleOrDefaultAsync(
                    project => project.Id == projectId,
                    cancellationToken);

        if (project is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "The requested project was not found.");
        }

        if (project.SupervisorUserId != supervisorUserId)
        {
            throw new ApiException(
                StatusCodes.Status403Forbidden,
                ErrorCodes.Forbidden,
                "Only the owning Supervisor can manage project members.");
        }

        var member =
            await dbContext.ProjectMembers
                .SingleOrDefaultAsync(
                    item =>
                        item.ProjectId == projectId &&
                        item.UserId == studentId &&
                        item.MemberRole == ProjectMemberRoles.Student,
                    cancellationToken);

        if (member is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "The requested student membership was not found.");
        }

        dbContext.ProjectMembers.Remove(member);

        if (project.LeaderStudentUserId == studentId)
        {
            project.LeaderStudentUserId = null;
        }

        var now =
            _timeProvider.GetUtcNow().UtcDateTime;

        project.UpdatedAt = now;
        project.LastActivityAt = now;

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return await BuildProjectResponseAsync(
            dbContext,
            project,
            cancellationToken);
    }

    // ============================================================
    // ADD MILESTONE
    // ============================================================

    public async Task<ProjectMilestoneResponse>
        AddMilestoneAsync(
            Guid supervisorUserId,
            Guid projectId,
            CreateProjectMilestoneRequest request,
            CancellationToken cancellationToken)
    {
        var title = request.Title?.Trim();
        var description = request.Description?.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                ErrorCodes.ValidationError,
                "Milestone title is required.");
        }

        if (request.DueDate is null)
        {
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                ErrorCodes.ValidationError,
                "Milestone due date is required.");
        }

        var dueDate = request.DueDate.Value;

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var project =
            await dbContext.Projects
                .SingleOrDefaultAsync(
                    project => project.Id == projectId,
                    cancellationToken);

        if (project is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "The requested project was not found.");
        }

        if (project.SupervisorUserId != supervisorUserId)
        {
            throw new ApiException(
                StatusCodes.Status403Forbidden,
                ErrorCodes.Forbidden,
                "Only the owning Supervisor can add milestones.");
        }

        var maxSequence =
            await dbContext.ProjectMilestones
                .Where(milestone =>
                    milestone.ProjectId == projectId)
                .Select(milestone =>
                    (int?)milestone.SequenceNo)
                .MaxAsync(cancellationToken) ?? 0;

        var now =
            _timeProvider.GetUtcNow().UtcDateTime;

        var milestone = new ProjectMilestone
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Title = title,
            Description = description,
            DueDate = dueDate,
            Status =
                ProjectMilestoneStatuses.Planned,
            SequenceNo = maxSequence + 1,
            CreatedByUserId = supervisorUserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.ProjectMilestones.Add(milestone);

        if (dueDate < project.MilestoneDate)
        {
            project.MilestoneDate = dueDate;
        }

        project.UpdatedAt = now;
        project.LastActivityAt = now;

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return MapMilestone(milestone);
    }

    // ============================================================
    // UPDATE MILESTONE
    // ============================================================

    public async Task<ProjectMilestoneResponse>
    UpdateMilestoneAsync(
        Guid supervisorUserId,
        Guid projectId,
        Guid milestoneId,
        UpdateProjectMilestoneRequest request,
        CancellationToken cancellationToken)
    {
        var title = request.Title?.Trim();
        var description = request.Description?.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                ErrorCodes.ValidationError,
                "Milestone title is required.");
        }

        var dueDate = request.DueDate;

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var project =
            await dbContext.Projects
                .SingleOrDefaultAsync(
                    project => project.Id == projectId,
                    cancellationToken);

        if (project is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "The requested project was not found.");
        }

        if (project.SupervisorUserId != supervisorUserId)
        {
            throw new ApiException(
                StatusCodes.Status403Forbidden,
                ErrorCodes.Forbidden,
                "Only the owning Supervisor can update milestones.");
        }

        var milestone =
            await dbContext.ProjectMilestones
                .SingleOrDefaultAsync(
                    item =>
                        item.Id == milestoneId &&
                        item.ProjectId == projectId,
                    cancellationToken);

        if (milestone is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "The requested milestone was not found.");
        }

        var now =
            _timeProvider.GetUtcNow().UtcDateTime;

        milestone.Title = title;
        milestone.Description = description;
        milestone.DueDate = dueDate;
        milestone.UpdatedAt = now;

        // Recalculate earliest milestone date
        var milestoneDates =
            await dbContext.ProjectMilestones
                .Where(item =>
                    item.ProjectId == projectId)
                .Select(item =>
                    item.DueDate)
                .ToListAsync(cancellationToken);

        if (milestoneDates.Count > 0)
        {
            project.MilestoneDate =
                milestoneDates.Min();
        }

        project.UpdatedAt = now;
        project.LastActivityAt = now;

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return MapMilestone(milestone);
    }

    // ============================================================
    // BUILD PROJECT RESPONSE
    // ============================================================

    private static async Task<ProjectResponse>
        BuildProjectResponseAsync(
            ProjectDbContext dbContext,
            Project project,
            CancellationToken cancellationToken)
    {
        var members =
            await dbContext.ProjectMembers
                .AsNoTracking()
                .Where(member =>
                    member.ProjectId == project.Id)
                .OrderBy(member =>
                    member.CreatedAt)
                .Select(member =>
                    new ProjectMemberResponse(
                        member.UserId,
                        member.FirstName,
                        member.LastName,
                        member.Email,
                        member.RegistrationNumber,
                        member.MemberRole))
                .ToListAsync(cancellationToken);

        var milestones =
            await dbContext.ProjectMilestones
                .AsNoTracking()
                .Where(milestone =>
                    milestone.ProjectId == project.Id)
                .OrderBy(milestone =>
                    milestone.SequenceNo)
                .Select(milestone =>
                    new ProjectMilestoneResponse(
                        milestone.Id,
                        milestone.Title,
                        milestone.Description,
                        milestone.DueDate,
                        milestone.Status,
                        milestone.SequenceNo))
                .ToListAsync(cancellationToken);

        return CreateProjectResponse(
            project,
            members,
            milestones);
    }

    // ============================================================
    // CREATE PROJECT RESPONSE
    // ============================================================

    private static ProjectResponse CreateProjectResponse(
        Project project,
        IReadOnlyList<ProjectMemberResponse> members,
        IReadOnlyList<ProjectMilestoneResponse> milestones)
    {
        var supervisorMember =
            members.FirstOrDefault(
                member =>
                    member.MemberRole ==
                    ProjectMemberRoles.Supervisor);

        var leaderMember =
            project.LeaderStudentUserId is Guid leaderId
                ? members.FirstOrDefault(
                    member =>
                        member.Id == leaderId &&
                        member.MemberRole ==
                            ProjectMemberRoles.Student)
                : null;

        return new ProjectResponse(
            project.Id,
            project.Title,
            project.Summary,
            project.LifecycleStatus,
            project.Batch,
            project.Semester,
            project.MilestoneDate,
            project.LastActivityAt,
            project.ProgressPercent,
            supervisorMember is null
                ? null
                : MapUser(supervisorMember),
            leaderMember is null
                ? null
                : MapUser(leaderMember),
            members,
            milestones);
    }

    // ============================================================
    // CREATE MEMBER
    // ============================================================

    private static ProjectMember CreateMember(
        Guid projectId,
        AuthDirectoryUser user,
        string memberRole,
        DateTime now)
    {
        return new ProjectMember
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            UserId = user.Id,
            MemberRole = memberRole,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            RegistrationNumber =
                user.RegistrationNumber,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    // ============================================================
    // MAP USER
    // ============================================================

    private static ProjectUserResponse MapUser(
        AuthDirectoryUser user)
    {
        return new ProjectUserResponse(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email,
            user.RegistrationNumber);
    }

    private static ProjectUserResponse MapUser(
        ProjectMemberResponse user)
    {
        return new ProjectUserResponse(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email,
            user.RegistrationNumber);
    }

    // ============================================================
    // MAP MILESTONE
    // ============================================================

    private static ProjectMilestoneResponse MapMilestone(
        ProjectMilestone milestone)
    {
        return new ProjectMilestoneResponse(
            milestone.Id,
            milestone.Title,
            milestone.Description,
            milestone.DueDate,
            milestone.Status,
            milestone.SequenceNo);
    }
}
