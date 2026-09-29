using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Workspaces;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Api.Authorization;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId =>
        Guid.TryParse(Claim(TaskFlowClaims.Subject), out var id) ? id : null;

    public WorkspaceRole? Role =>
        Enum.TryParse<WorkspaceRole>(Claim(TaskFlowClaims.Role), out var role) ? role : null;

    private string? Claim(string type) => accessor.HttpContext?.User.FindFirst(type)?.Value;
}
