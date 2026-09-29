namespace TaskFlow.Domain.Common;

/// <summary>
/// Entidades que no se borran físicamente. El borrado lo convierte AppDbContext.SaveChanges en
/// "deleted_at = now()", y un filtro global las oculta de toda consulta.
/// </summary>
public interface ISoftDeletable
{
    DateTime? DeletedAt { get; }
    void MarkDeleted(DateTime utcNow);
}

/// <summary>Entidades cuyos cambios quedan registrados en la tabla de actividad.</summary>
public interface IAuditable
{
    Guid Id { get; }
}
