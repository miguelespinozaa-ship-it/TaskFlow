using NetArchTest.Rules;
using TaskFlow.Application;
using TaskFlow.Domain.Common;

namespace TaskFlow.ArchitectureTests;

public sealed class LayerDependencyTests
{
    [Fact]
    public void Domain_no_depende_de_capas_externas_ni_de_EF()
    {
        var result = Types.InAssembly(typeof(Entity).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "TaskFlow.Application", "TaskFlow.Infrastructure", "TaskFlow.Api",
                "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore")
            .GetResult();

        result.FailingTypeNames.ShouldBeNull();
    }

    [Fact]
    public void Application_no_depende_de_Infrastructure_Api_ni_EF()
    {
        var result = Types.InAssembly(typeof(DependencyInjection).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "TaskFlow.Infrastructure", "TaskFlow.Api",
                "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore")
            .GetResult();

        result.FailingTypeNames.ShouldBeNull();
    }

    [Fact]
    public void Control_Infrastructure_si_depende_de_EF()
    {
        // Test de control: si NetArchTest dejara de detectar dependencias, las reglas de
        // arriba pasarían en falso. Este falla en ese caso.
        var result = Types.InAssembly(typeof(TaskFlow.Infrastructure.DependencyInjection).Assembly)
            .ShouldNot().HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.ShouldBeFalse();
        result.FailingTypeNames.ShouldContain("TaskFlow.Infrastructure.Persistence.AppDbContext");
    }

    [Fact]
    public void Infrastructure_no_depende_de_Api()
    {
        var result = Types.InAssembly(typeof(TaskFlow.Infrastructure.DependencyInjection).Assembly)
            .ShouldNot()
            .HaveDependencyOn("TaskFlow.Api")
            .GetResult();

        result.FailingTypeNames.ShouldBeNull();
    }
}
