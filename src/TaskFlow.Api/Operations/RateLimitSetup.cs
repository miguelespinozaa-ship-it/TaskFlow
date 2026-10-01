using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace TaskFlow.Api.Operations;

public sealed class AuthRateLimitOptions
{
    public const string SectionName = "RateLimiting:Auth";

    /// <summary>Intentos de login/registro permitidos por dirección IP en cada ventana.</summary>
    [Range(1, int.MaxValue)]
    public int PermitLimit { get; init; } = 10;

    [Range(1, 3600)]
    public int WindowSeconds { get; init; } = 60;
}

public static class RateLimitSetup
{
    /// <summary>Política para login y registro: frena la adivinación de contraseñas y el alta masiva de cuentas.</summary>
    public const string AuthPolicy = "auth";

    public static IServiceCollection AddTaskFlowRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<AuthRateLimitOptions>().BindConfiguration(AuthRateLimitOptions.SectionName)
            .ValidateDataAnnotations().ValidateOnStart();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Por IP y no por cuenta: el bloqueo por cuenta ya lo hace Identity (5 intentos fallidos). Este límite
            // cubre el otro ataque: probar UNA contraseña común contra MUCHAS cuentas desde la misma máquina.
            limiter.AddPolicy(AuthPolicy, http =>
            {
                var options = http.RequestServices.GetRequiredService<IOptions<AuthRateLimitOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.PermitLimit,
                        Window = TimeSpan.FromSeconds(options.WindowSeconds),
                        QueueLimit = 0, // se rechaza de inmediato: encolar intentos de login no tiene sentido
                    });
            });

            limiter.OnRejected = async (context, ct) =>
            {
                var http = context.HttpContext;
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : TimeSpan.FromMinutes(1);
                http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

                await http.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = http,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Demasiados intentos. Espera un momento antes de volver a probar.",
                        Type = "https://httpstatuses.io/429",
                    },
                });
            };
        });
        return services;
    }

    /// <summary>
    /// Detrás de un proxy inverso (nginx en docker compose) todas las conexiones llegan desde la IP del proxy:
    /// sin esto, el límite por IP sería un único límite global. Solo se activa por configuración, porque confiar
    /// en X-Forwarded-For con la API expuesta directamente permitiría a cualquiera falsificar su IP.
    /// </summary>
    public static void UseTaskFlowForwardedHeaders(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
            return;

        var options = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
        // El proxy está en la red interna de Docker, con IP variable: se confía en el que conecte.
        // Es seguro mientras el puerto de la API NO esté publicado hacia afuera (en el compose no lo está).
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        app.UseForwardedHeaders(options);
    }
}
