using System.Text.RegularExpressions;
using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Labels;

public sealed partial class Label : Entity, ITenantEntity, IAuditable
{
    public const int NameMaxLength = 40;

    public Guid WorkspaceId { get; set; }
    public string Name { get; private set; } = null!;
    public string Color { get; private set; } = null!;

    private Label() { } // EF Core

    public static Label Create(Guid workspaceId, string name, string color)
    {
        if (workspaceId == Guid.Empty)
            throw new DomainException("La etiqueta tiene que pertenecer a un workspace.");
        var label = new Label { WorkspaceId = workspaceId };
        label.Update(name, color);
        return label;
    }

    public void Update(string name, string color)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre de la etiqueta es obligatorio.");
        name = name.Trim();
        if (name.Length > NameMaxLength)
            throw new DomainException($"El nombre no puede superar {NameMaxLength} caracteres.");
        if (!HexColor().IsMatch(color))
            throw new DomainException("El color tiene que ser hexadecimal, p. ej. #4f46e5.");

        Name = name;
        Color = color.ToLowerInvariant();
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();
}
