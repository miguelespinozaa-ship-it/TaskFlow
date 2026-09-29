using TaskFlow.Application.Tasks;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Abstractions;

public interface ITaskRepository
{
    void Add(TaskItem task);

    /// <summary>Posición para agregar una tarea al final de la columna <paramref name="status"/>.</summary>
    Task<decimal> GetNextPositionAsync(Guid projectId, TaskItemStatus status, CancellationToken ct);

    Task<IReadOnlyList<TaskDto>> ListByProjectAsync(Guid projectId, CancellationToken ct);
}
