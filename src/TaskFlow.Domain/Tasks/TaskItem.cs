using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Domain.Tasks;

// "TaskItem" y no "Task" para no chocar con System.Threading.Tasks.Task en todo el código async.
public sealed class TaskItem : Entity, ITenantEntity
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 5000;

    public Guid WorkspaceId { get; set; }
    public Guid ProjectId { get; private init; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public TaskItemStatus Status { get; private set; }
    public TaskPriority Priority { get; private set; }
    public decimal Position { get; private set; }
    public DateTime? DueAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime CreatedAt { get; private init; }
    public DateTime UpdatedAt { get; private set; }

    private TaskItem() { } // EF Core

    public static TaskItem Create(
        Project project, string title, string? description, TaskPriority priority, decimal position, DateTime utcNow)
    {
        if (project.IsArchived)
            throw new DomainException("No se pueden crear tareas en un proyecto archivado.");

        var task = new TaskItem
        {
            // El workspace se hereda del proyecto: una tarea nunca puede quedar en otro tenant.
            WorkspaceId = project.WorkspaceId,
            ProjectId = project.Id,
            Status = TaskItemStatus.Todo,
            Priority = priority,
            Position = position,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };
        task.SetTitle(title);
        task.SetDescription(description);
        return task;
    }

    public void Rename(string title, DateTime utcNow)
    {
        SetTitle(title);
        UpdatedAt = utcNow;
    }

    public void MoveTo(TaskItemStatus status, decimal position, DateTime utcNow)
    {
        Status = status;
        Position = position;
        CompletedAt = status == TaskItemStatus.Done ? CompletedAt ?? utcNow : null;
        UpdatedAt = utcNow;
    }

    public void MarkCompleted(DateTime utcNow)
    {
        if (Status == TaskItemStatus.Done)
            throw new DomainException("La tarea ya está completada.");

        Status = TaskItemStatus.Done;
        CompletedAt = utcNow;
        UpdatedAt = utcNow;
    }

    private void SetTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("El título es obligatorio.");
        title = title.Trim();
        if (title.Length > TitleMaxLength)
            throw new DomainException($"El título no puede superar {TitleMaxLength} caracteres.");
        Title = title;
    }

    private void SetDescription(string? description)
    {
        if (description?.Length > DescriptionMaxLength)
            throw new DomainException($"La descripción no puede superar {DescriptionMaxLength} caracteres.");
        Description = string.IsNullOrWhiteSpace(description) ? null : description;
    }
}
