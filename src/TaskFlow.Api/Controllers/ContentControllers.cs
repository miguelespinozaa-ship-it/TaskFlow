using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Authorization;
using TaskFlow.Application.Activities;
using TaskFlow.Application.Comments;
using TaskFlow.Application.Common.Paging;
using TaskFlow.Application.Labels;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/v1/comments")]
public sealed class CommentsController(ICommentService comments) : ControllerBase
{
    /// <summary>Solo el autor (lo valida el dominio).</summary>
    [HttpPatch("{id:guid}")]
    [Authorize(Policy = WorkspacePolicies.Write)]
    public async Task<ActionResult<CommentDto>> Edit(Guid id, SaveCommentRequest request, CancellationToken ct) =>
        Ok(await comments.EditAsync(id, request, ct));

    /// <summary>El autor, o un Admin/Owner moderando.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = WorkspacePolicies.Write)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await comments.DeleteAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/labels")]
public sealed class LabelsController(ILabelService labels) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = WorkspacePolicies.Read)]
    public async Task<ActionResult<IReadOnlyList<LabelDto>>> List(CancellationToken ct) => Ok(await labels.ListAsync(ct));

    [HttpPost]
    [Authorize(Policy = WorkspacePolicies.Write)]
    public async Task<ActionResult<LabelDto>> Create(SaveLabelRequest request, CancellationToken ct)
    {
        var created = await labels.CreateAsync(request, ct);
        return Created("/api/v1/labels", created);
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = WorkspacePolicies.Admin)]
    public async Task<ActionResult<LabelDto>> Update(Guid id, SaveLabelRequest request, CancellationToken ct) =>
        Ok(await labels.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = WorkspacePolicies.Admin)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await labels.DeleteAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/activity")]
public sealed class ActivityController(IActivityService activity) : ControllerBase
{
    /// <summary>Historial del workspace activo, más reciente primero.</summary>
    [HttpGet]
    [Authorize(Policy = WorkspacePolicies.Read)]
    public async Task<ActionResult<CursorPage<ActivityDto>>> List(
        [FromQuery] string? cursor, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await activity.ListAsync(null, cursor, pageSize, ct));
}
