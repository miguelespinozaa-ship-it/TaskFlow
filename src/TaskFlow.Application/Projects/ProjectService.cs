using FluentValidation;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Projects;

public sealed record CreateProjectRequest(string Name, string KeyPrefix, string? Description);

public sealed class CreateProjectRequestValidator : AbstractValidator<CreateProjectRequest>
{
    public CreateProjectRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.KeyPrefix).NotEmpty()
            .Matches("^[A-Z]{1,10}$").WithMessage("Entre 1 y 10 letras mayúsculas.");
        RuleFor(x => x.Description).MaximumLength(2000);
    }
}

public interface IProjectService
{
    Task<IReadOnlyList<ProjectDto>> ListAsync(CancellationToken ct);
    Task<ProjectDto> CreateAsync(CreateProjectRequest request, CancellationToken ct);
}

public sealed class ProjectService(
    IProjectRepository projects,
    IUnitOfWork unitOfWork,
    ITenantContext tenant,
    IValidator<CreateProjectRequest> validator,
    TimeProvider clock) : IProjectService
{
    public Task<IReadOnlyList<ProjectDto>> ListAsync(CancellationToken ct) => projects.ListAsync(ct);

    public async Task<ProjectDto> CreateAsync(CreateProjectRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        // El workspace sale del token, nunca del body: el cliente no elige en qué tenant escribe.
        var project = Project.Create(
            tenant.RequireWorkspaceId(), request.Name, request.KeyPrefix, request.Description, clock.GetUtcNow().UtcDateTime);

        projects.Add(project);
        await unitOfWork.SaveChangesAsync(ct);

        return new ProjectDto(project.Id, project.WorkspaceId, project.Name, project.Description, project.KeyPrefix, project.CreatedAt);
    }
}
