using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common.Paging;
using TaskFlow.Application.Tasks;
using TaskFlow.Domain.Tasks;
using TaskFlow.Infrastructure.Persistence.Configurations;

namespace TaskFlow.Infrastructure.Persistence.Repositories;

internal sealed class TaskRepository(AppDbContext db) : ITaskRepository
{
    // Proyección directa a DTO: sin tracking, sin traer columnas que la API no expone, y las etiquetas
    // en la MISMA query (sin N+1).
    private static readonly Expression<Func<TaskItem, TaskDto>> ToDto = t => new TaskDto(
        t.Id, t.ProjectId, t.Title, t.Description, t.Status, t.Priority, t.Position, t.AssigneeId, t.ReporterId,
        t.DueAt, t.CompletedAt, t.CreatedAt, t.UpdatedAt, t.Labels.Select(l => l.LabelId).ToList());

    public void Add(TaskItem task) => db.Tasks.Add(task);

    public void Remove(TaskItem task) => db.Tasks.Remove(task);

    public Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Tasks.Include(t => t.Labels).FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<TaskDto?> GetDtoAsync(Guid id, CancellationToken ct) =>
        db.Tasks.AsNoTracking().Where(t => t.Id == id).Select(ToDto).FirstOrDefaultAsync(ct);

    public async Task<decimal> GetNextPositionAsync(Guid projectId, TaskItemStatus status, CancellationToken ct)
    {
        var max = await db.Tasks
            .Where(t => t.ProjectId == projectId && t.Status == status)
            .MaxAsync(t => (decimal?)t.Position, ct);
        return BoardPosition.Between(max, null);
    }

    public async Task<(decimal? Previous, decimal? Next)?> GetNeighborPositionsAsync(
        Guid projectId, TaskItemStatus status, Guid? afterTaskId, Guid movingTaskId, CancellationToken ct)
    {
        var column = db.Tasks.Where(t => t.ProjectId == projectId && t.Status == status && t.Id != movingTaskId);

        decimal? previous = null;
        if (afterTaskId is Guid afterId)
        {
            previous = await column.Where(t => t.Id == afterId).Select(t => (decimal?)t.Position).FirstOrDefaultAsync(ct);
            if (previous is null)
                return null;
        }

        var below = previous is decimal p ? column.Where(t => t.Position > p) : column;
        var next = await below.MinAsync(t => (decimal?)t.Position, ct);
        return (previous, next);
    }

    public Task RebalanceColumnAsync(Guid projectId, TaskItemStatus status, CancellationToken ct) =>
        // Un solo UPDATE con window function: 1000, 2000, 3000... en el orden actual.
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE tasks AS t
            SET position = s.rn * {BoardPosition.Gap}
            FROM (
                SELECT id, row_number() OVER (ORDER BY position, id) AS rn
                FROM tasks
                WHERE project_id = {projectId} AND status = {status.ToString()} AND deleted_at IS NULL
            ) AS s
            WHERE t.id = s.id
            """, ct);

    public async Task<IReadOnlyList<TaskDto>> ListByProjectAsync(Guid projectId, CancellationToken ct) =>
        await db.Tasks
            .AsNoTracking()
            .Where(t => t.ProjectId == projectId)
            .OrderBy(t => t.Status).ThenBy(t => t.Position)
            .Select(ToDto)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TaskDto>> SearchAsync(TaskQuery query, Cursor? after, int take, CancellationToken ct)
    {
        var tasks = db.Tasks.AsNoTracking();

        if (query.ProjectId is Guid projectId) tasks = tasks.Where(t => t.ProjectId == projectId);
        if (query.Status is TaskItemStatus status) tasks = tasks.Where(t => t.Status == status);
        if (query.AssigneeId is Guid assigneeId) tasks = tasks.Where(t => t.AssigneeId == assigneeId);
        if (query.LabelId is Guid labelId) tasks = tasks.Where(t => t.Labels.Any(l => l.LabelId == labelId));

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // websearch_to_tsquery acepta lo que escribe un humano ("bug login -safari", "\"frase exacta\"")
            // sin romper con caracteres especiales, a diferencia de to_tsquery.
            var search = query.Search.Trim();
            tasks = tasks.Where(t => EF.Property<NpgsqlTsVector>(t, TaskItemConfiguration.SearchVector)
                .Matches(EF.Functions.WebSearchToTsQuery("spanish", search)));
        }

        if (after is Cursor cursor)
        {
            // Comparación de tuplas de Postgres: (created_at, id) < (@c, @id). Usa el índice compuesto.
            tasks = tasks.Where(t => EF.Functions.LessThan(
                ValueTuple.Create(t.CreatedAt, t.Id),
                ValueTuple.Create(cursor.CreatedAt, cursor.Id)));
        }

        return await tasks
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Take(take)
            .Select(ToDto)
            .ToListAsync(ct);
    }

    public Task<int> SoftDeleteByProjectAsync(Guid projectId, DateTime utcNow, CancellationToken ct) =>
        db.Tasks
            .Where(t => t.ProjectId == projectId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.DeletedAt, utcNow), ct);
}
