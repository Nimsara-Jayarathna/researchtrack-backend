using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.ProjectService.Contracts;

namespace ResearchTrack.ProjectService.Features.Projects;

internal static class ProjectRequestValidator
{
    private const int TitleMaxLength = 40;
    private const int SummaryMaxLength = 250;
    private const int BatchMaxLength = 32;
    private const int SemesterMaxLength = 32;

    public static NormalizedCreateProject Validate(CreateProjectRequest request)
    {
        var errors = new List<ApiFieldError>();
        var title = NormalizeRequired(request.Title, "title", "Title", TitleMaxLength, errors);
        var summary = NormalizeRequired(request.Summary, "summary", "Summary", SummaryMaxLength, errors);
        var batch = NormalizeRequired(request.Batch, "batch", "Batch", BatchMaxLength, errors);
        var semester = NormalizeRequired(request.Semester, "semester", "Semester", SemesterMaxLength, errors);

        if (errors.Count > 0)
        {
            throw new ApiValidationException(errors);
        }

        return new NormalizedCreateProject(title!, summary!, batch!, semester!);
    }

    private static string? NormalizeRequired(
        string? value,
        string field,
        string displayName,
        int maxLength,
        ICollection<ApiFieldError> errors)
    {
        var normalized = value?.Trim();
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            messages.Add($"{displayName} is required.");
        }
        else if (normalized.Length > maxLength)
        {
            messages.Add($"{displayName} must not exceed {maxLength} characters.");
        }

        if (messages.Count > 0)
        {
            errors.Add(new ApiFieldError(field, messages));
        }

        return normalized;
    }

    internal sealed record NormalizedCreateProject(string Title, string Summary, string Batch, string Semester);
}
