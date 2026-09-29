using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Projects;

public sealed class Project : Entity, ITenantEntity
{
    public Guid WorkspaceId { get; set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string KeyPrefix { get; private set; } = null!;
    public bool IsArchived { get; private set; }
    public DateTime CreatedAt { get; private init; }

    private Project() { } // EF Core

    public static Project Create(Guid workspaceId, string name, string keyPrefix, string? description, DateTime utcNow)
    {
        if (workspaceId == Guid.Empty)
            throw new DomainException("Un proyecto tiene que pertenecer a un workspace.");
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre del proyecto es obligatorio.");
        if (string.IsNullOrWhiteSpace(keyPrefix) || keyPrefix.Length > 10 || !keyPrefix.All(char.IsAsciiLetterUpper))
            throw new DomainException("El prefijo debe tener entre 1 y 10 letras mayúsculas.");

        return new Project
        {
            WorkspaceId = workspaceId,
            Name = name.Trim(),
            KeyPrefix = keyPrefix,
            Description = description,
            CreatedAt = utcNow,
        };
    }

    public void Archive()
    {
        if (IsArchived)
            throw new DomainException("El proyecto ya está archivado.");
        IsArchived = true;
    }
}
