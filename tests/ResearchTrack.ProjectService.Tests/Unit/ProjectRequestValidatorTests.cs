using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.ProjectService.Contracts;
using ResearchTrack.ProjectService.Domain;
using ResearchTrack.ProjectService.Features.Projects;

namespace ResearchTrack.ProjectService.Tests.Unit;

public sealed class ProjectRequestValidatorTests
{
    private static readonly DateOnly Today = new(2026, 10, 9);
    private static readonly Guid Student1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Student2 = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Validate_TrimsAndReturnsNormalizedValidRequest()
    {
        var request = ValidRequest(
            title: "  ResearchTrack  ",
            summary: "  Project summary  ",
            batch: "  2026  ",
            semester: $"  {ProjectSemesters.Semester1}  ");

        var result = ProjectRequestValidator.Validate(request, Today);

        Assert.Equal("ResearchTrack", result.Title);
        Assert.Equal("Project summary", result.Summary);
        Assert.Equal("2026", result.Batch);
        Assert.Equal(ProjectSemesters.Semester1, result.Semester);
        Assert.Equal(new[] { Student1, Student2 }, result.StudentIds);
        Assert.Equal(Student1, result.LeaderStudentId);
        Assert.Equal(2, result.Milestones.Count);
        Assert.Equal("Proposal", result.Milestones[0].Title);
        Assert.Null(result.Milestones[0].Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RejectsMissingRequiredProjectFields(string? value)
    {
        var request = new CreateProjectRequest
        {
            Title = value,
            Summary = value,
            Batch = value,
            Semester = value,
            StudentIds = [Student1],
            Milestones = [new CreateProjectMilestoneRequest { Title = "M1", DueDate = Today }]
        };

        var exception = Assert.Throws<ApiValidationException>(() =>
            ProjectRequestValidator.Validate(request, Today));

        var fields = exception.FieldErrors.Select(error => error.Field).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("title", fields);
        Assert.Contains("summary", fields);
        Assert.Contains("batch", fields);
        Assert.Contains("semester", fields);
    }

    [Fact]
    public void Validate_RejectsLengthBoundariesAboveConfiguredMaximums()
    {
        var request = new CreateProjectRequest
        {
            Title = new string('t', 41),
            Summary = new string('s', 251),
            Batch = new string('b', 33),
            Semester = new string('x', 33),
            StudentIds = [Student1],
            Milestones = [new CreateProjectMilestoneRequest
            {
                Title = new string('m', 41),
                Description = new string('d', 251),
                DueDate = Today
            }]
        };

        var exception = Assert.Throws<ApiValidationException>(() =>
            ProjectRequestValidator.Validate(request, Today));

        var fields = exception.FieldErrors.Select(error => error.Field).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("title", fields);
        Assert.Contains("summary", fields);
        Assert.Contains("batch", fields);
        Assert.Contains("semester", fields);
        Assert.Contains("milestones[0].title", fields);
        Assert.Contains("milestones[0].description", fields);
    }

    [Fact]
    public void Validate_AcceptsExactConfiguredLengthBoundaries()
    {
        var request = new CreateProjectRequest
        {
            Title = new string('t', 40),
            Summary = new string('s', 250),
            Batch = new string('b', 32),
            Semester = ProjectSemesters.Semester1,
            StudentIds = [Student1],
            Milestones = [new CreateProjectMilestoneRequest
            {
                Title = new string('m', 40),
                Description = new string('d', 250),
                DueDate = Today
            }]
        };

        var result = ProjectRequestValidator.Validate(request, Today);

        Assert.Equal(40, result.Title.Length);
        Assert.Equal(250, result.Summary.Length);
        Assert.Equal(32, result.Batch.Length);
        Assert.Equal(40, result.Milestones[0].Title.Length);
        Assert.Equal(250, result.Milestones[0].Description!.Length);
    }

    [Fact]
    public void Validate_RejectsDuplicateStudentsAndLeaderOutsideSelectedStudents()
    {
        var request = new CreateProjectRequest
        {
            Title = "Project",
            Summary = "Summary",
            Batch = "2026",
            Semester = ProjectSemesters.Semester1,
            StudentIds = [Student1, Student1],
            LeaderStudentId = Student2,
            Milestones = [new CreateProjectMilestoneRequest { Title = "M1", DueDate = Today }]
        };

        var exception = Assert.Throws<ApiValidationException>(() =>
            ProjectRequestValidator.Validate(request, Today));

        Assert.Contains(exception.FieldErrors, error => error.Field == "studentIds");
        Assert.Contains(exception.FieldErrors, error => error.Field == "leaderStudentId");
    }

    [Fact]
    public void Validate_RejectsMissingStudentsAndMilestones()
    {
        var request = new CreateProjectRequest
        {
            Title = "Project",
            Summary = "Summary",
            Batch = "2026",
            Semester = ProjectSemesters.Semester1,
            StudentIds = [],
            Milestones = []
        };

        var exception = Assert.Throws<ApiValidationException>(() =>
            ProjectRequestValidator.Validate(request, Today));

        Assert.Contains(exception.FieldErrors, error => error.Field == "studentIds");
        Assert.Contains(exception.FieldErrors, error => error.Field == "milestones");
    }

    [Fact]
    public void Validate_RejectsPastMissingAndOutOfOrderMilestoneDates()
    {
        var request = new CreateProjectRequest
        {
            Title = "Project",
            Summary = "Summary",
            Batch = "2026",
            Semester = ProjectSemesters.Semester1,
            StudentIds = [Student1],
            Milestones =
            [
                new CreateProjectMilestoneRequest { Title = "Past", DueDate = Today.AddDays(-1) },
                new CreateProjectMilestoneRequest { Title = "Future", DueDate = Today.AddDays(10) },
                new CreateProjectMilestoneRequest { Title = "Out of order", DueDate = Today.AddDays(5) },
                new CreateProjectMilestoneRequest { Title = "Missing", DueDate = null }
            ]
        };

        var exception = Assert.Throws<ApiValidationException>(() =>
            ProjectRequestValidator.Validate(request, Today));

        Assert.Contains(exception.FieldErrors, error => error.Field == "milestones[0].dueDate");
        Assert.Contains(exception.FieldErrors, error => error.Field == "milestones[2].dueDate");
        Assert.Contains(exception.FieldErrors, error => error.Field == "milestones[3].dueDate");
    }

    [Theory]
    [InlineData(ProjectSemesters.Semester1)]
    [InlineData(ProjectSemesters.Semester2)]
    public void ValidateSemester_AcceptsSupportedSemester(string semester)
    {
        Assert.Equal(semester, ProjectRequestValidator.ValidateSemester($" {semester} "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Semester 3")]
    [InlineData("semester 1")]
    public void ValidateSemester_RejectsMissingOrUnsupportedSemester(string semester)
    {
        Assert.Throws<ApiValidationException>(() => ProjectRequestValidator.ValidateSemester(semester));
    }

    [Fact]
    public void ValidateMemberStudentIds_ReturnsValidSelectionAndRejectsInvalidSelections()
    {
        Assert.Equal(
            new[] { Student1, Student2 },
            ProjectRequestValidator.ValidateMemberStudentIds(
                new AddProjectMembersRequest { StudentIds = [Student1, Student2] }));

        Assert.Throws<ApiValidationException>(() =>
            ProjectRequestValidator.ValidateMemberStudentIds(
                new AddProjectMembersRequest { StudentIds = [] }));

        Assert.Throws<ApiValidationException>(() =>
            ProjectRequestValidator.ValidateMemberStudentIds(
                new AddProjectMembersRequest { StudentIds = [Student1, Student1] }));
    }

    [Fact]
    public void ValidateResolvedStudents_RejectsAnyUnresolvedRequestedStudent()
    {
        ProjectRequestValidator.ValidateResolvedStudents([Student1], [Student1]);

        var exception = Assert.Throws<ApiValidationException>(() =>
            ProjectRequestValidator.ValidateResolvedStudents([Student1, Student2], [Student1]));

        Assert.Equal("studentIds", Assert.Single(exception.FieldErrors).Field);
    }

    private static CreateProjectRequest ValidRequest(
        string title = "ResearchTrack",
        string summary = "Project summary",
        string batch = "2026",
        string? semester = null) => new()
    {
        Title = title,
        Summary = summary,
        Batch = batch,
        Semester = semester ?? ProjectSemesters.Semester1,
        StudentIds = [Student1, Student2],
        LeaderStudentId = Student1,
        Milestones =
        [
            new CreateProjectMilestoneRequest { Title = " Proposal ", Description = "   ", DueDate = Today },
            new CreateProjectMilestoneRequest { Title = " Final ", Description = " Final delivery ", DueDate = Today.AddDays(30) }
        ]
    };
}
