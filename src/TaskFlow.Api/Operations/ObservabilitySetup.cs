using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Api.Operations;

public static class ObservabilitySetup
{
    private const string ServiceName = "taskflow-api";

    public static void AddTaskFlowObservability(this WebApplicationBuilder builder)
    {
        // Logs estructurados: cada evento es un objeto con propiedades (UserId, WorkspaceId, TraceId…),
        // no una línea de texto que después hay que parsear con expresiones regulares.
        builder.Host.UseSerilog((context, _, logger) =>
        {
            logger
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                .ReadFrom.Configuration(context.Configuration) // la sección "Serilog" puede cambiar niveles sin recompilar
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", ServiceName);

            if (context.HostingEnvironment.IsDevelopment())
                logger.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
            else
                // Una línea JSON por evento: es lo que esperan los recolectores de logs (Loki, Elastic, CloudWatch).
                logger.WriteTo.Console(new RenderedCompactJsonFormatter());
        });

        // Trazas distribuidas. Sin OTEL_EXPORTER_OTLP_ENDPOINT no se exporta nada, pero los TraceId se generan
        // igual y aparecen en los logs y en los errores (ProblemDetails.traceId), para poder cruzarlos.
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(o => o.Filter = http => !http.Request.Path.StartsWithSegments("/health"))
                    .AddHttpClientInstrumentation(); // las llamadas a GitHub quedan como spans hijos del request
                if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
                    tracing.AddOtlpExporter();
            });
    }

    /// <summary>Un evento por request, con quién lo hizo y en qué workspace. No registra query string, headers ni cuerpos.</summary>
    public static void UseTaskFlowRequestLogging(this WebApplication app) =>
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate = "{RequestMethod:l} {RequestPath:l} → {StatusCode} en {Elapsed:0} ms";
            // Los health checks se consultan cada pocos segundos: a nivel Information taparían todo lo demás.
            options.GetLevel = (http, _, exception) =>
                exception is not null || http.Response.StatusCode >= 500 ? LogEventLevel.Error
                : http.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose
                : LogEventLevel.Information;
            options.EnrichDiagnosticContext = (diagnostics, http) =>
            {
                if (http.User.FindFirst(TaskFlowClaims.Subject)?.Value is { } userId)
                    diagnostics.Set("UserId", userId);
                if (http.User.FindFirst(TaskFlowClaims.WorkspaceId)?.Value is { } workspaceId)
                    diagnostics.Set("WorkspaceId", workspaceId);
            };
        });
}
