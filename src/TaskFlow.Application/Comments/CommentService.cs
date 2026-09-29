using FluentValidation;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Application.Common.Exceptions;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Comments;

public sealed record CommentDto(
    Guid Id, Guid TaskId, Guid AuthorId, string AuthorName, string Body, DateTime CreatedAt, DateTime? EditedAt);

public sealed record SaveCommentRequest(string Body);

public sealed class SaveCommentRequestValidator : AbstractValidator<SaveCommentRequest>
{
    public SaveCommentRequestValidator() => RuleFor(x => x.Body).NotEmpty().MaximumLength(Comment.BodyMaxLength);
}

public interface ICommentService
{
    Task<IReadOnlyList<CommentDto>> ListAsync(Guid taskId, CancellationToken ct);
    Task<CommentDto> CreateAsync(Guid taskId, SaveCommentRequest request, CancellationToken ct);
    Task<CommentDto> EditAsync(Guid commentId, SaveCommentRequest request, CancellationToken ct);
    Task DeleteAsync(Guid commentId, CancellationToken ct);
}

public sealed class CommentService(
    ICommentRepository comments,
    ITaskRepository tasks,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IRequestValidator validator,
    TimeProvider clock) : ICommentService
{
    public async Task<IReadOnlyList<CommentDto>> ListAsync(Guid taskId, CancellationToken ct)
    {
        _ = await tasks.GetDtoAsync(taskId, ct) ?? throw new NotFoundException("Tarea no encontrada.");
        return await comments.ListByTaskAsync(taskId, ct);
    }

    public async Task<CommentDto> CreateAsync(Guid taskId, SaveCommentRequest request, CancellationToken ct)
    {
        await validator.ValidateAsync(request, ct);
        var task = await tasks.GetByIdAsync(taskId, ct) ?? throw new NotFoundException("Tarea no encontrada.");

        var comment = Comment.Create(task, currentUser.RequireUserId(), request.Body, clock.GetUtcNow().UtcDateTime);
        comments.Add(comment);
        await unitOfWork.SaveChangesAsync(ct);
        return await comments.GetDtoAsync(comment.Id, ct) ?? throw new InvalidOperationException();
    }

    public async Task<CommentDto> EditAsync(Guid commentId, SaveCommentRequest request, CancellationToken ct)
    {
        await validator.ValidateAsync(request, ct);
        var comment = await RequireAsync(commentId, ct);

        comment.Edit(currentUser.RequireUserId(), request.Body, clock.GetUtcNow().UtcDateTime);
        await unitOfWork.SaveChangesAsync(ct);
        return await comments.GetDtoAsync(comment.Id, ct) ?? throw new InvalidOperationException();
    }

    public async Task DeleteAsync(Guid commentId, CancellationToken ct)
    {
        var comment = await RequireAsync(commentId, ct);

        // Moderación: el autor borra lo suyo; Admin y Owner pueden borrar cualquiera.
        if (comment.AuthorId != currentUser.RequireUserId() && !currentUser.IsAdmin())
            throw new ForbiddenException("Solo el autor o un administrador pueden borrar el comentario.");

        comments.Remove(comment);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<Comment> RequireAsync(Guid id, CancellationToken ct) =>
        await comments.GetByIdAsync(id, ct) ?? throw new NotFoundException("Comentario no encontrado.");
}
