using FluentValidation;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common.Exceptions;
using TaskFlow.Domain.Workspaces;

namespace TaskFlow.Application.Workspaces;

public interface IWorkspaceService
{
    Task<WorkspaceSummaryDto> CreateAsync(CreateWorkspaceRequest request, CancellationToken ct);
    Task<IReadOnlyList<WorkspaceSummaryDto>> ListMineAsync(CancellationToken ct);
    Task<IReadOnlyList<MemberDto>> ListMembersAsync(CancellationToken ct);
    Task<MemberDto> AddMemberAsync(AddMemberRequest request, CancellationToken ct);
    Task<WorkspaceUsageDto> GetUsageAsync(CancellationToken ct);
    Task<WorkspaceUsageDto> ChangePlanAsync(ChangePlanRequest request, CancellationToken ct);
}

public sealed class WorkspaceService(
    IWorkspaceRepository workspaces,
    IProjectRepository projects,
    IUserDirectory userDirectory,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenant,
    IValidator<CreateWorkspaceRequest> createValidator,
    IValidator<AddMemberRequest> addMemberValidator,
    TimeProvider clock) : IWorkspaceService
{
    public async Task<WorkspaceSummaryDto> CreateAsync(CreateWorkspaceRequest request, CancellationToken ct)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        var userId = currentUser.RequireUserId();

        if (await workspaces.SlugExistsAsync(request.Slug, ct))
            throw new ConflictException("Ese slug ya está en uso.");

        var now = clock.GetUtcNow().UtcDateTime;
        var workspace = Workspace.Create(request.Name, request.Slug, now);
        workspaces.Add(workspace);
        workspaces.AddMember(WorkspaceMember.Create(workspace.Id, userId, WorkspaceRole.Owner, now));
        await unitOfWork.SaveChangesAsync(ct);

        return new WorkspaceSummaryDto(workspace.Id, workspace.Name, workspace.Slug, WorkspaceRole.Owner);
    }

    public Task<IReadOnlyList<WorkspaceSummaryDto>> ListMineAsync(CancellationToken ct) =>
        workspaces.ListForUserAsync(currentUser.RequireUserId(), ct);

    public Task<IReadOnlyList<MemberDto>> ListMembersAsync(CancellationToken ct) =>
        workspaces.ListMembersAsync(tenant.RequireWorkspaceId(), ct);

    public async Task<MemberDto> AddMemberAsync(AddMemberRequest request, CancellationToken ct)
    {
        await addMemberValidator.ValidateAndThrowAsync(request, ct);
        var workspaceId = tenant.RequireWorkspaceId();

        var userId = await userDirectory.FindUserIdByEmailAsync(request.Email, ct)
            ?? throw new NotFoundException("No hay ningún usuario registrado con ese email.");

        if (await workspaces.IsMemberAsync(workspaceId, userId, ct))
            throw new ConflictException("Ese usuario ya es miembro del workspace.");

        var workspace = await RequireCurrentAsync(ct);
        if (workspace.Plan.MaxMembers() is int max && await workspaces.CountMembersAsync(workspaceId, ct) >= max)
            throw new PlanLimitExceededException($"El plan {workspace.Plan} permite hasta {max} miembros. Cambia de plan para sumar más.");

        workspaces.AddMember(WorkspaceMember.Create(workspaceId, userId, request.Role, clock.GetUtcNow().UtcDateTime));
        await unitOfWork.SaveChangesAsync(ct);

        var members = await workspaces.ListMembersAsync(workspaceId, ct);
        return members.Single(m => m.UserId == userId);
    }

    public async Task<WorkspaceUsageDto> GetUsageAsync(CancellationToken ct) => await UsageAsync(await RequireCurrentAsync(ct), ct);

    public async Task<WorkspaceUsageDto> ChangePlanAsync(ChangePlanRequest request, CancellationToken ct)
    {
        var workspace = await RequireCurrentAsync(ct);
        workspace.ChangePlan(request.Plan);
        await unitOfWork.SaveChangesAsync(ct);
        return await UsageAsync(workspace, ct);
    }

    private async Task<WorkspaceUsageDto> UsageAsync(Workspace workspace, CancellationToken ct) => new(
        workspace.Plan,
        await workspaces.CountMembersAsync(workspace.Id, ct), workspace.Plan.MaxMembers(),
        await projects.CountAsync(ct), workspace.Plan.MaxProjects());

    private async Task<Workspace> RequireCurrentAsync(CancellationToken ct) =>
        await workspaces.GetAsync(tenant.RequireWorkspaceId(), ct) ?? throw new NotFoundException("Workspace no encontrado.");
}
