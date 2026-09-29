using System.Text.RegularExpressions;
using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Workspaces;

public sealed partial class Workspace : Entity
{
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public DateTime CreatedAt { get; private init; }

    private Workspace() { } // EF Core

    public static Workspace Create(string name, string slug, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre del workspace es obligatorio.");
        if (!SlugPattern().IsMatch(slug))
            throw new DomainException("El slug solo admite minúsculas, números y guiones.");

        return new Workspace { Name = name.Trim(), Slug = slug, CreatedAt = utcNow };
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}
