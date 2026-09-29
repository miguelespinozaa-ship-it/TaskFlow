using TaskFlow.Application.Workspaces;
using TaskFlow.Domain.Workspaces;

namespace TaskFlow.Application.Abstractions;

public interface IWorkspaceRepository
{
    void Add(Workspace workspace);
    void AddMember(WorkspaceMember member);
    Task<bool> SlugExistsAsync(string slug, CancellationToken ct);
    Task<bool> IsMemberAsync(Guid workspaceId, Guid userId, CancellationToken ct);

    /// <summary>Workspaces del usuario, cruzando tenants (no aplica el filtro del workspace activo).</summary>
    Task<IReadOnlyList<WorkspaceSummaryDto>> ListForUserAsync(Guid userId, CancellationToken ct);

    Task<IReadOnlyList<MemberDto>> ListMembersAsync(Guid workspaceId, CancellationToken ct);
}

public interface IUserDirectory
{
    Task<Guid?> FindUserIdByEmailAsync(string email, CancellationToken ct);
}
