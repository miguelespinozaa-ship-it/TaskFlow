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
}

public sealed class WorkspaceService(
    IWorkspaceRepository workspaces,
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

        workspaces.AddMember(WorkspaceMember.Create(workspaceId, userId, request.Role, clock.GetUtcNow().UtcDateTime));
        await unitOfWork.SaveChangesAsync(ct);

        var members = await workspaces.ListMembersAsync(workspaceId, ct);
        return members.Single(m => m.UserId == userId);
    }
}
