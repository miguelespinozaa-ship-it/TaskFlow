using FluentValidation;
using FluentValidation.Results;

namespace TaskFlow.Application.Common;

public static class Validation
{
    /// <summary>Error de validación de un campo, con el mismo formato (400 + errors) que FluentValidation.</summary>
    public static ValidationException Fail(string property, string message) =>
        new([new ValidationFailure(property, message)]);
}
