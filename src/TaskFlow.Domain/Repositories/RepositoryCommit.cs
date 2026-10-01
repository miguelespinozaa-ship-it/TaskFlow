using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Repositories;

/// <summary>Commit importado de GitHub. Inmutable: es una copia de lo que hay en el repositorio.</summary>
public sealed class RepositoryCommit : Entity, ITenantEntity
{
    public const int MessageMaxLength = 10_000;
    public const int MaxFolders = 50;

    public Guid WorkspaceId { get; set; }
    public Guid ProjectId { get; private init; }
    public string Sha { get; private init; } = null!;

    /// <summary>El mensaje completo: es la explicación del cambio, escrita por quien lo hizo.</summary>
    public string Message { get; private init; } = null!;

    public string AuthorName { get; private init; } = null!;
    public string? AuthorLogin { get; private init; }
    public string? AuthorAvatarUrl { get; private init; }
    public DateTime CommittedAt { get; private init; }
    public int Additions { get; private init; }
    public int Deletions { get; private init; }
    public int FilesChanged { get; private init; }
    public IReadOnlyList<FolderChange> Folders { get; private init; } = [];

    private RepositoryCommit() { } // EF Core

    public static RepositoryCommit Create(
        RepositoryLink link,
        string sha,
        string message,
        string authorName,
        string? authorLogin,
        string? authorAvatarUrl,
        DateTime committedAt,
        IReadOnlyCollection<ChangedFile> files)
    {
        if (sha.Length != 40 || !sha.All(char.IsAsciiHexDigit))
            throw new DomainException("SHA de commit inválido.");

        var folders = FolderSummary.From(files);
        return new RepositoryCommit
        {
            WorkspaceId = link.WorkspaceId,
            ProjectId = link.ProjectId,
            Sha = sha.ToLowerInvariant(),
            Message = message.Length > MessageMaxLength ? message[..MessageMaxLength] : message,
            AuthorName = string.IsNullOrWhiteSpace(authorName) ? "Desconocido" : authorName.Trim(),
            AuthorLogin = authorLogin,
            AuthorAvatarUrl = authorAvatarUrl,
            CommittedAt = committedAt,
            Additions = files.Sum(f => f.Additions),
            Deletions = files.Sum(f => f.Deletions),
            FilesChanged = files.Count,
            // Un commit gigante (un rename masivo) puede tocar cientos de carpetas: se guardan las principales.
            Folders = [.. folders.Take(MaxFolders)],
        };
    }
}
