using FluentValidation;
using TaskFlow.Domain.Workspaces;

namespace TaskFlow.Application.Workspaces;

public sealed record WorkspaceSummaryDto(Guid Id, string Name, string Slug, WorkspaceRole Role);

public sealed record MemberDto(Guid UserId, string Email, string DisplayName, WorkspaceRole Role, DateTime JoinedAt);

/// <summary>Plan del workspace y cuánto lleva usado de cada límite (límite null = sin tope).</summary>
public sealed record WorkspaceUsageDto(WorkspacePlan Plan, int Members, int? MaxMembers, int Projects, int? MaxProjects);

public sealed record ChangePlanRequest(WorkspacePlan Plan);

public sealed record CreateWorkspaceRequest(string Name, string Slug);

public sealed record AddMemberRequest(string Email, WorkspaceRole Role);

public sealed class CreateWorkspaceRequestValidator : AbstractValidator<CreateWorkspaceRequest>
{
    public CreateWorkspaceRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(60)
            .Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$").WithMessage("Solo minúsculas, números y guiones.");
    }
}

public sealed class AddMemberRequestValidator : AbstractValidator<AddMemberRequest>
{
    public AddMemberRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Role).IsInEnum()
            .NotEqual(WorkspaceRole.Owner).WithMessage("El rol Owner no se asigna: se transfiere.");
    }
}
