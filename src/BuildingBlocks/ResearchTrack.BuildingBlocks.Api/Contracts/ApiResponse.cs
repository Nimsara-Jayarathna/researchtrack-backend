using System.Text.Json.Serialization;

namespace ResearchTrack.BuildingBlocks.Api.Contracts;

public sealed record ApiResponse<T>(
    bool Success,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] T? Data,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ApiError? Error,
    ApiMeta Meta)
{
    public static ApiResponse<T> Ok(T data, ApiMeta meta) => new(true, data, null, meta);

    public static ApiResponse<T> Fail(ApiError error, ApiMeta meta) => new(false, default, error, meta);
}
