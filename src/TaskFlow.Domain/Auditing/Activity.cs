using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Auditing;

/// <summary>
/// Registro de auditoría. Una sola tabla para todas las entidades: el detalle de cada cambio va en
/// <see cref="Changes"/> (jsonb) en vez de tener una tabla de historial por cada tipo.
/// Inmutable: se inserta y nunca se modifica.
/// </summary>
public sealed class Activity : Entity, ITenantEntity
{
    public Guid WorkspaceId { get; set; }

    /// <summary>Null = acción del sistema (seed, jobs).</summary>
    public Guid? ActorId { get; private init; }

    public string EntityType { get; private init; } = null!;
    public Guid EntityId { get; private init; }
    public string Action { get; private init; } = null!;

    /// <summary>JSON: { "campo": { "from": ..., "to": ... } } en updates, snapshot mínimo en creates.</summary>
    public string Changes { get; private init; } = null!;

    public DateTime CreatedAt { get; private init; }

    private Activity() { } // EF Core

    public static Activity Create(
        Guid workspaceId, Guid? actorId, string entityType, Guid entityId, string action, string changesJson, DateTime utcNow) =>
        new()
        {
            WorkspaceId = workspaceId,
            ActorId = actorId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            Changes = changesJson,
            CreatedAt = utcNow,
        };
}

public static class ActivityActions
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Deleted = "deleted";
}
