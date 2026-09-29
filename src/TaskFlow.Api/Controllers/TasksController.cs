using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.Tasks;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/v1/projects/{projectId:guid}/tasks")]
public sealed class TasksController(ITaskService tasks) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TaskDto>>> List(Guid projectId, CancellationToken ct) =>
        Ok(await tasks.ListByProjectAsync(projectId, ct));

    [HttpPost]
    public async Task<ActionResult<TaskDto>> Create(Guid projectId, CreateTaskRequest request, CancellationToken ct)
    {
        var created = await tasks.CreateAsync(projectId, request, ct);
        // Fase 1 no tiene GET /tasks/{id} todavía: Location apunta al listado del proyecto.
        return Created($"/api/v1/projects/{projectId}/tasks", created);
    }
}
