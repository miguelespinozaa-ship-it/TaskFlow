using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Authorization;
using TaskFlow.Application.Workspaces;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/v1/workspaces")]
public sealed class WorkspacesController(IWorkspaceService workspaces) : ControllerBase
{
    /// <summary>Workspaces a los que pertenece el usuario, con su rol en cada uno.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WorkspaceSummaryDto>>> ListMine(CancellationToken ct) =>
        Ok(await workspaces.ListMineAsync(ct));

    /// <summary>Crea un workspace; quien lo crea queda como Owner. Para usarlo: POST /auth/switch-workspace.</summary>
    [HttpPost]
    public async Task<ActionResult<WorkspaceSummaryDto>> Create(CreateWorkspaceRequest request, CancellationToken ct)
    {
        var created = await workspaces.CreateAsync(request, ct);
        return Created($"/api/v1/workspaces/{created.Id}", created);
    }

    /// <summary>Plan del workspace activo y cuánto lleva usado de cada límite.</summary>
    [HttpGet("current")]
    [Authorize(Policy = WorkspacePolicies.Read)]
    public async Task<ActionResult<WorkspaceUsageDto>> GetUsage(CancellationToken ct) => Ok(await workspaces.GetUsageAsync(ct));

    /// <summary>Cambia el plan. No hay cobro: el endpoint representa lo que haría el webhook de un proveedor de pagos.</summary>
    [HttpPut("current/plan")]
    [Authorize(Policy = WorkspacePolicies.Owner)]
    public async Task<ActionResult<WorkspaceUsageDto>> ChangePlan(ChangePlanRequest request, CancellationToken ct) =>
        Ok(await workspaces.ChangePlanAsync(request, ct));

    // "current" = el workspace del token. El id no viaja en la URL: no hay forma de apuntar a otro tenant.
    [HttpGet("current/members")]
    [Authorize(Policy = WorkspacePolicies.Read)]
    public async Task<ActionResult<IReadOnlyList<MemberDto>>> ListMembers(CancellationToken ct) =>
        Ok(await workspaces.ListMembersAsync(ct));

    [HttpPost("current/members")]
    [Authorize(Policy = WorkspacePolicies.Admin)]
    public async Task<ActionResult<MemberDto>> AddMember(AddMemberRequest request, CancellationToken ct)
    {
        var member = await workspaces.AddMemberAsync(request, ct);
        return Created("/api/v1/workspaces/current/members", member);
    }
}
