using FluentValidation;
using TaskFlow.Application.Workspaces;
using TaskFlow.Domain.Workspaces;

namespace TaskFlow.Application.Auth;

public sealed record RegisterRequest(string Email, string Password, string DisplayName);

public sealed record LoginRequest(string Email, string Password);

public sealed record SwitchWorkspaceRequest(Guid WorkspaceId);

public sealed record UserDto(Guid Id, string Email, string DisplayName);

public sealed record CurrentWorkspaceDto(Guid Id, string Name, string Slug, WorkspaceRole Role);

/// <summary>Resultado interno. El refresh token NO viaja en el body: el controller lo pone en una cookie httpOnly.</summary>
public sealed record AuthResult(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    UserDto User,
    CurrentWorkspaceDto Workspace);

/// <summary>Lo que ve el cliente.</summary>
public sealed record AuthResponse(string AccessToken, DateTime ExpiresAt, UserDto User, CurrentWorkspaceDto Workspace)
{
    public static AuthResponse From(AuthResult r) => new(r.AccessToken, r.AccessTokenExpiresAt, r.User, r.Workspace);
}

public sealed record MeResponse(UserDto User, Guid CurrentWorkspaceId, IReadOnlyList<WorkspaceSummaryDto> Workspaces);

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct);
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<AuthResult> RefreshAsync(string? refreshToken, CancellationToken ct);
    Task<AuthResult> SwitchWorkspaceAsync(Guid userId, Guid workspaceId, string? currentRefreshToken, CancellationToken ct);
    Task LogoutAsync(string? refreshToken, CancellationToken ct);
    Task<MeResponse> GetMeAsync(Guid userId, Guid currentWorkspaceId, CancellationToken ct);
}

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        // La fortaleza de la contraseña la valida Identity (PasswordOptions): una sola fuente de verdad.
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(80);
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}
