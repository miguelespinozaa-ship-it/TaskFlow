using TaskFlow.Application.Projects;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Abstractions;

public interface IProjectRepository
{
    Task<Project?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ProjectDto>> ListAsync(CancellationToken ct);
}
