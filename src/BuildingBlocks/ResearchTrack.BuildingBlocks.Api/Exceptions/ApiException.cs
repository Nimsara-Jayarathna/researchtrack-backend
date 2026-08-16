namespace ResearchTrack.BuildingBlocks.Api.Exceptions;

public class ApiException : Exception
{
    public ApiException(int statusCode, string code, string message, object? details = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
        Details = details;
    }

    public int StatusCode { get; }

    public string Code { get; }

    public object? Details { get; }
}
