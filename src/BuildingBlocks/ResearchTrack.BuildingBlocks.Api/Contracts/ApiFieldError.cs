namespace ResearchTrack.BuildingBlocks.Api.Contracts;

public sealed record ApiFieldError(string Field, IReadOnlyList<string> Errors);
