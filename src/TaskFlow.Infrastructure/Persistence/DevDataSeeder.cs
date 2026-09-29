using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Workspaces;

namespace TaskFlow.Infrastructure.Persistence;

/// <summary>
/// Datos de demo para desarrollo local. No aplica migraciones: el esquema se crea con
/// <c>dotnet ef database update</c>, nunca con EnsureCreated.
/// </summary>
public static class DevDataSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DevDataSeeder));

        if ((await db.Database.GetPendingMigrationsAsync(ct)).Any())
        {
            logger.LogWarning("Hay migraciones pendientes; seed omitido. Corré `dotnet ef database update`.");
            return;
        }

        if (await db.Workspaces.AnyAsync(ct))
            return;

        var now = DateTime.UtcNow;
        var workspace = Workspace.Create("Demo", "demo", now);
        db.Workspaces.Add(workspace);
        db.Projects.Add(Project.Create(workspace.Id, "TaskFlow MVP", "TF", "Primer proyecto de ejemplo", now));
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Seed de desarrollo creado: workspace {Slug}", workspace.Slug);
    }
}
