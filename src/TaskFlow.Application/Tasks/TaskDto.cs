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
    Guid? AssigneeId,
    Guid? ReporterId,
    DateTime? DueAt,
    DateTime? CompletedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<Guid> LabelIds)
{
    public static TaskDto From(TaskItem t) => new(
        t.Id, t.ProjectId, t.Title, t.Description, t.Status, t.Priority, t.Position, t.AssigneeId, t.ReporterId,
        t.DueAt, t.CompletedAt, t.CreatedAt, t.UpdatedAt, [.. t.Labels.Select(l => l.LabelId)]);
}
