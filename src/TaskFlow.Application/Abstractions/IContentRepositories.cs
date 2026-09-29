using TaskFlow.Application.Activities;
using TaskFlow.Application.Comments;
using TaskFlow.Application.Common.Paging;
using TaskFlow.Application.Labels;
using TaskFlow.Domain.Labels;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Abstractions;

public interface ILabelRepository
{
    void Add(Label label);
    void Remove(Label label);
    Task<Label?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<LabelDto>> ListAsync(CancellationToken ct);
    Task<bool> NameExistsAsync(string name, Guid? exceptId, CancellationToken ct);

    /// <summary>Cuántas de <paramref name="ids"/> existen en el workspace activo.</summary>
    Task<int> CountExistingAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}

public interface ICommentRepository
{
    void Add(Comment comment);
    void Remove(Comment comment);
    Task<Comment?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<CommentDto?> GetDtoAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<CommentDto>> ListByTaskAsync(Guid taskId, CancellationToken ct);
}

public interface IActivityRepository
{
    Task<IReadOnlyList<ActivityDto>> ListAsync(Guid? entityId, Cursor? after, int take, CancellationToken ct);
}
