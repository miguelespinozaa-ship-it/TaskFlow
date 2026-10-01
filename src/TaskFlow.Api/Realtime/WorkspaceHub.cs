using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using TaskFlow.Application.Abstractions;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Api.Realtime;

/// <summary>
/// Canal en tiempo real de un workspace. El cliente no elige a qué grupo entra: sale del claim firmado
/// del token, igual que el tenant en la API. No hay método para unirse a otro grupo.
/// </summary>
[Authorize]
public sealed class WorkspaceHub : Hub
{
    public const string Path = "/hubs/workspace";
    public const string ChangedEvent = "WorkspaceChanged";

    public static string Group(Guid workspaceId) => $"workspace:{workspaceId}";

    public override async Task OnConnectedAsync()
    {
        if (!Guid.TryParse(Context.User?.FindFirst(TaskFlowClaims.WorkspaceId)?.Value, out var workspaceId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, Group(workspaceId));
        await base.OnConnectedAsync();
    }
}

public sealed class SignalRWorkspaceNotifier(IHubContext<WorkspaceHub> hub, ILogger<SignalRWorkspaceNotifier> logger)
    : IWorkspaceNotifier
{
    public async Task WorkspaceChangedAsync(Guid workspaceId, CancellationToken ct)
    {
        try
        {
            // El evento no lleva datos: solo dice "algo cambió" y cada cliente vuelve a pedir lo que muestra,
            // con SU token. Así no hay forma de filtrar por este canal algo que la API no le daría.
            await hub.Clients.Group(WorkspaceHub.Group(workspaceId)).SendAsync(WorkspaceHub.ChangedEvent, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // El cambio ya se guardó: que falle el aviso no puede convertir el request en un error.
            logger.LogWarning(ex, "No se pudo notificar el cambio del workspace {WorkspaceId}", workspaceId);
        }
    }
}
