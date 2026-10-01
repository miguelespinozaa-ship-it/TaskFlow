using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace TaskFlow.Domain.Repositories;

/// <summary>Identifica un repositorio de GitHub: "owner/name".</summary>
public readonly partial record struct RepositoryName(string Owner, string Name)
{
    public string FullName => $"{Owner}/{Name}";

    /// <summary>
    /// Acepta "owner/name", "https://github.com/owner/name(.git)", "github.com/owner/name/tree/main"
    /// y "git@github.com:owner/name.git".
    ///
    /// El resultado se usa para armar la URL de la API de GitHub, así que la validación es estricta
    /// (solo los caracteres que GitHub permite): nada de "..", barras extra ni otros hosts.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? input, out RepositoryName result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var value = input.Trim();
        foreach (var prefix in Prefixes)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = value[prefix.Length..];
                break;
            }
        }

        // Una URL puede traer más segmentos (/tree/main, /pulls…): solo importan los dos primeros.
        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
            return false;

        var owner = segments[0];
        var name = segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? segments[1][..^4] : segments[1];

        // Sin prefijo de GitHub solo vale la forma exacta "owner/name".
        if (value == input.Trim() && segments.Length != 2)
            return false;
        if (!OwnerPattern().IsMatch(owner) || !NamePattern().IsMatch(name) || name is "." or "..")
            return false;

        result = new RepositoryName(owner, name);
        return true;
    }

    private static readonly string[] Prefixes =
    [
        "https://github.com/", "http://github.com/", "https://www.github.com/", "git@github.com:", "github.com/",
    ];

    // Reglas de GitHub: usuario/organización alfanumérico con guiones (máx. 39); repo con . _ - (máx. 100).
    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$")]
    private static partial Regex OwnerPattern();

    [GeneratedRegex("^[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex NamePattern();
}
