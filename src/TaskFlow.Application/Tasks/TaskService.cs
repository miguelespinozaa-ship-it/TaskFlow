using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Application.Common.Exceptions;
using TaskFlow.Application.Common.Paging;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Tasks;

public interface ITaskService
{
    Task<TaskDto> CreateAsync(Guid projectId, CreateTaskRequest request, CancellationToken ct);
    Task<IReadOnlyList<TaskDto>> ListByProjectAsync(Guid projectId, CancellationToken ct);
    Task<CursorPage<TaskDto>> SearchAsync(TaskQuery query, CancellationToken ct);
    Task<TaskDto> GetAsync(Guid id, CancellationToken ct);
    Task<TaskDto> UpdateAsync(Guid id, UpdateTaskRequest request, CancellationToken ct);
    Task<TaskDto> MoveAsync(Guid id, MoveTaskRequest request, CancellationToken ct);
    Task<TaskDto> AssignAsync(Guid id, AssignTaskRequest request, CancellationToken ct);
    Task<TaskDto> SetLabelsAsync(Guid id, SetLabelsRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}

public sealed class TaskService(
    IProjectRepository projects,
    ITaskRepository tasks,
    ILabelRepository labels,
    IWorkspaceRepository workspaces,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenant,
    IRequestValidator validator,
    TimeProvider clock) : ITaskService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<TaskDto> CreateAsync(Guid projectId, CreateTaskRequest request, CancellationToken ct)
    {
        await validator.ValidateAsync(request, ct);

        var project = await projects.GetByIdAsync(projectId, ct)
            ?? throw new NotFoundException("Proyecto no encontrado.");

        var position = await tasks.GetNextPositionAsync(projectId, TaskItemStatus.Todo, ct);
        var task = TaskItem.Create(
            project, request.Title, request.Description, request.Priority, position, Now, currentUser.UserId);

        if (request.DueAt is not null)
            task.ChangeDueDate(request.DueAt, Now);
        if (request.AssigneeId is not null)
            task.AssignTo(await RequireMemberAsync(request.AssigneeId.Value, ct), Now);
        if (request.LabelIds is { Count: > 0 })
            task.SetLabels(await RequireLabelsAsync(request.LabelIds, ct), Now);

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

    public async Task<CursorPage<TaskDto>> SearchAsync(TaskQuery query, CancellationToken ct)
    {
        await validator.ValidateAsync(query, ct);
        var pageSize = Cursor.ClampPageSize(query.PageSize);

        // Se pide uno de más: si llega, hay página siguiente. Evita un COUNT(*) aparte.
        var items = await tasks.SearchAsync(query, Cursor.Decode(query.Cursor), pageSize + 1, ct);
        if (items.Count <= pageSize)
            return new CursorPage<TaskDto>(items, null);

        var page = items.Take(pageSize).ToList();
        return new CursorPage<TaskDto>(page, new Cursor(page[^1].CreatedAt, page[^1].Id).Encode());
    }

    public async Task<TaskDto> GetAsync(Guid id, CancellationToken ct) =>
        await tasks.GetDtoAsync(id, ct) ?? throw new NotFoundException("Tarea no encontrada.");

    public async Task<TaskDto> UpdateAsync(Guid id, UpdateTaskRequest request, CancellationToken ct)
    {
        await validator.ValidateAsync(request, ct);
        var task = await RequireTaskAsync(id, ct);

        if (request.Title is not null) task.Rename(request.Title, Now);
        if (request.Description is not null) task.ChangeDescription(request.Description, Now);
        if (request.Priority is not null) task.ChangePriority(request.Priority.Value, Now);
        if (request.DueAt is not null) task.ChangeDueDate(request.DueAt, Now);
        if (request.ClearDueAt) task.ChangeDueDate(null, Now);

        await unitOfWork.SaveChangesAsync(ct);
        return TaskDto.From(task);
    }

    public async Task<TaskDto> MoveAsync(Guid id, MoveTaskRequest request, CancellationToken ct)
    {
        await validator.ValidateAsync(request, ct);
        var task = await RequireTaskAsync(id, ct);

        if (request.AfterTaskId == task.Id)
            throw Validation.Fail(nameof(MoveTaskRequest.AfterTaskId), "Una tarea no puede ir después de sí misma.");

        var neighbors = await GetNeighborsAsync(task, request, ct);
        if (BoardPosition.NeedsRebalance(neighbors.Previous, neighbors.Next))
        {
            // Tras muchos movimientos al mismo hueco se agotan los decimales: se renumera la columna
            // entera (un UPDATE) y se recalcula. Pasa muy rara vez.
            await tasks.RebalanceColumnAsync(task.ProjectId, request.Status, ct);
            neighbors = await GetNeighborsAsync(task, request, ct);
        }

        task.MoveTo(request.Status, BoardPosition.Between(neighbors.Previous, neighbors.Next), Now);
        await unitOfWork.SaveChangesAsync(ct);
        return TaskDto.From(task);
    }

    public async Task<TaskDto> AssignAsync(Guid id, AssignTaskRequest request, CancellationToken ct)
    {
        var task = await RequireTaskAsync(id, ct);
        var assignee = request.AssigneeId is Guid userId ? await RequireMemberAsync(userId, ct) : (Guid?)null;

        task.AssignTo(assignee, Now);
        await unitOfWork.SaveChangesAsync(ct);
        return TaskDto.From(task);
    }

    public async Task<TaskDto> SetLabelsAsync(Guid id, SetLabelsRequest request, CancellationToken ct)
    {
        await validator.ValidateAsync(request, ct);
        var task = await RequireTaskAsync(id, ct);

        task.SetLabels(await RequireLabelsAsync(request.LabelIds, ct), Now);
        await unitOfWork.SaveChangesAsync(ct);
        return TaskDto.From(task);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var task = await RequireTaskAsync(id, ct);
        tasks.Remove(task); // AppDbContext lo convierte en soft delete
        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<TaskItem> RequireTaskAsync(Guid id, CancellationToken ct) =>
        await tasks.GetByIdAsync(id, ct) ?? throw new NotFoundException("Tarea no encontrada.");

    private async Task<(decimal? Previous, decimal? Next)> GetNeighborsAsync(
        TaskItem task, MoveTaskRequest request, CancellationToken ct) =>
        await tasks.GetNeighborPositionsAsync(task.ProjectId, request.Status, request.AfterTaskId, task.Id, ct)
        ?? throw Validation.Fail(nameof(MoveTaskRequest.AfterTaskId), "La tarea de referencia no está en esa columna.");

    private async Task<Guid> RequireMemberAsync(Guid userId, CancellationToken ct)
    {
        if (!await workspaces.IsMemberAsync(tenant.RequireWorkspaceId(), userId, ct))
            throw Validation.Fail("AssigneeId", "El usuario no es miembro del workspace.");
        return userId;
    }

    private async Task<IReadOnlyList<Guid>> RequireLabelsAsync(IReadOnlyList<Guid> labelIds, CancellationToken ct)
    {
        var distinct = labelIds.Distinct().ToList();
        // El repo cuenta con el filtro de tenant: una etiqueta de otro workspace "no existe".
        if (await labels.CountExistingAsync(distinct, ct) != distinct.Count)
            throw Validation.Fail("LabelIds", "Alguna de las etiquetas no existe.");
        return distinct;
    }

}
