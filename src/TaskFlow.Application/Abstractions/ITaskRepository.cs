using TaskFlow.Application.Common.Paging;
using TaskFlow.Application.Tasks;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Abstractions;

public interface ITaskRepository
{
    void Add(TaskItem task);
    void Remove(TaskItem task);

    /// <summary>Tarea con sus etiquetas, con tracking (para modificarla).</summary>
    Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<TaskDto?> GetDtoAsync(Guid id, CancellationToken ct);

    /// <summary>Posición para agregar una tarea al final de la columna <paramref name="status"/>.</summary>
    Task<decimal> GetNextPositionAsync(Guid projectId, TaskItemStatus status, CancellationToken ct);

    /// <summary>
    /// Posiciones de las vecinas entre las que va a quedar una tarea movida: la tarea <paramref name="afterTaskId"/>
    /// (o ninguna = primera) y la siguiente en la columna. Excluye a la tarea que se está moviendo.
    /// Devuelve null si <paramref name="afterTaskId"/> no está en esa columna.
    /// </summary>
    Task<(decimal? Previous, decimal? Next)?> GetNeighborPositionsAsync(
        Guid projectId, TaskItemStatus status, Guid? afterTaskId, Guid movingTaskId, CancellationToken ct);

    /// <summary>Renumera la columna con separación uniforme cuando el fractional indexing se quedó sin decimales.</summary>
    Task RebalanceColumnAsync(Guid projectId, TaskItemStatus status, CancellationToken ct);

    /// <summary>Board: todas las tareas del proyecto, ordenadas por columna y posición.</summary>
    Task<IReadOnlyList<TaskDto>> ListByProjectAsync(Guid projectId, CancellationToken ct);

    Task<IReadOnlyList<TaskDto>> SearchAsync(TaskQuery query, Cursor? after, int take, CancellationToken ct);

    /// <summary>Soft delete masivo (un UPDATE) de las tareas de un proyecto eliminado.</summary>
    Task<int> SoftDeleteByProjectAsync(Guid projectId, DateTime utcNow, CancellationToken ct);
}
