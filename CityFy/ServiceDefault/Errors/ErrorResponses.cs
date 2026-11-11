using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ServiceDefault.Errors
{
    /// <summary>
    /// Uniform error responses: RFC 9457 ProblemDetails with the extensions
    ///   "code"    — stable ErrorCodes value the frontend switches on
    ///   "traceId" — to find the request in the Aspire dashboard / logs
    /// </summary>
    public static class ErrorResponses
    {
        public const string CodeExtension = "code";
        public const string TraceIdExtension = "traceId";

        public static IServiceCollection AddCityFyProblemDetails(this IServiceCollection services)
        {
            services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
            {
                ctx.ProblemDetails.Extensions.TryAdd(CodeExtension, DefaultCode(ctx.ProblemDetails.Status));
                ctx.ProblemDetails.Extensions.TryAdd(TraceIdExtension, Activity.Current?.TraceId.ToString() ?? ctx.HttpContext.TraceIdentifier);
            });
            services.AddExceptionHandler<CityFyExceptionHandler>();

            // [ApiController] model validation errors carry the code as well
            services.Configure<ApiBehaviorOptions>(o =>
            {
                var defaultFactory = o.InvalidModelStateResponseFactory;
                o.InvalidModelStateResponseFactory = ctx =>
                {
                    var result = defaultFactory(ctx);
                    if (result is ObjectResult { Value: ProblemDetails pd })
                        pd.Extensions[CodeExtension] = ErrorCodes.ValidationFailed;
                    return result;
                };
            });

            return services;
        }

        /// <summary>ProblemDetails result with a code, for errors detected in a controller.</summary>
        public static ObjectResult CityFyProblem(this ControllerBase controller, string code, string detail, int statusCode)
        {
            var result = controller.Problem(detail: detail, statusCode: statusCode, title: code);
            ((ProblemDetails)result.Value!).Extensions[CodeExtension] = code;
            return result;
        }

        // Code of responses that did not set one (empty 404, 405, model binding, ...)
        private static string DefaultCode(int? status) => status switch
        {
            StatusCodes.Status400BadRequest => ErrorCodes.ValidationFailed,
            StatusCodes.Status401Unauthorized => ErrorCodes.Unauthenticated,
            StatusCodes.Status404NotFound => ErrorCodes.NotFound,
            StatusCodes.Status503ServiceUnavailable => ErrorCodes.ServiceUnavailable,
            >= 400 and < 500 => ErrorCodes.RequestRejected,
            _ => ErrorCodes.InternalError
        };
    }

    /// <summary>Unhandled exceptions -> ProblemDetails (technical message only in Development).</summary>
    internal sealed class CityFyExceptionHandler(
        IProblemDetailsService problemDetails,
        IWebHostEnvironment environment,
        ILogger<CityFyExceptionHandler> logger) : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
        {
            var (status, code, detail) = exception switch
            {
                CityFyException e => (e.StatusCode, e.Code, e.Message),
                BadHttpRequestException e => (e.StatusCode, ErrorCodes.ValidationFailed, e.Message),
                _ => (StatusCodes.Status500InternalServerError, ErrorCodes.InternalError,
                      environment.IsDevelopment() ? exception.Message : "Unexpected error")
            };

            if (status >= 500)
                logger.LogError(exception, "Request failed with {Code}", code);

            context.Response.StatusCode = status;
            return await problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                Exception = exception,
                ProblemDetails = new ProblemDetails
                {
                    Status = status,
                    Title = code,
                    Detail = detail,
                    Extensions = { [ErrorResponses.CodeExtension] = code }
                }
            });
        }
    }
}
