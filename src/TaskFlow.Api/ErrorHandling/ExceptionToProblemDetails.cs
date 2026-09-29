using System.Diagnostics;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.Common.Exceptions;
using TaskFlow.Domain.Common;

namespace TaskFlow.Api.ErrorHandling;

/// <summary>Traduce excepciones a ProblemDetails (RFC 9457). Nunca expone stack traces.</summary>
public sealed class ExceptionToProblemDetails(
    IProblemDetailsService problemDetails,
    ILogger<ExceptionToProblemDetails> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, title) = ex switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, ex.Message),
            ValidationException => (StatusCodes.Status400BadRequest, "La solicitud no es válida."),
            DomainException => (StatusCodes.Status422UnprocessableEntity, ex.Message),
            _ => (StatusCodes.Status500InternalServerError, "Error interno."),
        };

        if (status == StatusCodes.Status500InternalServerError)
            logger.LogError(ex, "Unhandled exception on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
        else
            logger.LogWarning("Client error {Status} on {Method} {Path}: {Message}",
                status, ctx.Request.Method, ctx.Request.Path, ex.Message);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://httpstatuses.io/{status}",
            Instance = ctx.Request.Path,
            // El cliente puede mandarle el traceId a soporte para ubicar el log.
            Extensions = { ["traceId"] = Activity.Current?.Id ?? ctx.TraceIdentifier },
        };

        if (ex is ValidationException validation)
        {
            problem.Extensions["errors"] = validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        }

        ctx.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = ctx,
            Exception = ex,
            ProblemDetails = problem,
        });
    }
}
