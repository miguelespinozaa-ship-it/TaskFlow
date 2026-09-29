using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Projects;

public sealed record ProjectDto(
    Guid Id, Guid WorkspaceId, string Name, string? Description, string KeyPrefix, bool IsArchived, DateTime CreatedAt)
{
    public static ProjectDto From(Project p) =>
        new(p.Id, p.WorkspaceId, p.Name, p.Description, p.KeyPrefix, p.IsArchived, p.CreatedAt);
}
