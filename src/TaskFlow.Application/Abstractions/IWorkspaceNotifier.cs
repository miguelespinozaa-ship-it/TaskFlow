namespace TaskFlow.Application.Abstractions;

/// <summary>Avisa en tiempo real a quienes tienen abierto un workspace que algo cambió en él.</summary>
public interface IWorkspaceNotifier
{
    Task WorkspaceChangedAsync(Guid workspaceId, CancellationToken ct);
}
