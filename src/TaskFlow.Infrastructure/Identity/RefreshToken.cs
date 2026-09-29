namespace TaskFlow.Infrastructure.Identity;

/// <summary>
/// Refresh token persistido. Solo se guarda el HASH: si alguien lee la tabla no puede usar los tokens.
/// Cada login abre una "familia"; cada refresh revoca el token usado y emite otro de la misma familia.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; private init; }
    public Guid UserId { get; private init; }
    public Guid WorkspaceId { get; private init; }
    public string TokenHash { get; private init; } = null!;
    public Guid FamilyId { get; private init; }
    public DateTime ExpiresAt { get; private init; }
    public DateTime CreatedAt { get; private init; }
    public DateTime? RevokedAt { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }

    private RefreshToken() { } // EF Core

    public static RefreshToken Create(
        Guid id, Guid userId, Guid workspaceId, string tokenHash, Guid familyId, DateTime expiresAt, DateTime utcNow) =>
        new()
        {
            Id = id,
            UserId = userId,
            WorkspaceId = workspaceId,
            TokenHash = tokenHash,
            FamilyId = familyId,
            ExpiresAt = expiresAt,
            CreatedAt = utcNow,
        };
}
