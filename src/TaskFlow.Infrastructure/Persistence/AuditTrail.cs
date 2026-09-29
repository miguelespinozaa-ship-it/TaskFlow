using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using TaskFlow.Domain.Auditing;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Labels;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Infrastructure.Persistence;

/// <summary>
/// Genera los registros de Activity a partir del ChangeTracker. Al estar en SaveChanges, ningún caso de
/// uso puede "olvidarse" de auditar: cualquier cambio a una entidad IAuditable queda registrado.
/// </summary>
internal static class AuditTrail
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    // No aportan información en el historial (o son largos / cambian solos).
    private static readonly HashSet<string> IgnoredProperties =
    [
        "Id", nameof(ITenantEntity.WorkspaceId), "CreatedAt", "UpdatedAt", "EditedAt", nameof(ISoftDeletable.DeletedAt),
    ];

    // En el snapshot de "created" solo lo que identifica al recurso, no textos largos.
    private static readonly HashSet<string> SnapshotProperties =
        ["Title", "Name", "Status", "Priority", "KeyPrefix", "Color", "TaskId", "ProjectId", "AssigneeId"];

    public static IEnumerable<Activity> Build(
        ChangeTracker tracker, IReadOnlySet<object> softDeleted, Guid? actorId, DateTime now)
    {
        var activities = new List<Activity>();

        foreach (var entry in tracker.Entries().Where(e => e.Entity is IAuditable and ITenantEntity).ToList())
        {
            var entity = (IAuditable)entry.Entity;
            var workspaceId = ((ITenantEntity)entry.Entity).WorkspaceId;

            (string Action, Dictionary<string, object?> Changes)? record = entry.State switch
            {
                _ when softDeleted.Contains(entry.Entity) => (ActivityActions.Deleted, []),
                EntityState.Added => (ActivityActions.Created, Snapshot(entry)),
                EntityState.Deleted => (ActivityActions.Deleted, []),
                EntityState.Modified => Diff(entry) is { Count: > 0 } diff ? (ActivityActions.Updated, diff) : null,
                _ => null,
            };

            if (record is var (action, changes))
                activities.Add(Activity.Create(workspaceId, actorId, EntityType(entry.Entity), entity.Id, action,
                    JsonSerializer.Serialize(changes, Json), now));
        }

        activities.AddRange(LabelChanges(tracker, actorId, now));
        return activities;
    }

    private static Dictionary<string, object?> Snapshot(EntityEntry entry) =>
        entry.Properties
            .Where(p => SnapshotProperties.Contains(p.Metadata.Name))
            .ToDictionary(p => Camel(p.Metadata.Name), p => p.CurrentValue);

    private static Dictionary<string, object?> Diff(EntityEntry entry) =>
        entry.Properties
            .Where(p => p.IsModified && !IgnoredProperties.Contains(p.Metadata.Name) && !Equals(p.OriginalValue, p.CurrentValue))
            .ToDictionary(p => Camel(p.Metadata.Name), p => (object?)new { from = p.OriginalValue, to = p.CurrentValue });

    /// <summary>task_labels no es auditable por sí misma: sus altas/bajas se registran como un update de la tarea.</summary>
    private static IEnumerable<Activity> LabelChanges(ChangeTracker tracker, Guid? actorId, DateTime now)
    {
        var tasks = tracker.Entries<TaskItem>().ToDictionary(e => e.Entity.Id);

        return tracker.Entries<TaskLabel>()
            .Where(e => e.State is EntityState.Added or EntityState.Deleted)
            .GroupBy(e => e.Entity.TaskId)
            // Una tarea recién creada ya queda registrada con su "created"; no duplicamos.
            .Where(g => tasks.TryGetValue(g.Key, out var task) && task.State != EntityState.Added)
            .Select(g => Activity.Create(
                tasks[g.Key].Entity.WorkspaceId, actorId, "task", g.Key, ActivityActions.Updated,
                JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["labels"] = new
                    {
                        added = g.Where(e => e.State == EntityState.Added).Select(e => e.Entity.LabelId),
                        removed = g.Where(e => e.State == EntityState.Deleted).Select(e => e.Entity.LabelId),
                    },
                }, Json),
                now));
    }

    private static string EntityType(object entity) => entity switch
    {
        TaskItem => "task",
        Project => "project",
        Comment => "comment",
        Label => "label",
        _ => entity.GetType().Name.ToLowerInvariant(),
    };

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
