namespace ResearchTrack.ProjectService.Contracts;

public sealed class AddProjectMembersRequest
{
    public IReadOnlyList<Guid>? StudentIds { get; init; }
}
