using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Authorization;
using TaskFlow.Application.Common.Paging;
using TaskFlow.Application.Repositories;

namespace TaskFlow.Api.Controllers;

/// <summary>Repositorio de GitHub conectado a un proyecto, y los commits importados de él.</summary>
[ApiController]
[Route("api/v1/projects/{projectId:guid}")]
public sealed class RepositoryController(IRepositoryService repositories) : ControllerBase
{
    /// <summary>204 si el proyecto no tiene repositorio conectado (no es un error: es un estado válido).</summary>
    [HttpGet("repository")]
    [Authorize(Policy = WorkspacePolicies.Read)]
    public async Task<ActionResult<RepositoryDto>> Get(Guid projectId, CancellationToken ct) =>
        await repositories.GetAsync(projectId, ct) is { } repository ? Ok(repository) : NoContent();

    /// <summary>Conecta (o reemplaza) el repositorio e importa los commits más recientes.</summary>
    [HttpPut("repository")]
    [Authorize(Policy = WorkspacePolicies.Admin)]
    public async Task<ActionResult<SyncResult>> Link(Guid projectId, LinkRepositoryRequest request, CancellationToken ct) =>
        Ok(await repositories.LinkAsync(projectId, request, ct));

    [HttpDelete("repository")]
    [Authorize(Policy = WorkspacePolicies.Admin)]
    public async Task<IActionResult> Unlink(Guid projectId, CancellationToken ct)
    {
        await repositories.UnlinkAsync(projectId, ct);
        return NoContent();
    }

    [HttpPost("repository/sync")]
    [Authorize(Policy = WorkspacePolicies.Write)]
    public async Task<ActionResult<SyncResult>> Sync(Guid projectId, CancellationToken ct) =>
        Ok(await repositories.SyncAsync(projectId, ct));

    [HttpGet("commits")]
    [Authorize(Policy = WorkspacePolicies.Read)]
    public async Task<ActionResult<CursorPage<CommitDto>>> Commits(
        Guid projectId, [FromQuery] string? cursor, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await repositories.ListCommitsAsync(projectId, cursor, pageSize, ct));
}
