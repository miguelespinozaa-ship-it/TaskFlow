using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Tasks;

public sealed class Comment : Entity, ITenantEntity, ISoftDeletable, IAuditable
{
    public const int BodyMaxLength = 10_000;

    public Guid WorkspaceId { get; set; }
    public Guid TaskId { get; private init; }
    public Guid AuthorId { get; private init; }
    public string Body { get; private set; } = null!;
    public DateTime CreatedAt { get; private init; }
    public DateTime? EditedAt { get; private set; }
    public DateTime? DeletedAt { get; private set; }

    private Comment() { } // EF Core

    public static Comment Create(TaskItem task, Guid authorId, string body, DateTime utcNow)
    {
        if (task.DeletedAt is not null)
            throw new DomainException("No se puede comentar una tarea eliminada.");
        if (authorId == Guid.Empty)
            throw new DomainException("El comentario necesita autor.");

        var comment = new Comment { WorkspaceId = task.WorkspaceId, TaskId = task.Id, AuthorId = authorId, CreatedAt = utcNow };
        comment.SetBody(body);
        return comment;
    }

    public void Edit(Guid editorId, string body, DateTime utcNow)
    {
        // Solo el autor edita: un admin puede borrar un comentario ajeno, pero no poner palabras en su boca.
        if (editorId != AuthorId)
            throw new ForbiddenDomainException("Solo el autor puede editar el comentario.");
        SetBody(body);
        EditedAt = utcNow;
    }

    public void MarkDeleted(DateTime utcNow)
    {
        DeletedAt ??= utcNow;
    }

    private void SetBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new DomainException("El comentario no puede estar vacío.");
        body = body.Trim();
        if (body.Length > BodyMaxLength)
            throw new DomainException($"El comentario no puede superar {BodyMaxLength} caracteres.");
        Body = body;
    }
}
