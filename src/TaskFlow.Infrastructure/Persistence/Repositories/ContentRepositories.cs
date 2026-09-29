using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Activities;
using TaskFlow.Application.Comments;
using TaskFlow.Application.Common.Paging;
using TaskFlow.Application.Labels;
using TaskFlow.Domain.Labels;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Infrastructure.Persistence.Repositories;

internal sealed class LabelRepository(AppDbContext db) : ILabelRepository
{
    public void Add(Label label) => db.Labels.Add(label);

    public void Remove(Label label) => db.Labels.Remove(label);

    public Task<Label?> GetByIdAsync(Guid id, CancellationToken ct) => db.Labels.FirstOrDefaultAsync(l => l.Id == id, ct);

    public async Task<IReadOnlyList<LabelDto>> ListAsync(CancellationToken ct) =>
        await db.Labels.AsNoTracking().OrderBy(l => l.Name).Select(l => new LabelDto(l.Id, l.Name, l.Color)).ToListAsync(ct);

    public Task<bool> NameExistsAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        var lower = name.ToLowerInvariant();
        return db.Labels.AnyAsync(l => l.Name.ToLower() == lower && l.Id != exceptId, ct);
    }

    public Task<int> CountExistingAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        db.Labels.CountAsync(l => ids.Contains(l.Id), ct);
}

internal sealed class CommentRepository(AppDbContext db) : ICommentRepository
{
    public void Add(Comment comment) => db.Comments.Add(comment);

    public void Remove(Comment comment) => db.Comments.Remove(comment);

    public Task<Comment?> GetByIdAsync(Guid id, CancellationToken ct) => db.Comments.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<CommentDto?> GetDtoAsync(Guid id, CancellationToken ct) =>
        WithAuthor(db.Comments.Where(c => c.Id == id)).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<CommentDto>> ListByTaskAsync(Guid taskId, CancellationToken ct) =>
        await WithAuthor(db.Comments.Where(c => c.TaskId == taskId).OrderBy(c => c.CreatedAt)).ToListAsync(ct);

    // Los filtros van ANTES de proyectar: EF no puede traducir un Where sobre el constructor de un record.
    private IQueryable<CommentDto> WithAuthor(IQueryable<Comment> comments) =>
        comments.AsNoTracking()
            .Join(db.Users, c => c.AuthorId, u => u.Id,
                (c, u) => new CommentDto(c.Id, c.TaskId, c.AuthorId, u.DisplayName, c.Body, c.CreatedAt, c.EditedAt));
}

internal sealed class ActivityRepository(AppDbContext db) : IActivityRepository
{
    public async Task<IReadOnlyList<ActivityDto>> ListAsync(Guid? entityId, Cursor? after, int take, CancellationToken ct)
    {
        var activities = db.Activities.AsNoTracking();
        if (entityId is Guid id) activities = activities.Where(a => a.EntityId == id);
        if (after is Cursor cursor)
            activities = activities.Where(a => EF.Functions.LessThan(
                ValueTuple.Create(a.CreatedAt, a.Id), ValueTuple.Create(cursor.CreatedAt, cursor.Id)));

        // LEFT JOIN a users: la actividad del sistema (o de un usuario borrado) no tiene actor.
        var rows = await activities
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Take(take)
            .GroupJoin(db.Users, a => a.ActorId, u => (Guid?)u.Id, (a, users) => new { a, users })
            .SelectMany(x => x.users.DefaultIfEmpty(), (x, u) => new
            {
                x.a.Id, x.a.ActorId, ActorName = u == null ? null : u.DisplayName,
                x.a.EntityType, x.a.EntityId, x.a.Action, x.a.Changes, x.a.CreatedAt,
            })
            .ToListAsync(ct);

        return [.. rows
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Select(r => new ActivityDto(r.Id, r.ActorId, r.ActorName, r.EntityType, r.EntityId, r.Action,
                JsonDocument.Parse(r.Changes).RootElement.Clone(), r.CreatedAt))];
    }
}
