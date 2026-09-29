using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Workspaces;
using TaskFlow.Domain.Workspaces;

namespace TaskFlow.Infrastructure.Persistence.Repositories;

internal sealed class WorkspaceRepository(AppDbContext db) : IWorkspaceRepository
{
    public void Add(Workspace workspace) => db.Workspaces.Add(workspace);

    public void AddMember(WorkspaceMember member) => db.WorkspaceMembers.Add(member);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken ct) =>
        db.Workspaces.AnyAsync(w => w.Slug == slug, ct);

    public Task<bool> IsMemberAsync(Guid workspaceId, Guid userId, CancellationToken ct) =>
        db.WorkspaceMembers.IgnoreQueryFilters().AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, ct);

    // IgnoreQueryFilters es deliberado y acotado: el WHERE por user_id reemplaza al filtro de tenant.
    public async Task<IReadOnlyList<WorkspaceSummaryDto>> ListForUserAsync(Guid userId, CancellationToken ct) =>
        await db.WorkspaceMembers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Join(db.Workspaces, m => m.WorkspaceId, w => w.Id, (m, w) => new { m, w })
            .OrderBy(x => x.m.JoinedAt)
            .Select(x => new WorkspaceSummaryDto(x.w.Id, x.w.Name, x.w.Slug, x.m.Role))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<MemberDto>> ListMembersAsync(Guid workspaceId, CancellationToken ct) =>
        await db.WorkspaceMembers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m, u })
            .OrderBy(x => x.m.JoinedAt)
            .Select(x => new MemberDto(x.u.Id, x.u.Email!, x.u.DisplayName, x.m.Role, x.m.JoinedAt))
            .ToListAsync(ct);
}

internal sealed class UserDirectory(AppDbContext db, ILookupNormalizer normalizer) : IUserDirectory
{
    public async Task<Guid?> FindUserIdByEmailAsync(string email, CancellationToken ct)
    {
        // Identity guarda el email normalizado para búsquedas case-insensitive con índice.
        var normalized = normalizer.NormalizeEmail(email.Trim());
        return await db.Users
            .Where(u => u.NormalizedEmail == normalized)
            .Select(u => (Guid?)u.Id)
            .SingleOrDefaultAsync(ct);
    }
}
