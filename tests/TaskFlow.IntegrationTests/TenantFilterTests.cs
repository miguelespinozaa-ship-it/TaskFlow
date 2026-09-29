using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Domain.Tasks;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.IntegrationTests.Infrastructure;

namespace TaskFlow.IntegrationTests;

/// <summary>
/// Verifica el global query filter por tenant contra Postgres real. El resolver de tenant
/// llega en la fase 2; acá seteamos CurrentWorkspaceId a mano en el DbContext.
/// </summary>
public sealed class TenantFilterTests(TaskFlowApiFactory factory) : IClassFixture<TaskFlowApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Contexto_de_un_workspace_no_ve_tareas_de_otro()
    {
        var projectA = await factory.SeedProjectAsync();
        var projectB = await factory.SeedProjectAsync();
        await AddTaskAsync(projectA.Id, "Tarea de A");
        await AddTaskAsync(projectB.Id, "Tarea de B");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.CurrentWorkspaceId = projectA.WorkspaceId;

        var visibles = await db.Tasks
            .Where(t => t.ProjectId == projectA.Id || t.ProjectId == projectB.Id)
            .Select(t => t.Title)
            .ToListAsync(Ct);

        visibles.ShouldBe(["Tarea de A"]);
        (await db.Projects.AnyAsync(p => p.Id == projectB.Id, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task El_filtro_se_evalua_por_instancia_no_queda_cacheado()
    {
        // Regresión: si el filtro capturara el tenant como constante al construir el modelo,
        // el segundo contexto seguiría viendo el workspace del primero.
        var projectA = await factory.SeedProjectAsync();
        var projectB = await factory.SeedProjectAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.CurrentWorkspaceId = projectA.WorkspaceId;
            (await db.Projects.AnyAsync(p => p.Id == projectA.Id, Ct)).ShouldBeTrue();
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.CurrentWorkspaceId = projectB.WorkspaceId;
            (await db.Projects.AnyAsync(p => p.Id == projectB.Id, Ct)).ShouldBeTrue();
            (await db.Projects.AnyAsync(p => p.Id == projectA.Id, Ct)).ShouldBeFalse();
        }
    }

    private async Task AddTaskAsync(Guid projectId, string title)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = await db.Projects.SingleAsync(p => p.Id == projectId, Ct);
        db.Tasks.Add(TaskItem.Create(project, title, null, TaskPriority.Medium, 1000m, DateTime.UtcNow));
        await db.SaveChangesAsync(Ct);
    }
}
