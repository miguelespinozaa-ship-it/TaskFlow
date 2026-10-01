using FluentValidation;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Application.Common.Exceptions;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Workspaces;

namespace TaskFlow.Application.Projects;

public sealed record CreateProjectRequest(string Name, string KeyPrefix, string? Description);

/// <summary>PATCH: null = no cambiar; Description "" la vacía.</summary>
public sealed record UpdateProjectRequest(string? Name = null, string? Description = null);

public sealed class CreateProjectRequestValidator : AbstractValidator<CreateProjectRequest>
{
    public CreateProjectRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Project.NameMaxLength);
        RuleFor(x => x.KeyPrefix).NotEmpty()
            .Matches("^[A-Z]{1,10}$").WithMessage("Entre 1 y 10 letras mayúsculas.");
        RuleFor(x => x.Description).MaximumLength(Project.DescriptionMaxLength);
    }
}

public sealed class UpdateProjectRequestValidator : AbstractValidator<UpdateProjectRequest>
{
    public UpdateProjectRequestValidator()
    {
        RuleFor(x => x.Name!).NotEmpty().MaximumLength(Project.NameMaxLength).When(x => x.Name is not null);
        RuleFor(x => x.Description).MaximumLength(Project.DescriptionMaxLength);
    }
}

public interface IProjectService
{
    Task<IReadOnlyList<ProjectDto>> ListAsync(bool includeArchived, CancellationToken ct);
    Task<ProjectDto> GetAsync(Guid id, CancellationToken ct);
    Task<ProjectDto> CreateAsync(CreateProjectRequest request, CancellationToken ct);
    Task<ProjectDto> UpdateAsync(Guid id, UpdateProjectRequest request, CancellationToken ct);
    Task<ProjectDto> ArchiveAsync(Guid id, CancellationToken ct);
    Task<ProjectDto> UnarchiveAsync(Guid id, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}

public sealed class ProjectService(
    IProjectRepository projects,
    IWorkspaceRepository workspaces,
    ITaskRepository tasks,
    IUnitOfWork unitOfWork,
    ITenantContext tenant,
    IRequestValidator validator,
    TimeProvider clock) : IProjectService
{
    public Task<IReadOnlyList<ProjectDto>> ListAsync(bool includeArchived, CancellationToken ct) =>
        projects.ListAsync(includeArchived, ct);

    public async Task<ProjectDto> GetAsync(Guid id, CancellationToken ct) =>
        await projects.GetDtoAsync(id, ct) ?? throw new NotFoundException("Proyecto no encontrado.");

    public async Task<ProjectDto> CreateAsync(CreateProjectRequest request, CancellationToken ct)
    {
        await validator.ValidateAsync(request, ct);

        // ponytail: comprobar-y-después-insertar; dos altas simultáneas en el último cupo pueden pasarse por uno.
        // Si el límite pasara a ser de facturación estricta: lock por workspace (pg_advisory_xact_lock).
        var workspace = await workspaces.GetAsync(tenant.RequireWorkspaceId(), ct)
            ?? throw new NotFoundException("Workspace no encontrado.");
        if (workspace.Plan.MaxProjects() is int max && await projects.CountAsync(ct) >= max)
            throw new PlanLimitExceededException($"El plan {workspace.Plan} permite hasta {max} proyectos. Cambia de plan para crear más.");

        // El prefijo identifica al proyecto en tarjetas y búsquedas: dos proyectos con el mismo se confunden.
        // El repo consulta con el filtro de tenant, así que otro workspace sí puede usar el mismo.
        if (await projects.KeyPrefixExistsAsync(request.KeyPrefix, ct))
            throw new ConflictException($"Ya hay un proyecto con el prefijo {request.KeyPrefix} en este workspace.");

        // El workspace sale del token, nunca del body: el cliente no elige en qué tenant escribe.
        var project = Project.Create(
            tenant.RequireWorkspaceId(), request.Name, request.KeyPrefix, request.Description, clock.GetUtcNow().UtcDateTime);

        projects.Add(project);
        await unitOfWork.SaveChangesAsync(ct);
        return ProjectDto.From(project);
    }

    public async Task<ProjectDto> UpdateAsync(Guid id, UpdateProjectRequest request, CancellationToken ct)
    {
        await validator.ValidateAsync(request, ct);
        var project = await RequireAsync(id, ct);

        if (request.Name is not null) project.Rename(request.Name);
        if (request.Description is not null) project.ChangeDescription(request.Description);

        await unitOfWork.SaveChangesAsync(ct);
        return ProjectDto.From(project);
    }

    public async Task<ProjectDto> ArchiveAsync(Guid id, CancellationToken ct)
    {
        var project = await RequireAsync(id, ct);
        project.Archive();
        await unitOfWork.SaveChangesAsync(ct);
        return ProjectDto.From(project);
    }

    public async Task<ProjectDto> UnarchiveAsync(Guid id, CancellationToken ct)
    {
        var project = await RequireAsync(id, ct);
        project.Unarchive();
        await unitOfWork.SaveChangesAsync(ct);
        return ProjectDto.From(project);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var project = await RequireAsync(id, ct);

        // Las tareas se borran (soft) con un único UPDATE en vez de cargarlas todas en memoria.
        await tasks.SoftDeleteByProjectAsync(project.Id, clock.GetUtcNow().UtcDateTime, ct);
        projects.Remove(project);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<Project> RequireAsync(Guid id, CancellationToken ct) =>
        await projects.GetByIdAsync(id, ct) ?? throw new NotFoundException("Proyecto no encontrado.");
}
