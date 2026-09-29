namespace TaskFlow.Application.Abstractions;

/// <summary>Workspace activo del request. NULL = contexto de sistema (seed, jobs, migraciones).</summary>
public interface ITenantContext
{
    Guid? WorkspaceId { get; }
}

public static class TenantContextExtensions
{
    public static Guid RequireWorkspaceId(this ITenantContext tenant) =>
        tenant.WorkspaceId ?? throw new InvalidOperationException(
            "Operación de negocio sin workspace activo. Todo endpoint de negocio debe requerir autenticación.");
}
