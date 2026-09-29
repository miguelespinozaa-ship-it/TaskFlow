namespace TaskFlow.Application.Projects;

public sealed record ProjectDto(Guid Id, Guid WorkspaceId, string Name, string? Description, string KeyPrefix, DateTime CreatedAt);
