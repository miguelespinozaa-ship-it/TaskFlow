namespace TaskFlow.Domain.Tasks;

/// <summary>
/// Fractional indexing: para mover una tarea entre dos vecinas se le asigna el punto medio de sus
/// posiciones. Es un UPDATE de UNA fila, en vez de renumerar toda la columna.
/// </summary>
public static class BoardPosition
{
    public const decimal Gap = 1000m;

    /// <summary>Por debajo de esta distancia entre vecinas la columna se renumera (la columna es numeric(20,10)).</summary>
    public const decimal MinGap = 0.000001m;

    /// <param name="previous">Posición de la tarea que queda arriba (null = primera de la columna).</param>
    /// <param name="next">Posición de la tarea que queda abajo (null = última de la columna).</param>
    public static decimal Between(decimal? previous, decimal? next) => (previous, next) switch
    {
        (null, null) => Gap,
        (decimal p, null) => p + Gap,
        (null, decimal n) => n - Gap,
        (decimal p, decimal n) when p < n => p + (n - p) / 2,
        _ => throw new ArgumentException("La posición anterior tiene que ser menor que la siguiente."),
    };

    public static bool NeedsRebalance(decimal? previous, decimal? next) =>
        previous is decimal p && next is decimal n && n - p < MinGap * 2;
}
