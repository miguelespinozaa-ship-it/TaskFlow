using TaskFlow.Application.Abstractions;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Api.Authorization;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirst(TaskFlowClaims.Subject)?.Value, out var id) ? id : null;
}
