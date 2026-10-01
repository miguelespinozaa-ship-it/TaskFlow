using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Projects;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Infrastructure.Persistence.Repositories;

internal sealed class ProjectRepository(AppDbContext db) : IProjectRepository
{
    private static readonly Expression<Func<Project, ProjectDto>> ToDto = p =>
        new ProjectDto(p.Id, p.WorkspaceId, p.Name, p.Description, p.KeyPrefix, p.IsArchived, p.CreatedAt);

    public void Add(Project project) => db.Projects.Add(project);

    public void Remove(Project project) => db.Projects.Remove(project);

    public Task<Project?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct) =>
        db.Projects.AnyAsync(p => p.Id == id, ct);

    public Task<bool> KeyPrefixExistsAsync(string keyPrefix, CancellationToken ct) =>
        db.Projects.AnyAsync(p => p.KeyPrefix == keyPrefix, ct);

    public Task<ProjectDto?> GetDtoAsync(Guid id, CancellationToken ct) =>
        db.Projects.AsNoTracking().Where(p => p.Id == id).Select(ToDto).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ProjectDto>> ListAsync(bool includeArchived, CancellationToken ct) =>
        await db.Projects
            .AsNoTracking()
            .Where(p => includeArchived || !p.IsArchived)
            .OrderBy(p => p.Name)
            .Select(ToDto)
            .ToListAsync(ct);
}
