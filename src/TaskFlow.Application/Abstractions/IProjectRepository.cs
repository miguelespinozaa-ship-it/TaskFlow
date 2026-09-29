using TaskFlow.Application.Projects;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Abstractions;

public interface IProjectRepository
{
    void Add(Project project);
    void Remove(Project project);
    Task<Project?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct);
    Task<ProjectDto?> GetDtoAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ProjectDto>> ListAsync(bool includeArchived, CancellationToken ct);
}
