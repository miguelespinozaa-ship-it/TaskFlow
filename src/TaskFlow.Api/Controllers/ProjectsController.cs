using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Authorization;
using TaskFlow.Application.Projects;

namespace TaskFlow.Api.Controllers;

/// <summary>Proyectos del workspace activo (el del token). El filtro global de EF hace el resto.</summary>
[ApiController]
[Route("api/v1/projects")]
public sealed class ProjectsController(IProjectService projects) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = WorkspacePolicies.Read)]
    public async Task<ActionResult<IReadOnlyList<ProjectDto>>> List(CancellationToken ct) =>
        Ok(await projects.ListAsync(ct));

    [HttpPost]
    [Authorize(Policy = WorkspacePolicies.Write)]
    public async Task<ActionResult<ProjectDto>> Create(CreateProjectRequest request, CancellationToken ct)
    {
        var created = await projects.CreateAsync(request, ct);
        return Created($"/api/v1/projects/{created.Id}/tasks", created);
    }
}
