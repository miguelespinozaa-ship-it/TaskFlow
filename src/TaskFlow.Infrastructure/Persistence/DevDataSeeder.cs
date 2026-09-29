using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Workspaces;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Infrastructure.Persistence;

/// <summary>
/// Datos de demo para desarrollo local. Idempotente. No aplica migraciones: el esquema se crea con
/// <c>dotnet ef database update</c>, nunca con EnsureCreated.
/// </summary>
public static class DevDataSeeder
{
    public const string DemoPassword = "Demo1234";

    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DevDataSeeder));

        if ((await db.Database.GetPendingMigrationsAsync(ct)).Any())
        {
            logger.LogWarning("Hay migraciones pendientes; seed omitido. Corré `dotnet ef database update`.");
            return;
        }

        var now = DateTime.UtcNow;
        var workspace = await db.Workspaces.FirstOrDefaultAsync(w => w.Slug == "demo", ct);
        if (workspace is null)
        {
            workspace = Workspace.Create("Demo", "demo", now);
            db.Workspaces.Add(workspace);
            db.Projects.Add(Project.Create(workspace.Id, "TaskFlow MVP", "TF", "Primer proyecto de ejemplo", now));
            await db.SaveChangesAsync(ct);
        }

        // Un usuario por rol para poder probar RBAC a mano.
        await EnsureMemberAsync("demo@taskflow.dev", "Demo Owner", WorkspaceRole.Owner);
        await EnsureMemberAsync("member@taskflow.dev", "Demo Member", WorkspaceRole.Member);
        await EnsureMemberAsync("viewer@taskflow.dev", "Demo Viewer", WorkspaceRole.Viewer);

        async Task EnsureMemberAsync(string email, string name, WorkspaceRole role)
        {
            var user = await users.FindByEmailAsync(email);
            if (user is null)
            {
                user = new ApplicationUser(email, name, now);
                var result = await users.CreateAsync(user, DemoPassword);
                if (!result.Succeeded)
                    throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
                logger.LogInformation("Usuario demo {Email} ({Role}) / contraseña {Password}", email, role, DemoPassword);
            }

            var isMember = await db.WorkspaceMembers.IgnoreQueryFilters()
                .AnyAsync(m => m.WorkspaceId == workspace.Id && m.UserId == user.Id, ct);
            if (!isMember)
            {
                db.WorkspaceMembers.Add(WorkspaceMember.Create(workspace.Id, user.Id, role, now));
                await db.SaveChangesAsync(ct);
            }
        }
    }
}
