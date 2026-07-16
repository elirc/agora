using Agora.Application.Errors;
using Agora.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Agora.Api.Errors;

/// <summary>
/// Translates the application's failure vocabulary into RFC 7807 responses. One translation
/// table, applied everywhere — no endpoint does its own mapping.
/// </summary>
public sealed class AgoraExceptionHandler(IProblemDetailsService problemDetails)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // LEARN: the failure families and their contracts —
        //   404 not-found:            the resource isn't there (or you may not know)
        //   400 invalid-request:      the request itself is malformed (bad cursor, bad limit)
        //   422 domain-rule-violated: well-formed request, but the business vocabulary said no
        // Anything else is a genuine bug and stays a 500 (handled by the default pipeline; we
        // deliberately do NOT catch it here). More families arrive with the features that need
        // them: 401/403 with auth (S02), 429/503 later. The table grows; it never gets clever.
        (int status, string type, string title) = exception switch
        {
            NotFoundException => (
                StatusCodes.Status404NotFound,
                AgoraProblems.NotFound,
                "Resource not found"),
            RequestValidationException => (
                StatusCodes.Status400BadRequest,
                AgoraProblems.InvalidRequest,
                "Invalid request"),
            DomainRuleException => (
                StatusCodes.Status422UnprocessableEntity,
                AgoraProblems.DomainRuleViolated,
                "Business rule violated"),
            _ => (0, string.Empty, string.Empty),
        };

        if (status == 0)
        {
            return false;
        }

        httpContext.Response.StatusCode = status;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Type = type,
                Title = title,
                // WHY: exception messages here are written for API consumers (they come from our
                // own validation), so exposing them is deliberate. A 500's message is NOT safe
                // the same way — one more reason unknown exceptions don't take this path.
                Detail = exception.Message,
            },
        });
    }
}
