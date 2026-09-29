using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Authorization;
using TaskFlow.Application.Projects;
using TaskFlow.Application.Tasks;

namespace TaskFlow.Api.Controllers;

/// <summary>Proyectos del workspace activo (el del token). El filtro global de EF hace el resto.</summary>
[ApiController]
[Route("api/v1/projects")]
public sealed class ProjectsController(IProjectService projects, ITaskService tasks) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = WorkspacePolicies.Read)]
    public async Task<ActionResult<IReadOnlyList<ProjectDto>>> List([FromQuery] bool includeArchived, CancellationToken ct) =>
        Ok(await projects.ListAsync(includeArchived, ct));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = WorkspacePolicies.Read)]
    public async Task<ActionResult<ProjectDto>> Get(Guid id, CancellationToken ct) => Ok(await projects.GetAsync(id, ct));

    [HttpPost]
    [Authorize(Policy = WorkspacePolicies.Write)]
    public async Task<ActionResult<ProjectDto>> Create(CreateProjectRequest request, CancellationToken ct)
    {
        var created = await projects.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = WorkspacePolicies.Write)]
    public async Task<ActionResult<ProjectDto>> Update(Guid id, UpdateProjectRequest request, CancellationToken ct) =>
        Ok(await projects.UpdateAsync(id, request, ct));

    [HttpPost("{id:guid}/archive")]
    [Authorize(Policy = WorkspacePolicies.Admin)]
    public async Task<ActionResult<ProjectDto>> Archive(Guid id, CancellationToken ct) => Ok(await projects.ArchiveAsync(id, ct));

    [HttpPost("{id:guid}/unarchive")]
    [Authorize(Policy = WorkspacePolicies.Admin)]
    public async Task<ActionResult<ProjectDto>> Unarchive(Guid id, CancellationToken ct) => Ok(await projects.UnarchiveAsync(id, ct));

    /// <summary>Soft delete del proyecto y de todas sus tareas.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = WorkspacePolicies.Admin)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await projects.DeleteAsync(id, ct);
        return NoContent();
    }

    // ---- Tareas del proyecto (board) ----

    // Un proyecto de otro workspace no pasa el filtro global → 404, no 403: un 403 confirmaría que existe.
    [HttpGet("{id:guid}/tasks")]
    [Authorize(Policy = WorkspacePolicies.Read)]
    public async Task<ActionResult<IReadOnlyList<TaskDto>>> ListTasks(Guid id, CancellationToken ct) =>
        Ok(await tasks.ListByProjectAsync(id, ct));

    [HttpPost("{id:guid}/tasks")]
    [Authorize(Policy = WorkspacePolicies.Write)]
    public async Task<ActionResult<TaskDto>> CreateTask(Guid id, CreateTaskRequest request, CancellationToken ct)
    {
        var created = await tasks.CreateAsync(id, request, ct);
        return CreatedAtAction(nameof(TasksController.Get), "Tasks", new { id = created.Id }, created);
    }
}
