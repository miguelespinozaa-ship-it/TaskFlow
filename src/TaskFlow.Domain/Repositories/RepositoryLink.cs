using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Domain.Repositories;

/// <summary>Conexión de un proyecto con un repositorio de GitHub. Un proyecto tiene como mucho una.</summary>
public sealed class RepositoryLink : Entity, ITenantEntity, IAuditable
{
    public const int ErrorMaxLength = 500;

    public Guid WorkspaceId { get; set; }
    public Guid ProjectId { get; private init; }
    public string Owner { get; private init; } = null!;
    public string Name { get; private init; } = null!;
    public string DefaultBranch { get; private init; } = null!;
    public DateTime CreatedAt { get; private init; }

    /// <summary>
    /// ETag de la última lista de commits. Se manda como If-None-Match: si no hubo commits nuevos,
    /// GitHub responde 304 y esa consulta NO descuenta del límite de requests.
    /// </summary>
    public string? ETag { get; private set; }

    public DateTime? LastSyncedAt { get; private set; }
    public string? LastSyncError { get; private set; }

    private RepositoryLink() { } // EF Core

    public static RepositoryLink Create(Project project, RepositoryName repository, string defaultBranch, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(defaultBranch))
            throw new DomainException("El repositorio no tiene rama por defecto.");

        return new RepositoryLink
        {
            // Igual que las tareas: el tenant se hereda del proyecto.
            WorkspaceId = project.WorkspaceId,
            ProjectId = project.Id,
            Owner = repository.Owner,
            Name = repository.Name,
            DefaultBranch = defaultBranch,
            CreatedAt = utcNow,
        };
    }

    public RepositoryName Repository => new(Owner, Name);

    public void MarkSynced(DateTime utcNow, string? etag)
    {
        LastSyncedAt = utcNow;
        LastSyncError = null;
        ETag = etag;
    }

    public void MarkFailed(string error)
    {
        LastSyncError = error.Length > ErrorMaxLength ? error[..ErrorMaxLength] : error;
    }
}
