using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Lyubishchev_Time_Management.Infrastructure.Errors;

// Catches every otherwise-unhandled exception the app raises. API requests (/api/*) get a safe,
// fixed RFC 7807 response written here directly. Every other request is logged here and then left
// unhandled (this returns false) so the framework's own ExceptionHandlerOptions.ExceptionHandlingPath
// (configured in Program.cs as "/Home/Error") re-executes the request — HomeController.Error reads
// the TraceId this handler stashes in HttpContext.Items before returning.
public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    private const string SafeDetail = "發生錯誤，請稍後再試。"; // AGENTS.md's documented generic frontend error message.

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException)
        {
            // A cancelled request (client disconnect, navigated away) is not an application
            // error; never log it as one or fabricate a response for it.
            return false;
        }

        var traceId = GetTraceId(httpContext);

        // ExceptionHandlerMiddleware rewrites httpContext.Request.Path to the configured
        // ExceptionHandlingPath ("/Home/Error") *before* invoking registered IExceptionHandlers,
        // so httpContext.Request.Path no longer reflects the request that actually threw by the
        // time this method runs. IExceptionHandlerPathFeature.Path is the one place that still
        // holds the original path; use it everywhere below instead of httpContext.Request.Path.
        var originalPath = httpContext.Features.Get<IExceptionHandlerPathFeature>()?.Path ?? httpContext.Request.Path.Value ?? string.Empty;

        // A message-template scope (rather than a raw Dictionary) so IncludeScopes-enabled
        // console/journal output actually renders these fields instead of a bare type name, while
        // remaining structured (IReadOnlyList<KeyValuePair<string,object>>) for any log sink that
        // wants the individual values.
        using (logger.BeginScope("TraceId:{TraceId} RequestMethod:{RequestMethod} RequestPath:{RequestPath}", traceId, httpContext.Request.Method, originalPath))
        {
            logger.LogError(exception, "Unhandled request exception.");
        }

        if (httpContext.Response.HasStarted)
        {
            // Can't replace bytes already on the wire (e.g. a partially streamed CSV download);
            // the exception is still logged above, but there is nothing left to write here.
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        if (!originalPath.StartsWith("/api", StringComparison.Ordinal))
        {
            // Not our concern for HTML routes: stash the trace ID for HomeController.Error and
            // let the framework's configured ExceptionHandlingPath re-execute the request.
            httpContext.Items["TraceId"] = traceId;
            return false;
        }

        httpContext.Response.ContentType = "application/problem+json";
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Type = "https://httpstatuses.com/500",
                Title = "INTERNAL_SERVER_ERROR",
                Status = StatusCodes.Status500InternalServerError,
                Detail = SafeDetail,
                Extensions = { ["traceId"] = traceId },
            },
        });

        return true;
    }

    private static string GetTraceId(HttpContext context) => Activity.Current?.Id ?? context.TraceIdentifier;
}
