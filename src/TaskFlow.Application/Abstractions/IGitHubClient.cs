using TaskFlow.Application.Common.Paging;
using TaskFlow.Application.Repositories;
using TaskFlow.Domain.Repositories;

namespace TaskFlow.Application.Abstractions;

/// <summary>Lo mínimo que la app necesita de GitHub. La implementación real habla HTTP; los tests usan una falsa.</summary>
public interface IGitHubClient
{
    /// <summary>Null si el repositorio no existe o no es accesible.</summary>
    Task<GitHubRepository?> GetRepositoryAsync(RepositoryName repository, CancellationToken ct);

    /// <summary>Últimos commits de la rama, del más nuevo al más viejo. Con <paramref name="etag"/> vigente devuelve NotModified.</summary>
    Task<GitHubCommitList> ListCommitsAsync(RepositoryName repository, string branch, string? etag, int count, CancellationToken ct);

    /// <summary>Detalle de un commit, con los archivos que tocó. Null si no existe.</summary>
    Task<GitHubCommit?> GetCommitAsync(RepositoryName repository, string sha, CancellationToken ct);
}

public sealed record GitHubRepository(string Owner, string Name, string DefaultBranch, bool IsPrivate);

public sealed record GitHubCommitList(bool NotModified, string? ETag, IReadOnlyList<string> Shas);

public sealed record GitHubCommit(
    string Sha,
    string Message,
    string AuthorName,
    string? AuthorLogin,
    string? AuthorAvatarUrl,
    DateTime CommittedAt,
    IReadOnlyList<ChangedFile> Files);

/// <summary>GitHub no respondió, o se agotó el límite de requests. No es un error del usuario: se reintenta más tarde.</summary>
public sealed class GitHubUnavailableException(string message, DateTimeOffset? retryAt = null, Exception? inner = null)
    : Exception(message, inner)
{
    public DateTimeOffset? RetryAt { get; } = retryAt;
}

public interface IRepositoryLinkRepository
{
    void Add(RepositoryLink link);
    void Remove(RepositoryLink link);
    void AddCommit(RepositoryCommit commit);
    Task<RepositoryLink?> GetByProjectAsync(Guid projectId, CancellationToken ct);

    /// <summary>De <paramref name="shas"/>, cuáles ya están importados en el proyecto.</summary>
    Task<HashSet<string>> ExistingShasAsync(Guid projectId, IReadOnlyCollection<string> shas, CancellationToken ct);

    Task<int> DeleteCommitsAsync(Guid projectId, CancellationToken ct);
    Task<IReadOnlyList<CommitDto>> ListCommitsAsync(Guid projectId, Cursor? after, int take, CancellationToken ct);

    /// <summary>Todos los enlaces de TODOS los workspaces. Solo para el sincronizador de fondo (contexto de sistema).</summary>
    Task<IReadOnlyList<(Guid ProjectId, Guid WorkspaceId)>> ListAllForSyncAsync(CancellationToken ct);
}
