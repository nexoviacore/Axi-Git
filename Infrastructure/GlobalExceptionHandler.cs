using AxiGitAPI.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace AxiGitAPI.Infrastructure
{
    // Turns exceptions that escape a controller into a ProblemDetails response.
    // Controllers that catch their own exceptions (push, pull, listrepositories)
    // never reach this handler, so their response shapes are unchanged.
    public class GlobalExceptionHandler : IExceptionHandler
    {
        private readonly IProblemDetailsService _problemDetailsService;

        private readonly ILogger<GlobalExceptionHandler> _logger;

        public GlobalExceptionHandler(
            IProblemDetailsService problemDetailsService,
            ILogger<GlobalExceptionHandler> logger)
        {
            _problemDetailsService = problemDetailsService;
            _logger = logger;
        }

        public async ValueTask<bool> TryHandleAsync(
            HttpContext httpContext,
            Exception exception,
            CancellationToken cancellationToken)
        {
            // Known exceptions carry client-safe messages; anything else stays in the log only.
            (int status, string title, string? detail) = exception switch
            {
                RepositoryNotFoundException => (
                    StatusCodes.Status404NotFound,
                    "Repository not found.",
                    exception.Message),

                GitHubApiException => (
                    StatusCodes.Status502BadGateway,
                    "GitHub request failed.",
                    exception.Message),

                _ => (
                    StatusCodes.Status500InternalServerError,
                    "An unexpected error occurred.",
                    (string?)null)
            };

            if (status == StatusCodes.Status500InternalServerError)
            {
                _logger.LogError(
                    exception,
                    "Unhandled exception for {Method} {Path}.",
                    httpContext.Request.Method,
                    httpContext.Request.Path);
            }
            else
            {
                _logger.LogWarning(
                    "{Method} {Path} failed with {Status}: {Message}",
                    httpContext.Request.Method,
                    httpContext.Request.Path,
                    status,
                    exception.Message);
            }

            httpContext.Response.StatusCode = status;

            return await _problemDetailsService.TryWriteAsync(
                new ProblemDetailsContext
                {
                    HttpContext = httpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = status,
                        Title = title,
                        Detail = detail
                    }
                });
        }
    }
}
