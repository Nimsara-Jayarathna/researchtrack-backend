namespace ResearchTrack.BuildingBlocks.Api.RuntimeLogging;

public sealed record RuntimeLogEntry(
    long Id,
    DateTimeOffset Timestamp,
    string Level,
    string Category,
    int EventId,
    string Message,
    string? Exception,
    string Service,
    string? TraceId,
    IReadOnlyDictionary<string, object?> Properties);
