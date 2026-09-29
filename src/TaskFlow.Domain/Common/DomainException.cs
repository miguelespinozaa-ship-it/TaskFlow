namespace TaskFlow.Domain.Common;

/// <summary>Se lanza cuando una operación violaría una invariante del dominio.</summary>
public sealed class DomainException(string message) : Exception(message);
