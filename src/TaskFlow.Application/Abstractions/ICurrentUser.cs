using TaskFlow.Domain.Workspaces;

namespace TaskFlow.Application.Abstractions;

public interface ICurrentUser
{
    Guid? UserId { get; }

    /// <summary>Rol en el workspace activo (el del token).</summary>
    WorkspaceRole? Role { get; }
}

public static class CurrentUserExtensions
{
    public static Guid RequireUserId(this ICurrentUser user) =>
        user.UserId ?? throw new Common.Exceptions.UnauthorizedException("No autenticado.");

    public static bool IsAdmin(this ICurrentUser user) => user.Role is WorkspaceRole.Admin or WorkspaceRole.Owner;
}
