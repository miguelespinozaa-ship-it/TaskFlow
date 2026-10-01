using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Api.Operations;

/// <summary>
/// Dos preguntas distintas, dos endpoints:
///   /health/live  → ¿el proceso responde? Si falla, el orquestador REINICIA el contenedor.
///   /health/ready → ¿puede atender tráfico (llega a la base)? Si falla, lo SACA del balanceador, sin reiniciarlo.
/// Con un solo endpoint que chequea la base, una caída de Postgres haría reiniciar en bucle una API que está sana.
/// </summary>
public static class HealthCheckSetup
{
    private const string ReadyTag = "ready";

    public static IServiceCollection AddTaskFlowHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("postgres", tags: [ReadyTag]);
        return services;
    }

    public static void MapTaskFlowHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = WriteAsync })
            .AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains(ReadyTag), ResponseWriter = WriteAsync })
            .AllowAnonymous();
    }

    // Solo el estado de cada chequeo. Son endpoints públicos: nada de mensajes de excepción ni cadenas de conexión.
    private static Task WriteAsync(HttpContext http, HealthReport report) =>
        http.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()),
        });
}
