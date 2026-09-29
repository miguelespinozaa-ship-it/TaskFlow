using TaskFlow.Application.Tasks;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.UnitTests.Application;

public sealed class CreateTaskRequestValidatorTests
{
    private readonly CreateTaskRequestValidator _validator = new();

    [Fact]
    public void Request_valido_pasa()
    {
        _validator.Validate(new CreateTaskRequest("Tarea", "desc", TaskPriority.High)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Titulo_vacio_falla_en_Title()
    {
        var result = _validator.Validate(new CreateTaskRequest("", null));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(CreateTaskRequest.Title));
    }

    [Fact]
    public void Prioridad_fuera_del_enum_falla()
    {
        var result = _validator.Validate(new CreateTaskRequest("Tarea", null, (TaskPriority)99));

        result.Errors.ShouldContain(e => e.PropertyName == nameof(CreateTaskRequest.Priority));
    }
}
