using Microsoft.AspNetCore.Authorization;
using TaskFlow.Domain.Workspaces;

namespace TaskFlow.Api.Authorization;

/// <summary>
/// [Authorize] responde "¿quién sos?". Estas policies responden "¿qué podés hacer en ESTE workspace?".
/// El claim "role" es el rol en el workspace del token, no un rol global.
/// </summary>
public static class WorkspacePolicies
{
    public const string Read = "workspace:read";
    public const string Write = "workspace:write";
    public const string Admin = "workspace:admin";
    public const string Owner = "workspace:owner";

    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(Read, p => p.RequireRole(Roles(WorkspaceRole.Owner, WorkspaceRole.Admin, WorkspaceRole.Member, WorkspaceRole.Viewer)));
        options.AddPolicy(Write, p => p.RequireRole(Roles(WorkspaceRole.Owner, WorkspaceRole.Admin, WorkspaceRole.Member)));
        options.AddPolicy(Admin, p => p.RequireRole(Roles(WorkspaceRole.Owner, WorkspaceRole.Admin)));
        options.AddPolicy(Owner, p => p.RequireRole(Roles(WorkspaceRole.Owner)));

        // Seguro por defecto: todo endpoint exige token salvo [AllowAnonymous] explícito.
        // Un endpoint nuevo que alguien olvide marcar queda cerrado, no abierto.
        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    }

    private static string[] Roles(params WorkspaceRole[] roles) => [.. roles.Select(r => r.ToString())];
}
