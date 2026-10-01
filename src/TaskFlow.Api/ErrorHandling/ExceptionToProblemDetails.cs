using System.Diagnostics;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TaskFlow.Application.Abstractions;
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
            UnauthorizedException => (StatusCodes.Status401Unauthorized, ex.Message),
            ConflictException => (StatusCodes.Status409Conflict, ex.Message),
            ForbiddenException or ForbiddenDomainException => (StatusCodes.Status403Forbidden, ex.Message),
            ValidationException => (StatusCodes.Status400BadRequest, "La solicitud no es válida."),
            DomainException => (StatusCodes.Status422UnprocessableEntity, ex.Message),
            // Falla de un servicio externo, no del cliente ni nuestra: 503 y se puede reintentar.
            GitHubUnavailableException => (StatusCodes.Status503ServiceUnavailable, ex.Message),
            // Red de seguridad: si dos requests pasan la validación "¿ya existe?" a la vez, el índice
            // único de la base rechaza al segundo. Eso es un 409, no un 500.
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
                (StatusCodes.Status409Conflict, "El recurso ya existe."),
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
