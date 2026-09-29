using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace TaskFlow.Application.Common;

/// <summary>
/// Ejecuta el IValidator&lt;T&gt; registrado para un request, si existe. Evita que cada servicio con
/// varios casos de uso reciba un validador por constructor para cada uno.
/// </summary>
public interface IRequestValidator
{
    Task ValidateAsync<T>(T request, CancellationToken ct);
}

internal sealed class RequestValidator(IServiceProvider services) : IRequestValidator
{
    public async Task ValidateAsync<T>(T request, CancellationToken ct)
    {
        foreach (var validator in services.GetServices<IValidator<T>>())
            await validator.ValidateAndThrowAsync(request, ct);
    }
}
