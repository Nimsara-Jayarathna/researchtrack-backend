using Microsoft.AspNetCore.Mvc;
using ResearchTrack.BuildingBlocks.Api.Contracts;

namespace ResearchTrack.BuildingBlocks.Api.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected ActionResult<ApiResponse<T>> ApiOk<T>(T data)
    {
        var response = ApiResponse<T>.Ok(data, new ApiMeta(HttpContext.TraceIdentifier, DateTimeOffset.UtcNow));
        return Ok(response);
    }

    protected ActionResult<ApiResponse<T>> ApiCreated<T>(string? location, T data)
    {
        var response = ApiResponse<T>.Ok(data, new ApiMeta(HttpContext.TraceIdentifier, DateTimeOffset.UtcNow));
        return Created(location ?? string.Empty, response);
    }
}
