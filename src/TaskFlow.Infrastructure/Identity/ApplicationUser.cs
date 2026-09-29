using Microsoft.AspNetCore.Identity;

namespace TaskFlow.Infrastructure.Identity;

/// <summary>
/// Usuario de ASP.NET Core Identity (hash PBKDF2 con salt, lockout, security stamp).
/// Vive en Infrastructure: el dominio solo conoce el Id del usuario.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = null!;
    public DateTime CreatedAt { get; init; }

    private ApplicationUser() { } // EF Core

    public ApplicationUser(string email, string displayName, DateTime utcNow)
    {
        Id = Guid.CreateVersion7();
        Email = email;
        UserName = email;
        DisplayName = displayName.Trim();
        CreatedAt = utcNow;
    }
}
