namespace TaskFlow.Application.Common.Exceptions;

/// <summary>
/// 403: sabemos quién sos y el recurso es de tu workspace, pero no podés hacer ESTO.
/// Nunca se usa para recursos de otro tenant: eso es 404, para no confirmar que existen.
/// </summary>
public sealed class ForbiddenException(string message) : Exception(message);
