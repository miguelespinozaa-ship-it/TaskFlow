using FluentValidation;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Tasks;

public sealed record CreateTaskRequest(
    string Title,
    string? Description,
    TaskPriority Priority = TaskPriority.Medium,
    Guid? AssigneeId = null,
    DateTime? DueAt = null,
    IReadOnlyList<Guid>? LabelIds = null);

/// <summary>PATCH: un campo null = "no cambiar". Para vaciar la descripción, "" ; para quitar el vencimiento, ClearDueAt.</summary>
public sealed record UpdateTaskRequest(
    string? Title = null,
    string? Description = null,
    TaskPriority? Priority = null,
    DateTime? DueAt = null,
    bool ClearDueAt = false);

/// <summary>Mover en el board: columna destino + la tarea que queda justo arriba (null = primera de la columna).</summary>
public sealed record MoveTaskRequest(TaskItemStatus Status, Guid? AfterTaskId = null);

public sealed record AssignTaskRequest(Guid? AssigneeId);

public sealed record SetLabelsRequest(IReadOnlyList<Guid> LabelIds);

public sealed record TaskQuery(
    Guid? ProjectId = null,
    TaskItemStatus? Status = null,
    Guid? AssigneeId = null,
    Guid? LabelId = null,
    string? Search = null,
    string? Cursor = null,
    int? PageSize = null);

public sealed class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("El título es obligatorio.")
            .MaximumLength(TaskItem.TitleMaxLength);
        RuleFor(x => x.Description).MaximumLength(TaskItem.DescriptionMaxLength);
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.LabelIds!.Count).LessThanOrEqualTo(TaskItem.MaxLabels)
            .When(x => x.LabelIds is not null).OverridePropertyName(nameof(CreateTaskRequest.LabelIds));
    }
}

public sealed class UpdateTaskRequestValidator : AbstractValidator<UpdateTaskRequest>
{
    public UpdateTaskRequestValidator()
    {
        RuleFor(x => x.Title!).NotEmpty().MaximumLength(TaskItem.TitleMaxLength).When(x => x.Title is not null);
        RuleFor(x => x.Description).MaximumLength(TaskItem.DescriptionMaxLength);
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.DueAt).Null().When(x => x.ClearDueAt).WithMessage("No se puede fijar y quitar el vencimiento a la vez.");
    }
}

public sealed class MoveTaskRequestValidator : AbstractValidator<MoveTaskRequest>
{
    public MoveTaskRequestValidator() => RuleFor(x => x.Status).IsInEnum();
}

public sealed class SetLabelsRequestValidator : AbstractValidator<SetLabelsRequest>
{
    public SetLabelsRequestValidator() =>
        RuleFor(x => x.LabelIds).NotNull().Must(ids => ids.Distinct().Count() <= TaskItem.MaxLabels)
            .WithMessage($"Máximo {TaskItem.MaxLabels} etiquetas.");
}

public sealed class TaskQueryValidator : AbstractValidator<TaskQuery>
{
    public TaskQueryValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Search).MaximumLength(200);
    }
}
