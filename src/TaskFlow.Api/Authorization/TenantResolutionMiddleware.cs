using TaskFlow.Infrastructure.Identity;
using TaskFlow.Infrastructure.Tenancy;

namespace TaskFlow.Api.Authorization;

/// <summary>
/// Resuelve el workspace del request a partir del claim "workspace_id" del JWT.
///
/// Solo del claim: NO de un header, subdominio ni parámetro de URL. El claim está firmado; si la fuente
/// fuera el subdominio, un usuario con token válido de la empresa A podría pedir empresa-b.taskflow.app
/// y leer datos de B.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, TenantContext tenant)
    {
        if (ctx.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(ctx.User.FindFirst(TaskFlowClaims.WorkspaceId)?.Value, out var workspaceId))
        {
            tenant.Set(workspaceId);
        }

        await next(ctx);
    }
}
