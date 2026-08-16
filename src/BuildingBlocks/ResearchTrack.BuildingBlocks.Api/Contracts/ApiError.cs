using System.Text.Json.Serialization;

namespace ResearchTrack.BuildingBlocks.Api.Contracts;

public sealed record ApiError(
    string Code,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ApiFieldError>? FieldErrors = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? Details = null);
