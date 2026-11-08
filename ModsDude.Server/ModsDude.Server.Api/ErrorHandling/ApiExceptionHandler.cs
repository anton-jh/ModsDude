using Microsoft.AspNetCore.Diagnostics;
using ModsDude.Server.Application.Exceptions;
using ModsDude.Server.Domain.Exceptions;

namespace ModsDude.Server.Api.ErrorHandling;

/// <summary>
/// The one place a thrown exception becomes a response, so every failure the client sees carries a
/// problem type it can react to, never a bare status.
/// </summary>
/// <remarks>
/// <para>
/// Expected outcomes are typed results returned by the endpoints. What reaches here is what cannot be
/// returned from where it happens: <see cref="NotAuthenticatedException"/>, thrown below the handlers
/// by <c>GetUserId</c> and <c>CheckIsAllowedTo</c>; a request that could not be read at all; a value
/// the domain refused that no endpoint check stopped first; and genuine failures.
/// </para>
/// <para>
/// A <see cref="DomainValidationException"/> reaching here is logged as a warning, because it means an
/// endpoint lets through input the domain refuses. The client still gets a typed 400.
/// </para>
/// </remarks>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // Nobody is listening for an answer.
            logger.LogDebug("Request {Method} {Path} was aborted by the caller.", httpContext.Request.Method, httpContext.Request.Path);
            return true;
        }

        var (status, problem) = Classify(httpContext, exception);

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true;
    }


    private (int Status, CustomProblemDetails Problem) Classify(HttpContext httpContext, Exception exception)
    {
        var method = httpContext.Request.Method;
        var path = httpContext.Request.Path;

        switch (exception)
        {
            case NotAuthenticatedException:
                return (StatusCodes.Status401Unauthorized, Problems.NotAuthenticated);

            case BadHttpRequestException badRequest:
                logger.LogInformation(badRequest, "Request {Method} {Path} could not be read.", method, path);
                return (badRequest.StatusCode, Problems.InvalidRequest("The request could not be read."));

            case DomainValidationException domainValidation:
                logger.LogWarning(domainValidation, "Request {Method} {Path} carried a value the domain refused.", method, path);
                return (StatusCodes.Status400BadRequest, Problems.InvalidRequest(domainValidation.Message));

            default:
                logger.LogError(exception, "Request {Method} {Path} failed.", method, path);
                return (StatusCodes.Status500InternalServerError, Problems.Unexpected);
        }
    }
}
