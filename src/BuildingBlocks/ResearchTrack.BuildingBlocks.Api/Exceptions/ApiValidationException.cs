using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Contracts;

namespace ResearchTrack.BuildingBlocks.Api.Exceptions;

public sealed class ApiValidationException : ApiException
{
    public ApiValidationException(IReadOnlyList<ApiFieldError> fieldErrors, string message = "Validation failed.")
        : base(StatusCodes.Status400BadRequest, ErrorCodes.ValidationError, message)
    {
        FieldErrors = fieldErrors;
    }

    public IReadOnlyList<ApiFieldError> FieldErrors { get; }
}
