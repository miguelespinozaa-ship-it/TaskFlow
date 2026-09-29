using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;
using TaskFlow.Domain.Workspaces;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Infrastructure.Persistence;

// IdentityUserContext (y no IdentityDbContext): los roles son POR WORKSPACE (workspace_members),
// no globales, así que las tablas de roles de Identity no se usan.
public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext? tenant = null)
    : IdentityUserContext<ApplicationUser, Guid>(options), IUnitOfWork
{
    private Guid? _workspaceOverride;
    private bool _hasOverride;

    /// <summary>
    /// Workspace activo: por defecto el que resolvió TenantResolutionMiddleware para este request.
    /// NULL = contexto de sistema (seed, jobs, migraciones) donde el filtro NO se aplica.
    /// Se puede fijar a mano (tests, jobs que operan sobre un tenant concreto).
    /// </summary>
    public Guid? CurrentWorkspaceId
    {
        get => _hasOverride ? _workspaceOverride : tenant?.WorkspaceId;
        set
        {
            _workspaceOverride = value;
            _hasOverride = true;
        }
    }

    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        ApplyTenantFilters(modelBuilder);
    }

    private void ApplyTenantFilters(ModelBuilder modelBuilder)
    {
        // OnModelCreating corre UNA vez y el modelo queda cacheado para todos los contextos.
        // Por eso el filtro no puede capturar el valor de CurrentWorkspaceId como constante:
        // referencia la propiedad de la instancia y EF la evalúa como parámetro en cada query.
        //   e => this.CurrentWorkspaceId == null || e.WorkspaceId == this.CurrentWorkspaceId
        var currentWorkspaceId = Expression.Property(Expression.Constant(this), nameof(CurrentWorkspaceId));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
                continue;

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var entityWorkspaceId = Expression.Convert(
                Expression.Property(parameter, nameof(ITenantEntity.WorkspaceId)), typeof(Guid?));

            var body = Expression.OrElse(
                Expression.Equal(currentWorkspaceId, Expression.Constant(null, typeof(Guid?))),
                Expression.Equal(entityWorkspaceId, currentWorkspaceId));

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(Expression.Lambda(body, parameter));
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTenant();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTenant();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // Sin esto, un `new TaskItem()` sin WorkspaceId se persiste con tenant vacío y nunca
    // aparece en ningún query. Nunca se sobreescribe el tenant de una entidad existente.
    private void StampTenant()
    {
        foreach (var entry in ChangeTracker.Entries<ITenantEntity>())
        {
            if (entry.State is EntityState.Added && CurrentWorkspaceId is Guid id && entry.Entity.WorkspaceId == Guid.Empty)
                entry.Entity.WorkspaceId = id;
        }
    }
}
