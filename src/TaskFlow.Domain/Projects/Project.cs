using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Projects;

public sealed class Project : Entity, ITenantEntity, ISoftDeletable, IAuditable
{
    public const int NameMaxLength = 120;
    public const int DescriptionMaxLength = 2000;

    public Guid WorkspaceId { get; set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string KeyPrefix { get; private set; } = null!;
    public bool IsArchived { get; private set; }
    public DateTime CreatedAt { get; private init; }
    public DateTime? DeletedAt { get; private set; }

    private Project() { } // EF Core

    public static Project Create(Guid workspaceId, string name, string keyPrefix, string? description, DateTime utcNow)
    {
        if (workspaceId == Guid.Empty)
            throw new DomainException("Un proyecto tiene que pertenecer a un workspace.");
        if (string.IsNullOrWhiteSpace(keyPrefix) || keyPrefix.Length > 10 || !keyPrefix.All(char.IsAsciiLetterUpper))
            throw new DomainException("El prefijo debe tener entre 1 y 10 letras mayúsculas.");

        var project = new Project { WorkspaceId = workspaceId, KeyPrefix = keyPrefix, CreatedAt = utcNow };
        project.Rename(name);
        project.ChangeDescription(description);
        return project;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre del proyecto es obligatorio.");
        name = name.Trim();
        if (name.Length > NameMaxLength)
            throw new DomainException($"El nombre no puede superar {NameMaxLength} caracteres.");
        Name = name;
    }

    public void ChangeDescription(string? description)
    {
        if (description?.Length > DescriptionMaxLength)
            throw new DomainException($"La descripción no puede superar {DescriptionMaxLength} caracteres.");
        Description = string.IsNullOrWhiteSpace(description) ? null : description;
    }

    public void Archive()
    {
        if (IsArchived)
            throw new DomainException("El proyecto ya está archivado.");
        IsArchived = true;
    }

    public void Unarchive()
    {
        if (!IsArchived)
            throw new DomainException("El proyecto no está archivado.");
        IsArchived = false;
    }

    public void MarkDeleted(DateTime utcNow)
    {
        DeletedAt ??= utcNow;
    }
}
