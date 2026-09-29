using FluentValidation;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Tasks;

public sealed record CreateTaskRequest(string Title, string? Description, TaskPriority Priority = TaskPriority.Medium);

public sealed class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("El título es obligatorio.")
            .MaximumLength(TaskItem.TitleMaxLength);
        RuleFor(x => x.Description).MaximumLength(TaskItem.DescriptionMaxLength);
        RuleFor(x => x.Priority).IsInEnum();
    }
}
