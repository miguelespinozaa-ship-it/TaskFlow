using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Domain.Tasks;

// "TaskItem" y no "Task" para no chocar con System.Threading.Tasks.Task en todo el código async.
public sealed class TaskItem : Entity, ITenantEntity, ISoftDeletable, IAuditable
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 5000;
    public const int MaxLabels = 10;

    private readonly List<TaskLabel> _labels = [];

    public Guid WorkspaceId { get; set; }
    public Guid ProjectId { get; private init; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public TaskItemStatus Status { get; private set; }
    public TaskPriority Priority { get; private set; }
    public decimal Position { get; private set; }
    public Guid? AssigneeId { get; private set; }

    /// <summary>Quién la creó. Null solo en tareas anteriores a la autenticación (fase 1).</summary>
    public Guid? ReporterId { get; private init; }

    public DateTime? DueAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime CreatedAt { get; private init; }
    public DateTime UpdatedAt { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public IReadOnlyCollection<TaskLabel> Labels => _labels;

    private TaskItem() { } // EF Core

    public static TaskItem Create(
        Project project, string title, string? description, TaskPriority priority, decimal position, DateTime utcNow,
        Guid? reporterId = null)
    {
        if (project.IsArchived)
            throw new DomainException("No se pueden crear tareas en un proyecto archivado.");

        var task = new TaskItem
        {
            // El workspace se hereda del proyecto: una tarea nunca puede quedar en otro tenant.
            WorkspaceId = project.WorkspaceId,
            ProjectId = project.Id,
            Status = TaskItemStatus.Todo,
            Position = position,
            ReporterId = reporterId,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };
        task.SetTitle(title);
        task.SetDescription(description);
        task.SetPriority(priority);
        return task;
    }

    public void Rename(string title, DateTime utcNow)
    {
        SetTitle(title);
        Touch(utcNow);
    }

    public void ChangeDescription(string? description, DateTime utcNow)
    {
        SetDescription(description);
        Touch(utcNow);
    }

    public void ChangePriority(TaskPriority priority, DateTime utcNow)
    {
        SetPriority(priority);
        Touch(utcNow);
    }

    public void ChangeDueDate(DateTime? dueAt, DateTime utcNow)
    {
        DueAt = dueAt;
        Touch(utcNow);
    }

    /// <summary>La pertenencia del usuario al workspace la valida la capa de aplicación (el dominio no ve miembros).</summary>
    public void AssignTo(Guid? userId, DateTime utcNow)
    {
        if (userId == Guid.Empty)
            throw new DomainException("Usuario inválido.");
        AssigneeId = userId;
        Touch(utcNow);
    }

    public void MoveTo(TaskItemStatus status, decimal position, DateTime utcNow)
    {
        if (!Enum.IsDefined(status))
            throw new DomainException("Estado inválido.");
        Status = status;
        Position = position;
        CompletedAt = status == TaskItemStatus.Done ? CompletedAt ?? utcNow : null;
        Touch(utcNow);
    }

    public void MarkCompleted(DateTime utcNow)
    {
        if (Status == TaskItemStatus.Done)
            throw new DomainException("La tarea ya está completada.");

        Status = TaskItemStatus.Done;
        CompletedAt = utcNow;
        Touch(utcNow);
    }

    public void SetLabels(IReadOnlyCollection<Guid> labelIds, DateTime utcNow)
    {
        var distinct = labelIds.Distinct().ToList();
        if (distinct.Count > MaxLabels)
            throw new DomainException($"Una tarea admite como máximo {MaxLabels} etiquetas.");

        _labels.RemoveAll(l => !distinct.Contains(l.LabelId));
        foreach (var id in distinct.Where(id => _labels.All(l => l.LabelId != id)))
            _labels.Add(new TaskLabel(Id, id));
        Touch(utcNow);
    }

    public void MarkDeleted(DateTime utcNow)
    {
        DeletedAt ??= utcNow;
    }

    private void Touch(DateTime utcNow) => UpdatedAt = utcNow;

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

    private void SetPriority(TaskPriority priority)
    {
        if (!Enum.IsDefined(priority))
            throw new DomainException("Prioridad inválida.");
        Priority = priority;
    }
}

/// <summary>Fila de la relación N:M tarea ↔ etiqueta.</summary>
public sealed class TaskLabel(Guid taskId, Guid labelId)
{
    public Guid TaskId { get; private init; } = taskId;
    public Guid LabelId { get; private init; } = labelId;
}
