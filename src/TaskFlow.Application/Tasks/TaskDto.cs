using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Tasks;

public sealed record TaskDto(
    Guid Id,
    Guid ProjectId,
    string Title,
    string? Description,
    TaskItemStatus Status,
    TaskPriority Priority,
    decimal Position,
    DateTime CreatedAt)
{
    public static TaskDto From(TaskItem t) =>
        new(t.Id, t.ProjectId, t.Title, t.Description, t.Status, t.Priority, t.Position, t.CreatedAt);
}
