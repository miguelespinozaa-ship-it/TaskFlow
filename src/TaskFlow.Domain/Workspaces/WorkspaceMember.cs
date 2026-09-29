using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Workspaces;

/// <summary>Membresía N:M usuario ↔ workspace, con rol. El usuario se referencia solo por Id:
/// la identidad (Identity) es un detalle de infraestructura que el dominio no conoce.</summary>
public sealed class WorkspaceMember : ITenantEntity
{
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; private init; }
    public WorkspaceRole Role { get; private set; }
    public DateTime JoinedAt { get; private init; }

    private WorkspaceMember() { } // EF Core

    public static WorkspaceMember Create(Guid workspaceId, Guid userId, WorkspaceRole role, DateTime utcNow)
    {
        if (workspaceId == Guid.Empty || userId == Guid.Empty)
            throw new DomainException("La membresía necesita workspace y usuario.");
        if (!Enum.IsDefined(role))
            throw new DomainException("Rol inválido.");

        return new WorkspaceMember { WorkspaceId = workspaceId, UserId = userId, Role = role, JoinedAt = utcNow };
    }

    public void ChangeRole(WorkspaceRole role)
    {
        if (!Enum.IsDefined(role))
            throw new DomainException("Rol inválido.");
        if (Role == WorkspaceRole.Owner)
            throw new DomainException("El rol del owner no se cambia; primero hay que transferir la propiedad.");
        Role = role;
    }
}
