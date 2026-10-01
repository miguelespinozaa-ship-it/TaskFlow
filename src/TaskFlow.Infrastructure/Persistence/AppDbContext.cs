using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Auditing;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Labels;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Repositories;
using TaskFlow.Domain.Tasks;
using TaskFlow.Domain.Workspaces;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Infrastructure.Persistence;

public static class QueryFilters
{
    public const string Tenant = "Tenant";
    public const string SoftDelete = "SoftDelete";
}

// IdentityUserContext (y no IdentityDbContext): los roles son POR WORKSPACE (workspace_members),
// no globales, así que las tablas de roles de Identity no se usan.
public sealed class AppDbContext : IdentityUserContext<ApplicationUser, Guid>, IUnitOfWork
{
    private readonly ITenantContext? _tenant;
    private readonly ICurrentUser? _currentUser;
    private readonly TimeProvider _clock;
    private readonly IWorkspaceNotifier? _notifier;
    private Guid? _workspaceOverride;
    private bool _hasOverride;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ITenantContext? tenant = null,
        ICurrentUser? currentUser = null,
        TimeProvider? clock = null,
        IWorkspaceNotifier? notifier = null) : base(options)
    {
        _tenant = tenant;
        _currentUser = currentUser;
        _clock = clock ?? TimeProvider.System;
        _notifier = notifier;

        // Por defecto EF marca como borrados a los dependientes EN EL MOMENTO del Remove(). Lo diferimos
        // a SaveChanges para que el soft delete convierta primero el Deleted en Modified: si no, borrar
        // (soft) una tarea borraría (hard) sus filas de task_labels.
        ChangeTracker.CascadeDeleteTiming = CascadeTiming.OnSaveChanges;
        ChangeTracker.DeleteOrphansTiming = CascadeTiming.OnSaveChanges;
    }

    /// <summary>
    /// Workspace activo: por defecto el que resolvió TenantResolutionMiddleware para este request.
    /// NULL = contexto de sistema (seed, jobs, migraciones) donde el filtro NO se aplica.
    /// Se puede fijar a mano (tests, jobs que operan sobre un tenant concreto).
    /// </summary>
    public Guid? CurrentWorkspaceId
    {
        get => _hasOverride ? _workspaceOverride : _tenant?.WorkspaceId;
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
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<Label> Labels => Set<Label>();
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<RepositoryLink> RepositoryLinks => Set<RepositoryLink>();
    public DbSet<RepositoryCommit> RepositoryCommits => Set<RepositoryCommit>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        ApplyQueryFilters(modelBuilder);
    }

    private void ApplyQueryFilters(ModelBuilder modelBuilder)
    {
        // OnModelCreating corre UNA vez y el modelo queda cacheado para todos los contextos.
        // Por eso el filtro no puede capturar el valor de CurrentWorkspaceId como constante:
        // referencia la propiedad de la instancia y EF la evalúa como parámetro en cada query.
        //   e => this.CurrentWorkspaceId == null || e.WorkspaceId == this.CurrentWorkspaceId
        var currentWorkspaceId = Expression.Property(Expression.Constant(this), nameof(CurrentWorkspaceId));

        // Solo nuestras entidades: llamar modelBuilder.Entity() sobre los tipos internos de Identity
        // (p. ej. IdentityPasskeyData, que .NET 10 mapea como JSON) los convierte en entidades sin clave.
        var filteredTypes = modelBuilder.Model.GetEntityTypes()
            .Select(t => t.ClrType)
            .Where(t => typeof(ITenantEntity).IsAssignableFrom(t) || typeof(ISoftDeletable).IsAssignableFrom(t))
            .ToList();

        foreach (var clrType in filteredTypes)
        {
            var builder = modelBuilder.Entity(clrType);

            // Filtros con nombre (EF Core 10): se pueden ignorar por separado. Ver una tarea borrada
            // (IgnoreQueryFilters([SoftDelete])) sigue respetando el tenant.
            if (typeof(ITenantEntity).IsAssignableFrom(clrType))
            {
                var e = Expression.Parameter(clrType, "e");
                var entityWorkspaceId = Expression.Convert(
                    Expression.Property(e, nameof(ITenantEntity.WorkspaceId)), typeof(Guid?));
                var body = Expression.OrElse(
                    Expression.Equal(currentWorkspaceId, Expression.Constant(null, typeof(Guid?))),
                    Expression.Equal(entityWorkspaceId, currentWorkspaceId));
                builder.HasQueryFilter(QueryFilters.Tenant, Expression.Lambda(body, e));
            }

            if (typeof(ISoftDeletable).IsAssignableFrom(clrType))
            {
                var e = Expression.Parameter(clrType, "e");
                var body = Expression.Equal(
                    Expression.Property(e, nameof(ISoftDeletable.DeletedAt)), Expression.Constant(null, typeof(DateTime?)));
                builder.HasQueryFilter(QueryFilters.SoftDelete, Expression.Lambda(body, e));
            }
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        BeforeSave();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var changedWorkspaces = BeforeSave();
        var saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        // Después de guardar, nunca antes: si el guardado falla, nadie recibe el aviso de un cambio que no existió.
        if (_notifier is not null)
            foreach (var workspaceId in changedWorkspaces)
                await _notifier.WorkspaceChangedAsync(workspaceId, cancellationToken);
        return saved;
    }

    /// <returns>Workspaces con cambios auditables en este guardado (los que hay que avisar en tiempo real).</returns>
    private HashSet<Guid> BeforeSave()
    {
        ChangeTracker.DetectChanges();
        var now = _clock.GetUtcNow().UtcDateTime;

        StampTenant();
        var softDeleted = ConvertDeletesToSoftDeletes(now);
        // La auditoría se escribe en el MISMO SaveChanges (misma transacción): no puede haber un
        // cambio sin su registro, ni un registro de un cambio que falló.
        var activities = AuditTrail.Build(ChangeTracker, softDeleted, _currentUser?.UserId, now).ToList();
        Activities.AddRange(activities);
        return [.. activities.Select(a => a.WorkspaceId)];
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

    // Se guardan las ENTIDADES (por referencia), no las EntityEntry: ChangeTracker.Entries() crea
    // instancias de EntityEntry nuevas en cada llamada, así que un HashSet de entries nunca "encuentra" nada.
    private HashSet<object> ConvertDeletesToSoftDeletes(DateTime now)
    {
        var converted = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var entry in ChangeTracker.Entries<ISoftDeletable>().Where(e => e.State == EntityState.Deleted).ToList())
        {
            // Unchanged + marcar solo deleted_at: el UPDATE toca una columna, no todas.
            entry.State = EntityState.Unchanged;
            entry.Entity.MarkDeleted(now);
            entry.Property(nameof(ISoftDeletable.DeletedAt)).IsModified = true;
            converted.Add(entry.Entity);
        }
        return converted;
    }
}
