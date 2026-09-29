using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Workspaces;
using TaskFlow.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace TaskFlow.IntegrationTests.Infrastructure;

/// <summary>
/// Levanta la API completa contra un PostgreSQL real en Docker. No usamos EF InMemory:
/// ignora índices, FKs y transacciones, y los tests pasarían aunque producción fallara.
/// </summary>
public sealed class TaskFlowApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
        builder.UseSetting("Jwt:Secret", "integration-tests-secret-0123456789-abcdefghijklmnop");
    }

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        // Mismas migraciones que producción, no EnsureCreated.
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    /// <summary>Cliente sin cookies automáticas: los tests manejan la cookie de refresh a mano.</summary>
    public HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    /// <summary>Workspace + proyecto directo en la DB (contexto de sistema), para tests que no pasan por HTTP.</summary>
    public async Task<Project> SeedProjectAsync(bool archived = false)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        var workspace = Workspace.Create("Test", $"ws-{Guid.NewGuid():N}", now);
        var project = Project.Create(workspace.Id, "Proyecto test", "PT", null, now);
        if (archived) project.Archive();

        db.AddRange(workspace, project);
        await db.SaveChangesAsync();
        return project;
    }

    public async Task ArchiveProjectAsync(Guid projectId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = await db.Projects.SingleAsync(p => p.Id == projectId);
        project.Archive();
        await db.SaveChangesAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

/// <summary>Un solo contenedor de Postgres para todas las clases de test. Cada test crea sus propios
/// usuarios y workspaces con emails/slugs únicos, así que no comparten estado.</summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<TaskFlowApiFactory>
{
    public const string Name = "api";
}
