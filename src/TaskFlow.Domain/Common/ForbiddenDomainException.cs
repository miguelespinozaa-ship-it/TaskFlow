namespace TaskFlow.Domain.Common;

/// <summary>La operación es válida en sí, pero ESTE usuario no puede hacerla (p. ej. editar un comentario ajeno).</summary>
public sealed class ForbiddenDomainException(string message) : Exception(message);
