using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required] public string Issuer { get; init; } = null!;
    [Required] public string Audience { get; init; } = null!;

    /// <summary>Clave HMAC-SHA256. Mínimo 32 bytes. En producción viene de una variable de entorno, nunca del repo.</summary>
    [Required, MinLength(32)] public string Secret { get; init; } = null!;

    [Range(1, 60)] public int AccessTokenMinutes { get; init; } = 15;
    [Range(1, 90)] public int RefreshTokenDays { get; init; } = 7;
}
