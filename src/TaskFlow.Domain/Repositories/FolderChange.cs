namespace TaskFlow.Domain.Repositories;

/// <summary>Un archivo tocado por un commit.</summary>
public sealed record ChangedFile(string Path, int Additions, int Deletions);

/// <summary>Resumen de lo que un commit cambió dentro de una carpeta. <see cref="Path"/> vacío = raíz del repo.</summary>
public sealed record FolderChange(string Path, int Files, int Additions, int Deletions);

public static class FolderSummary
{
    /// <summary>
    /// Agrupa los archivos de un commit por carpeta, hasta <paramref name="depth"/> niveles.
    /// Con depth 2, "src/Api/Controllers/Tasks.cs" y "src/Api/Program.cs" caen en "src/Api":
    /// suficiente para ver QUÉ parte del sistema se tocó sin listar cada subcarpeta.
    /// </summary>
    public static IReadOnlyList<FolderChange> From(IEnumerable<ChangedFile> files, int depth = 2)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(depth, 1);

        return [.. files
            .GroupBy(f => FolderOf(f.Path, depth), StringComparer.Ordinal)
            .Select(g => new FolderChange(g.Key, g.Count(), g.Sum(f => f.Additions), g.Sum(f => f.Deletions)))
            // Primero donde más se trabajó; a igualdad, alfabético para que el orden sea estable.
            .OrderByDescending(f => f.Additions + f.Deletions)
            .ThenByDescending(f => f.Files)
            .ThenBy(f => f.Path, StringComparer.Ordinal)];
    }

    private static string FolderOf(string path, int depth)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var folders = segments.Length - 1; // el último segmento es el archivo
        return folders <= 0 ? string.Empty : string.Join('/', segments.Take(Math.Min(depth, folders)));
    }
}
