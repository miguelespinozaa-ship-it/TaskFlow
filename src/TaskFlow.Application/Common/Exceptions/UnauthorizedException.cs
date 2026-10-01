namespace TaskFlow.Application.Common.Exceptions;

/// <summary>401: no sabemos quién eres (credenciales o token inválidos).</summary>
public sealed class UnauthorizedException(string message) : Exception(message);
