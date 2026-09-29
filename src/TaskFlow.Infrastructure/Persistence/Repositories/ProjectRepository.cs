using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Projects;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Infrastructure.Persistence.Repositories;

internal sealed class ProjectRepository(AppDbContext db) : IProjectRepository
{
    public void Add(Project project) => db.Projects.Add(project);

    public Task<Project?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct) =>
        db.Projects.AnyAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<ProjectDto>> ListAsync(CancellationToken ct) =>
        await db.Projects
            .AsNoTracking()
            .Where(p => !p.IsArchived)
            .OrderBy(p => p.Name)
            .Select(p => new ProjectDto(p.Id, p.WorkspaceId, p.Name, p.Description, p.KeyPrefix, p.CreatedAt))
            .ToListAsync(ct);
}
