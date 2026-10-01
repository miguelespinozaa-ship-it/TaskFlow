using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Repositories;
using TaskFlow.Infrastructure.Tenancy;

namespace TaskFlow.Infrastructure.GitHub;

/// <summary>Una pasada de sincronización sobre todos los repositorios enlazados. Separada del worker para poder testearla.</summary>
public sealed class RepositorySyncRunner(IServiceScopeFactory scopes, ILogger<RepositorySyncRunner> logger)
{
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<(Guid ProjectId, Guid WorkspaceId)> links;
        using (var scope = scopes.CreateScope())
            links = await scope.ServiceProvider.GetRequiredService<IRepositoryLinkRepository>().ListAllForSyncAsync(ct);

        var imported = 0;
        foreach (var (projectId, workspaceId) in links)
        {
            // Un scope por repositorio: DbContext propio, y el tenant fijado ANTES de tocar la base, así
            // el job corre con el mismo aislamiento que un request de ese workspace.
            using var scope = scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<TenantContext>().Set(workspaceId);
            try
            {
                var result = await scope.ServiceProvider.GetRequiredService<IRepositoryService>().SyncAsync(projectId, ct);
                imported += result.Imported;
            }
            catch (GitHubUnavailableException ex)
            {
                // Sin cuota o GitHub caído: no tiene sentido seguir con los demás en esta pasada.
                logger.LogWarning("Sincronización de repositorios interrumpida: {Message}", ex.Message);
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Un repositorio que falla no debe impedir que se sincronicen los demás.
                logger.LogError(ex, "Falló la sincronización del proyecto {ProjectId}", projectId);
            }
        }

        if (imported > 0)
            logger.LogInformation("Sincronización de repositorios: {Imported} commits nuevos", imported);
        return imported;
    }
}

public sealed class RepositorySyncWorker(
    RepositorySyncRunner runner,
    IOptions<GitHubOptions> options,
    ILogger<RepositorySyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = options.Value.SyncIntervalMinutes;
        if (minutes <= 0)
        {
            logger.LogInformation("Sincronización periódica de repositorios desactivada");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await runner.RunOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Una pasada fallida no puede matar al worker: quedaría apagado hasta el próximo deploy.
                    logger.LogError(ex, "Falló la pasada de sincronización de repositorios");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Apagado normal de la app.
        }
    }
}
