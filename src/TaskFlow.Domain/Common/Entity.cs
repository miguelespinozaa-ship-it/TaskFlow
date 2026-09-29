namespace TaskFlow.Domain.Common;

public abstract class Entity
{
    // UUID v7: ordenable por tiempo, así los inserts no fragmentan el índice de la PK
    // como pasaría con UUID v4 aleatorios.
    public Guid Id { get; protected init; } = Guid.CreateVersion7();
}
