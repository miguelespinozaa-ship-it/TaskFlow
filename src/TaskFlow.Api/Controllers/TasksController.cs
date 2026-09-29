using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Authorization;
using TaskFlow.Application.Activities;
using TaskFlow.Application.Comments;
using TaskFlow.Application.Common.Paging;
using TaskFlow.Application.Tasks;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tasks")]
public sealed class TasksController(ITaskService tasks, ICommentService comments, IActivityService activity) : ControllerBase
{
    /// <summary>Búsqueda en todo el workspace: filtros + full-text + paginación por cursor.</summary>
    [HttpGet]
    [Authorize(Policy = WorkspacePolicies.Read)]
    public async Task<ActionResult<CursorPage<TaskDto>>> Search([FromQuery] TaskQuery query, CancellationToken ct) =>
        Ok(await tasks.SearchAsync(query, ct));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = WorkspacePolicies.Read)]
    public async Task<ActionResult<TaskDto>> Get(Guid id, CancellationToken ct) => Ok(await tasks.GetAsync(id, ct));

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = WorkspacePolicies.Write)]
    public async Task<ActionResult<TaskDto>> Update(Guid id, UpdateTaskRequest request, CancellationToken ct) =>
        Ok(await tasks.UpdateAsync(id, request, ct));

    [HttpPost("{id:guid}/move")]
    [Authorize(Policy = WorkspacePolicies.Write)]
    public async Task<ActionResult<TaskDto>> Move(Guid id, MoveTaskRequest request, CancellationToken ct) =>
        Ok(await tasks.MoveAsync(id, request, ct));

    [HttpPost("{id:guid}/assign")]
    [Authorize(Policy = WorkspacePolicies.Write)]
    public async Task<ActionResult<TaskDto>> Assign(Guid id, AssignTaskRequest request, CancellationToken ct) =>
        Ok(await tasks.AssignAsync(id, request, ct));

    [HttpPut("{id:guid}/labels")]
    [Authorize(Policy = WorkspacePolicies.Write)]
    public async Task<ActionResult<TaskDto>> SetLabels(Guid id, SetLabelsRequest request, CancellationToken ct) =>
        Ok(await tasks.SetLabelsAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = WorkspacePolicies.Write)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await tasks.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/comments")]
    [Authorize(Policy = WorkspacePolicies.Read)]
    public async Task<ActionResult<IReadOnlyList<CommentDto>>> ListComments(Guid id, CancellationToken ct) =>
        Ok(await comments.ListAsync(id, ct));

    [HttpPost("{id:guid}/comments")]
    [Authorize(Policy = WorkspacePolicies.Write)]
    public async Task<ActionResult<CommentDto>> AddComment(Guid id, SaveCommentRequest request, CancellationToken ct)
    {
        var created = await comments.CreateAsync(id, request, ct);
        return Created($"/api/v1/tasks/{id}/comments", created);
    }

    [HttpGet("{id:guid}/activity")]
    [Authorize(Policy = WorkspacePolicies.Read)]
    public async Task<ActionResult<CursorPage<ActivityDto>>> Activity(
        Guid id, [FromQuery] string? cursor, [FromQuery] int? pageSize, CancellationToken ct)
    {
        _ = await tasks.GetAsync(id, ct); // 404 si la tarea no es de este workspace
        return Ok(await activity.ListAsync(id, cursor, pageSize, ct));
    }
}
