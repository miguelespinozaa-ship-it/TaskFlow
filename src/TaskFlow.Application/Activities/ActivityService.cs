using System.Text.Json;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common.Paging;

namespace TaskFlow.Application.Activities;

public sealed record ActivityDto(
    Guid Id,
    Guid? ActorId,
    string? ActorName,
    string EntityType,
    Guid EntityId,
    string Action,
    JsonElement Changes,
    DateTime CreatedAt);

public interface IActivityService
{
    Task<CursorPage<ActivityDto>> ListAsync(Guid? entityId, string? cursor, int? pageSize, CancellationToken ct);
}

public sealed class ActivityService(IActivityRepository activities) : IActivityService
{
    public async Task<CursorPage<ActivityDto>> ListAsync(Guid? entityId, string? cursor, int? pageSize, CancellationToken ct)
    {
        var size = Cursor.ClampPageSize(pageSize);
        var items = await activities.ListAsync(entityId, Cursor.Decode(cursor), size + 1, ct);
        if (items.Count <= size)
            return new CursorPage<ActivityDto>(items, null);

        var page = items.Take(size).ToList();
        return new CursorPage<ActivityDto>(page, new Cursor(page[^1].CreatedAt, page[^1].Id).Encode());
    }
}
