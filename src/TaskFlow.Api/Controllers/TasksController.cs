using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Authorization;
using TaskFlow.Application.Tasks;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/v1/projects/{projectId:guid}/tasks")]
public sealed class TasksController(ITaskService tasks) : ControllerBase
{
    // Un proyecto de otro workspace no pasa el filtro global → el servicio devuelve 404, no 403:
    // un 403 confirmaría que el recurso existe.
    [HttpGet]
    [Authorize(Policy = WorkspacePolicies.Read)]
    public async Task<ActionResult<IReadOnlyList<TaskDto>>> List(Guid projectId, CancellationToken ct) =>
        Ok(await tasks.ListByProjectAsync(projectId, ct));

    [HttpPost]
    [Authorize(Policy = WorkspacePolicies.Write)]
    public async Task<ActionResult<TaskDto>> Create(Guid projectId, CreateTaskRequest request, CancellationToken ct)
    {
        var created = await tasks.CreateAsync(projectId, request, ct);
        // Todavía no hay GET /tasks/{id} (fase 2b): Location apunta al listado del proyecto.
        return Created($"/api/v1/projects/{projectId}/tasks", created);
    }
}
