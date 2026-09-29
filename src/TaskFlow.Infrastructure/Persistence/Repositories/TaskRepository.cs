using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Tasks;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Infrastructure.Persistence.Repositories;

internal sealed class TaskRepository(AppDbContext db) : ITaskRepository
{
    private const decimal PositionGap = 1000m;

    public void Add(TaskItem task) => db.Tasks.Add(task);

    public async Task<decimal> GetNextPositionAsync(Guid projectId, TaskItemStatus status, CancellationToken ct)
    {
        var max = await db.Tasks
            .Where(t => t.ProjectId == projectId && t.Status == status)
            .MaxAsync(t => (decimal?)t.Position, ct);
        return (max ?? 0m) + PositionGap;
    }

    // Proyección directa a DTO: sin tracking, sin traer columnas que la API no expone.
    public async Task<IReadOnlyList<TaskDto>> ListByProjectAsync(Guid projectId, CancellationToken ct) =>
        await db.Tasks
            .AsNoTracking()
            .Where(t => t.ProjectId == projectId)
            .OrderBy(t => t.Status).ThenBy(t => t.Position)
            .Select(t => new TaskDto(t.Id, t.ProjectId, t.Title, t.Description, t.Status, t.Priority, t.Position, t.CreatedAt))
            .ToListAsync(ct);
}
