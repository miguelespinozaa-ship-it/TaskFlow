namespace TaskFlow.Application.Common.Exceptions;

/// <summary>409: la operación choca con el estado actual (email ya registrado, slug tomado...).</summary>
public sealed class ConflictException(string message) : Exception(message);
