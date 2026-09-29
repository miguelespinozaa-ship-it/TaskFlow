using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskFlow.Application.Auth;
using TaskFlow.Application.Common.Exceptions;
using TaskFlow.Domain.Workspaces;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.Identity;

internal sealed class AuthService(
    AppDbContext db,
    UserManager<ApplicationUser> users,
    TokenService tokens,
    IValidator<RegisterRequest> registerValidator,
    IValidator<LoginRequest> loginValidator,
    TimeProvider clock,
    ILogger<AuthService> logger) : IAuthService
{
    // Mismo mensaje para "no existe" y "contraseña incorrecta": no revelamos qué emails están registrados.
    private const string InvalidCredentials = "Email o contraseña incorrectos.";
    private const string InvalidSession = "Sesión inválida o expirada.";

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        await registerValidator.ValidateAndThrowAsync(request, ct);
        var now = clock.GetUtcNow().UtcDateTime;

        // Usuario + workspace personal + membresía + refresh token: todo o nada.
        // Sin la transacción, un fallo al crear el workspace deja un "usuario colgado" que no puede usar la app.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var user = new ApplicationUser(request.Email.Trim(), request.DisplayName, now);
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            throw ToException(result);

        var workspace = Workspace.Create(
            $"Workspace de {user.DisplayName}",
            $"{WorkspaceSlug.FromText(request.Email.Split('@')[0])}-{Guid.NewGuid().ToString("N")[..6]}",
            now);
        db.Workspaces.Add(workspace);
        db.WorkspaceMembers.Add(WorkspaceMember.Create(workspace.Id, user.Id, WorkspaceRole.Owner, now));

        var auth = IssueSession(user, workspace, WorkspaceRole.Owner, familyId: Guid.CreateVersion7());
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        logger.LogInformation("Usuario {UserId} registrado con workspace {WorkspaceId}", user.Id, workspace.Id);
        return auth;
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        await loginValidator.ValidateAndThrowAsync(request, ct);

        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
            throw new UnauthorizedException(InvalidCredentials);

        if (await users.IsLockedOutAsync(user))
        {
            logger.LogWarning("Login bloqueado por lockout para {UserId}", user.Id);
            throw new UnauthorizedException(InvalidCredentials);
        }

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user); // cuenta intentos → lockout tras N fallos
            throw new UnauthorizedException(InvalidCredentials);
        }

        await users.ResetAccessFailedCountAsync(user);

        // Entra al workspace más antiguo (el personal). Para otro: POST /auth/switch-workspace.
        var membership = await db.WorkspaceMembers
            .IgnoreQueryFilters()
            .Where(m => m.UserId == user.Id)
            .OrderBy(m => m.JoinedAt)
            .Join(db.Workspaces, m => m.WorkspaceId, w => w.Id, (m, w) => new { w, m.Role })
            .FirstOrDefaultAsync(ct)
            ?? throw new UnauthorizedException("El usuario no pertenece a ningún workspace.");

        var auth = IssueSession(user, membership.w, membership.Role, familyId: Guid.CreateVersion7());
        await db.SaveChangesAsync(ct);
        return auth;
    }

    public async Task<AuthResult> RefreshAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refreshToken))
            throw new UnauthorizedException(InvalidSession);

        var hash = TokenService.Hash(refreshToken);
        var stored = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash, ct)
            ?? throw new UnauthorizedException(InvalidSession);

        if (stored.RevokedAt is not null)
        {
            // Alguien presentó un token que ya se usó: o lo robaron, o es un replay.
            // No sabemos cuál de las dos copias es la legítima → matamos la familia entera.
            await RevokeFamilyAsync(stored.FamilyId, ct);
            logger.LogWarning("Reuso de refresh token detectado. Familia {FamilyId} de {UserId} revocada",
                stored.FamilyId, stored.UserId);
            throw new UnauthorizedException(InvalidSession);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        if (stored.ExpiresAt <= now)
            throw new UnauthorizedException(InvalidSession);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Revocación atómica: el "AND revoked_at IS NULL" hace que, si llegan dos refresh con el mismo
        // token a la vez, solo UNO actualice la fila. El otro ve 0 filas y se trata como reuso.
        var newTokenId = Guid.CreateVersion7();
        var updated = await db.RefreshTokens
            .Where(t => t.Id == stored.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.ReplacedByTokenId, newTokenId), ct);

        if (updated == 0)
        {
            await tx.RollbackAsync(ct);
            await RevokeFamilyAsync(stored.FamilyId, ct);
            throw new UnauthorizedException(InvalidSession);
        }

        // El rol se relee de la DB en cada refresh: un cambio de rol o una expulsión
        // se aplica como mucho en lo que dura un access token (15 min).
        var membership = await db.WorkspaceMembers
            .IgnoreQueryFilters()
            .Where(m => m.WorkspaceId == stored.WorkspaceId && m.UserId == stored.UserId)
            .Join(db.Workspaces, m => m.WorkspaceId, w => w.Id, (m, w) => new { w, m.Role })
            .SingleOrDefaultAsync(ct);
        var user = await users.FindByIdAsync(stored.UserId.ToString());

        if (membership is null || user is null || await users.IsLockedOutAsync(user))
        {
            await tx.CommitAsync(ct); // el token viejo queda revocado igual
            throw new UnauthorizedException(InvalidSession);
        }

        var auth = IssueSession(user, membership.w, membership.Role, stored.FamilyId, newTokenId);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return auth;
    }

    public async Task<AuthResult> SwitchWorkspaceAsync(
        Guid userId, Guid workspaceId, string? currentRefreshToken, CancellationToken ct)
    {
        var membership = await db.WorkspaceMembers
            .IgnoreQueryFilters()
            .Where(m => m.WorkspaceId == workspaceId && m.UserId == userId)
            .Join(db.Workspaces, m => m.WorkspaceId, w => w.Id, (m, w) => new { w, m.Role })
            .SingleOrDefaultAsync(ct)
            // 404 y no 403: no confirmamos que exista un workspace al que no pertenecés.
            ?? throw new NotFoundException("Workspace no encontrado.");

        var user = await users.FindByIdAsync(userId.ToString())
            ?? throw new UnauthorizedException(InvalidSession);

        // La sesión del workspace anterior se cierra: una sola familia viva por sesión de navegador.
        await LogoutAsync(currentRefreshToken, ct);

        var auth = IssueSession(user, membership.w, membership.Role, familyId: Guid.CreateVersion7());
        await db.SaveChangesAsync(ct);
        return auth;
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refreshToken))
            return;

        var hash = TokenService.Hash(refreshToken);
        var familyId = await db.RefreshTokens
            .Where(t => t.TokenHash == hash)
            .Select(t => (Guid?)t.FamilyId)
            .SingleOrDefaultAsync(ct);

        if (familyId is not null)
            await RevokeFamilyAsync(familyId.Value, ct);
    }

    public async Task<MeResponse> GetMeAsync(Guid userId, Guid currentWorkspaceId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString())
            ?? throw new UnauthorizedException(InvalidSession);

        var workspaces = await db.WorkspaceMembers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.JoinedAt)
            .Join(db.Workspaces, m => m.WorkspaceId, w => w.Id,
                (m, w) => new Application.Workspaces.WorkspaceSummaryDto(w.Id, w.Name, w.Slug, m.Role))
            .ToListAsync(ct);

        return new MeResponse(ToDto(user), currentWorkspaceId, workspaces);
    }

    private AuthResult IssueSession(
        ApplicationUser user, Workspace workspace, WorkspaceRole role, Guid familyId, Guid? refreshTokenId = null)
    {
        var (access, accessExpires) = tokens.CreateAccessToken(user, workspace.Id, role);
        var (refresh, refreshPlain) = tokens.CreateRefreshToken(
            refreshTokenId ?? Guid.CreateVersion7(), user.Id, workspace.Id, familyId);
        db.RefreshTokens.Add(refresh);

        return new AuthResult(
            access, accessExpires, refreshPlain, refresh.ExpiresAt,
            ToDto(user), new CurrentWorkspaceDto(workspace.Id, workspace.Name, workspace.Slug, role));
    }

    private Task<int> RevokeFamilyAsync(Guid familyId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    private static UserDto ToDto(ApplicationUser user) => new(user.Id, user.Email!, user.DisplayName);

    private static Exception ToException(IdentityResult result)
    {
        if (result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName"))
            return new ConflictException("Ya existe una cuenta con ese email.");

        // Errores de Identity (contraseña débil, email inválido...) → mismo formato que FluentValidation.
        return new ValidationException(result.Errors.Select(e => new ValidationFailure(
            e.Code.StartsWith("Password", StringComparison.Ordinal) ? nameof(RegisterRequest.Password) : nameof(RegisterRequest.Email),
            e.Description)));
    }
}
