using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Infrastructure.GitHub;

public sealed class GitHubOptions
{
    public const string SectionName = "GitHub";

    /// <summary>
    /// Token opcional (variable de entorno GitHub__Token; nunca en el repo). No hace falta para repos públicos,
    /// pero sube el límite de 60 a 5000 requests por hora. Alcanza con un token SIN permisos (solo lectura pública).
    /// </summary>
    public string? Token { get; init; }

    /// <summary>Cada cuánto el sincronizador de fondo revisa los repositorios enlazados. 0 = desactivado.</summary>
    [Range(0, 1440)]
    public int SyncIntervalMinutes { get; init; } = 5;
}
