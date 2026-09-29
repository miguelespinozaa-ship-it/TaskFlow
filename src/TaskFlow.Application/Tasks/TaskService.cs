using FluentValidation;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common.Exceptions;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Tasks;

public interface ITaskService
{
    Task<TaskDto> CreateAsync(Guid projectId, CreateTaskRequest request, CancellationToken ct);
    Task<IReadOnlyList<TaskDto>> ListByProjectAsync(Guid projectId, CancellationToken ct);
}

public sealed class TaskService(
    IProjectRepository projects,
    ITaskRepository tasks,
    IUnitOfWork unitOfWork,
    IValidator<CreateTaskRequest> validator,
    TimeProvider clock) : ITaskService
{
    public async Task<TaskDto> CreateAsync(Guid projectId, CreateTaskRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        var project = await projects.GetByIdAsync(projectId, ct)
            ?? throw new NotFoundException("Proyecto no encontrado.");

        var position = await tasks.GetNextPositionAsync(projectId, TaskItemStatus.Todo, ct);
        var task = TaskItem.Create(
            project, request.Title, request.Description, request.Priority, position, clock.GetUtcNow().UtcDateTime);

        tasks.Add(task);
        await unitOfWork.SaveChangesAsync(ct);

        return TaskDto.From(task);
    }

    public async Task<IReadOnlyList<TaskDto>> ListByProjectAsync(Guid projectId, CancellationToken ct)
    {
        // 404 explícito: una lista vacía no distingue "sin tareas" de "proyecto inexistente".
        if (!await projects.ExistsAsync(projectId, ct))
            throw new NotFoundException("Proyecto no encontrado.");

        return await tasks.ListByProjectAsync(projectId, ct);
    }
}
