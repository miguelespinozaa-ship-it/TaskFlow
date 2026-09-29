using TaskFlow.Application.Abstractions;

namespace TaskFlow.Infrastructure.Tenancy;

/// <summary>Scoped: uno por request. Lo llena TenantResolutionMiddleware a partir del claim firmado del JWT.</summary>
public sealed class TenantContext : ITenantContext
{
    public Guid? WorkspaceId { get; private set; }

    public void Set(Guid workspaceId)
    {
        if (WorkspaceId is not null && WorkspaceId != workspaceId)
            throw new InvalidOperationException("El tenant de un request no puede cambiar una vez resuelto.");
        WorkspaceId = workspaceId;
    }
}
