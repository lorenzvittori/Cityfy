using Microsoft.AspNetCore.Http;

namespace ServiceDefault.Errors;

/// <summary>
/// Error with a stable <see cref="ErrorCodes"/> code. Thrown in a request it becomes a
/// ProblemDetails response with that code and status; in a background job its code is
/// stored in the task/run status.
/// </summary>
public class CityFyException : Exception
{
    public CityFyException(string code, string message, int statusCode = StatusCodes.Status400BadRequest, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }

    public int StatusCode { get; }
}
