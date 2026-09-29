using TaskFlow.Application.Abstractions;

namespace TaskFlow.Application.Projects;

public interface IProjectService
{
    Task<IReadOnlyList<ProjectDto>> ListAsync(CancellationToken ct);
}

public sealed class ProjectService(IProjectRepository projects) : IProjectService
{
    public Task<IReadOnlyList<ProjectDto>> ListAsync(CancellationToken ct) => projects.ListAsync(ct);
}
