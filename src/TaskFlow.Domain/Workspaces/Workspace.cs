using System.Text.RegularExpressions;
using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Workspaces;

public sealed partial class Workspace : Entity
{
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public WorkspacePlan Plan { get; private set; }
    public DateTime CreatedAt { get; private init; }

    private Workspace() { } // EF Core

    /// <summary>Bajar a Free con más uso del permitido es válido: lo existente se conserva, no se puede agregar más.</summary>
    public void ChangePlan(WorkspacePlan plan)
    {
        if (!Enum.IsDefined(plan))
            throw new DomainException("Plan inválido.");
        Plan = plan;
    }

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
