using System.Net;

using Microsoft.AspNetCore.Mvc;

namespace Sheaf.Middleware;

/// <summary>
/// Middleware to map well-known exception types to specific HTTP status codes.
/// Writes RFC 9457 ProblemDetails responses via IProblemDetailsService so the
/// error shape is consistent with UseExceptionHandler() for all other errors.
/// </summary>
public partial class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    IProblemDetailsService problemDetailsService)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (UnauthorizedAccessException ex)
        {
            LogUnauthorizedAccess(logger, ex, ex.Message);
            await WriteProblemDetailsAsync(context, ex, (int)HttpStatusCode.Forbidden);
        }
        catch (InvalidOperationException ex)
        {
            LogInvalidOperation(logger, ex, ex.Message);
            await WriteProblemDetailsAsync(context, ex, (int)HttpStatusCode.BadRequest);
        }
    }

    private async Task WriteProblemDetailsAsync(HttpContext context, Exception exception, int statusCode)
    {
        context.Response.StatusCode = statusCode;
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Detail = exception.Message,
            },
        });
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unauthorized access attempt: {Message}")]
    private static partial void LogUnauthorizedAccess(ILogger logger, Exception exception, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid operation: {Message}")]
    private static partial void LogInvalidOperation(ILogger logger, Exception exception, string message);
}
