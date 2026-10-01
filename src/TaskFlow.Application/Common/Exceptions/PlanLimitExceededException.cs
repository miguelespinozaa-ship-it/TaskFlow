namespace TaskFlow.Application.Common.Exceptions;

/// <summary>402: la operación es válida, pero el plan del workspace no la incluye. Se resuelve cambiando de plan.</summary>
public sealed class PlanLimitExceededException(string message) : Exception(message);
