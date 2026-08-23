namespace ResearchTrack.AuthService.Contracts;

public sealed class ResolveStudentsRequest
{
    public IReadOnlyList<Guid>? StudentIds { get; init; }
}
