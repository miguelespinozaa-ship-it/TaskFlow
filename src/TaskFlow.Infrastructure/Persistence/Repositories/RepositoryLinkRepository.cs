using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common.Paging;
using TaskFlow.Application.Repositories;
using TaskFlow.Domain.Repositories;

namespace TaskFlow.Infrastructure.Persistence.Repositories;

internal sealed class RepositoryLinkRepository(AppDbContext db) : IRepositoryLinkRepository
{
    public void Add(RepositoryLink link) => db.RepositoryLinks.Add(link);

    public void Remove(RepositoryLink link) => db.RepositoryLinks.Remove(link);

    public void AddCommit(RepositoryCommit commit) => db.RepositoryCommits.Add(commit);

    public Task<RepositoryLink?> GetByProjectAsync(Guid projectId, CancellationToken ct) =>
        db.RepositoryLinks.FirstOrDefaultAsync(l => l.ProjectId == projectId, ct);

    public async Task<HashSet<string>> ExistingShasAsync(Guid projectId, IReadOnlyCollection<string> shas, CancellationToken ct) =>
        [.. await db.RepositoryCommits
            .Where(c => c.ProjectId == projectId && shas.Contains(c.Sha))
            .Select(c => c.Sha)
            .ToListAsync(ct)];

    public Task<int> DeleteCommitsAsync(Guid projectId, CancellationToken ct) =>
        db.RepositoryCommits.Where(c => c.ProjectId == projectId).ExecuteDeleteAsync(ct);

    public async Task<IReadOnlyList<CommitDto>> ListCommitsAsync(Guid projectId, Cursor? after, int take, CancellationToken ct)
    {
        var commits = db.RepositoryCommits.AsNoTracking().Where(c => c.ProjectId == projectId);
        if (after is Cursor cursor)
            commits = commits.Where(c => EF.Functions.LessThan(
                ValueTuple.Create(c.CommittedAt, c.Id), ValueTuple.Create(cursor.CreatedAt, cursor.Id)));

        var rows = await commits
            .OrderByDescending(c => c.CommittedAt).ThenByDescending(c => c.Id)
            .Take(take)
            .Join(db.RepositoryLinks, c => c.ProjectId, l => l.ProjectId, (c, l) => new { Commit = c, l.Owner, l.Name })
            .ToListAsync(ct);

        return [.. rows
            .OrderByDescending(r => r.Commit.CommittedAt).ThenByDescending(r => r.Commit.Id)
            .Select(r => ToDto(r.Commit, r.Owner, r.Name))];
    }

    public async Task<IReadOnlyList<(Guid ProjectId, Guid WorkspaceId)>> ListAllForSyncAsync(CancellationToken ct) =>
        [.. (await db.RepositoryLinks
                // Deliberado: el sincronizador de fondo atiende a todos los tenants.
                .IgnoreQueryFilters([QueryFilters.Tenant])
                .AsNoTracking()
                // Proyectos borrados (soft delete) quedan fuera: el filtro SoftDelete sigue activo en la subconsulta.
                .Where(l => db.Projects.Any(p => p.Id == l.ProjectId))
                .OrderBy(l => l.LastSyncedAt)
                .Select(l => new { l.ProjectId, l.WorkspaceId })
                .ToListAsync(ct))
            .Select(l => (l.ProjectId, l.WorkspaceId))];

    private static CommitDto ToDto(RepositoryCommit c, string owner, string name)
    {
        // La primera línea del mensaje es el título; el resto, la explicación larga (si la hay).
        var newline = c.Message.IndexOf('\n');
        var title = (newline < 0 ? c.Message : c.Message[..newline]).Trim();
        var body = newline < 0 ? null : c.Message[(newline + 1)..].Trim();

        return new CommitDto(
            c.Id, c.Sha, title, string.IsNullOrEmpty(body) ? null : body, c.AuthorName, c.AuthorLogin, c.AuthorAvatarUrl,
            c.CommittedAt,
            // La URL la armamos nosotros con datos ya validados; no se confía en la que devuelve la API.
            $"https://github.com/{owner}/{name}/commit/{c.Sha}",
            c.Additions, c.Deletions, c.FilesChanged, c.Folders);
    }
}
