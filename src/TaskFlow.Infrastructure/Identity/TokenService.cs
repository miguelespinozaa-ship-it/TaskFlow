using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TaskFlow.Domain.Workspaces;

namespace TaskFlow.Infrastructure.Identity;

public static class TaskFlowClaims
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string WorkspaceId = "workspace_id";
    public const string Role = "role";
}

internal sealed class TokenService(IOptions<JwtOptions> options, TimeProvider clock)
{
    private readonly JwtOptions _options = options.Value;
    private readonly JsonWebTokenHandler _handler = new();

    public (string Token, DateTime ExpiresAt) CreateAccessToken(ApplicationUser user, Guid workspaceId, WorkspaceRole role)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret)), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [TaskFlowClaims.Subject] = user.Id.ToString(),
                [TaskFlowClaims.Email] = user.Email!,
                [TaskFlowClaims.Name] = user.DisplayName,
                // El tenant va DENTRO del token firmado: el cliente no puede elegirlo por header o URL.
                [TaskFlowClaims.WorkspaceId] = workspaceId.ToString(),
                [TaskFlowClaims.Role] = role.ToString(),
                ["jti"] = Guid.NewGuid().ToString(),
            },
        });

        return (token, expires);
    }

    public (RefreshToken Entity, string PlainText) CreateRefreshToken(Guid id, Guid userId, Guid workspaceId, Guid familyId)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var plain = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var entity = RefreshToken.Create(
            id, userId, workspaceId, Hash(plain), familyId, now.AddDays(_options.RefreshTokenDays), now);
        return (entity, plain);
    }

    // SHA-256 sin salt es correcto ACÁ (no para contraseñas): el token tiene 256 bits aleatorios,
    // no hay diccionario ni rainbow table posible. Y al ser determinista se puede buscar por índice.
    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
