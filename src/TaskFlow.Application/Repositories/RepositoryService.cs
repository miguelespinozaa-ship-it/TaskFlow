using FluentValidation;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Application.Common.Exceptions;
using TaskFlow.Application.Common.Paging;
using TaskFlow.Domain.Repositories;

namespace TaskFlow.Application.Repositories;

public sealed record LinkRepositoryRequest(string Repository);

public sealed record RepositoryDto(
    Guid ProjectId,
    string Owner,
    string Name,
    string FullName,
    string HtmlUrl,
    string DefaultBranch,
    DateTime? LastSyncedAt,
    string? LastSyncError)
{
    public static RepositoryDto From(RepositoryLink l) => new(
        l.ProjectId, l.Owner, l.Name, l.Repository.FullName, $"https://github.com/{l.Owner}/{l.Name}",
        l.DefaultBranch, l.LastSyncedAt, l.LastSyncError);
}

public sealed record CommitDto(
    Guid Id,
    string Sha,
    string Title,
    string? Body,
    string AuthorName,
    string? AuthorLogin,
    string? AuthorAvatarUrl,
    DateTime CommittedAt,
    string HtmlUrl,
    int Additions,
    int Deletions,
    int FilesChanged,
    IReadOnlyList<FolderChange> Folders);

public sealed record SyncResult(int Imported, RepositoryDto Repository);

public sealed class LinkRepositoryRequestValidator : AbstractValidator<LinkRepositoryRequest>
{
    public LinkRepositoryRequestValidator() =>
        RuleFor(x => x.Repository)
            .NotEmpty()
            .Must(r => RepositoryName.TryParse(r, out _))
            .WithMessage("Usá el formato owner/nombre o la URL del repositorio en GitHub.");
}

public interface IRepositoryService
{
    Task<RepositoryDto?> GetAsync(Guid projectId, CancellationToken ct);
    Task<SyncResult> LinkAsync(Guid projectId, LinkRepositoryRequest request, CancellationToken ct);
    Task UnlinkAsync(Guid projectId, CancellationToken ct);
    Task<SyncResult> SyncAsync(Guid projectId, CancellationToken ct);
    Task<CursorPage<CommitDto>> ListCommitsAsync(Guid projectId, string? cursor, int? pageSize, CancellationToken ct);
}

public sealed class RepositoryService(
    IRepositoryLinkRepository links,
    IProjectRepository projects,
    IGitHubClient github,
    IUnitOfWork unitOfWork,
    IRequestValidator validator,
    TimeProvider clock) : IRepositoryService
{
    /// <summary>Cuántos commits recientes se miran en cada sincronización.</summary>
    public const int ListSize = 30;

    /// <summary>
    /// Tope de commits importados por sincronización. Cada commit cuesta un request a GitHub (hay que pedir
    /// sus archivos), y sin token el límite es de 60 por hora: con este tope una sincronización nunca se lo come entero.
    /// </summary>
    public const int MaxCommitsPerSync = 15;

    private const string NotAccessible = "No encontramos ese repositorio. Tiene que existir y ser público.";

    public async Task<RepositoryDto?> GetAsync(Guid projectId, CancellationToken ct)
    {
        await RequireProjectAsync(projectId, ct);
        return await links.GetByProjectAsync(projectId, ct) is { } link ? RepositoryDto.From(link) : null;
    }

    public async Task<SyncResult> LinkAsync(Guid projectId, LinkRepositoryRequest request, CancellationToken ct)
    {
        await validator.ValidateAsync(request, ct);
        RepositoryName.TryParse(request.Repository, out var name);

        var project = await projects.GetByIdAsync(projectId, ct) ?? throw new NotFoundException("Proyecto no encontrado.");

        var remote = await github.GetRepositoryAsync(name, ct);
        // Solo repos públicos: si el servidor tiene un token configurado, ese token podría ver repos privados
        // de su dueño, y cualquier workspace podría enlazarlos y leer sus commits. Mismo mensaje para
        // "no existe" y "es privado": no confirmamos que un repo privado existe.
        if (remote is null || remote.IsPrivate)
            throw Validation.Fail(nameof(LinkRepositoryRequest.Repository), NotAccessible);

        // Cambiar de repositorio = desconectar el anterior (con sus commits) y conectar el nuevo.
        if (await links.GetByProjectAsync(projectId, ct) is { } previous)
        {
            await links.DeleteCommitsAsync(projectId, ct);
            links.Remove(previous);
            await unitOfWork.SaveChangesAsync(ct);
        }

        // Owner/Name como los devuelve GitHub (respeta mayúsculas y renombres).
        var link = RepositoryLink.Create(
            project, new RepositoryName(remote.Owner, remote.Name), remote.DefaultBranch, clock.GetUtcNow().UtcDateTime);
        links.Add(link);
        await unitOfWork.SaveChangesAsync(ct);

        // El enlace ya quedó guardado: si la primera importación falla (límite de GitHub), no se pierde;
        // el error queda visible en el enlace y se reintenta con "Sincronizar".
        try
        {
            return new SyncResult(await ImportAsync(link, ct), RepositoryDto.From(link));
        }
        catch (GitHubUnavailableException)
        {
            return new SyncResult(0, RepositoryDto.From(link));
        }
    }

    public async Task UnlinkAsync(Guid projectId, CancellationToken ct)
    {
        var link = await RequireLinkAsync(projectId, ct);
        await links.DeleteCommitsAsync(projectId, ct);
        links.Remove(link);
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<SyncResult> SyncAsync(Guid projectId, CancellationToken ct)
    {
        var link = await RequireLinkAsync(projectId, ct);
        return new SyncResult(await ImportAsync(link, ct), RepositoryDto.From(link));
    }

    public async Task<CursorPage<CommitDto>> ListCommitsAsync(Guid projectId, string? cursor, int? pageSize, CancellationToken ct)
    {
        await RequireProjectAsync(projectId, ct);
        var size = Cursor.ClampPageSize(pageSize);

        var items = await links.ListCommitsAsync(projectId, Cursor.Decode(cursor), size + 1, ct);
        if (items.Count <= size)
            return new CursorPage<CommitDto>(items, null);

        var page = items.Take(size).ToList();
        return new CursorPage<CommitDto>(page, new Cursor(page[^1].CommittedAt, page[^1].Id).Encode());
    }

    private async Task<int> ImportAsync(RepositoryLink link, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        try
        {
            var list = await github.ListCommitsAsync(link.Repository, link.DefaultBranch, link.ETag, ListSize, ct);
            if (list.NotModified)
            {
                link.MarkSynced(now, link.ETag);
                await unitOfWork.SaveChangesAsync(ct);
                return 0;
            }

            var known = await links.ExistingShasAsync(link.ProjectId, list.Shas, ct);
            var missing = list.Shas.Where(sha => !known.Contains(sha)).ToList();
            var batch = missing.Take(MaxCommitsPerSync).ToList();

            var imported = 0;
            foreach (var sha in batch)
            {
                if (await github.GetCommitAsync(link.Repository, sha, ct) is not { } commit)
                    continue;

                links.AddCommit(RepositoryCommit.Create(
                    link, commit.Sha, commit.Message, commit.AuthorName, commit.AuthorLogin, commit.AuthorAvatarUrl,
                    commit.CommittedAt, [.. commit.Files]));
                imported++;
            }

            // El ETag solo se guarda si quedamos al día. Si faltó importar (tope por sincronización), guardarlo
            // haría que la próxima consulta devuelva 304 "sin cambios" y esos commits no entrarían nunca.
            link.MarkSynced(now, batch.Count == missing.Count ? list.ETag : null);
            await unitOfWork.SaveChangesAsync(ct);
            return imported;
        }
        catch (GitHubUnavailableException ex)
        {
            // Lo importado hasta el fallo se conserva; el error queda a la vista en el enlace.
            link.MarkFailed(ex.Message);
            await unitOfWork.SaveChangesAsync(ct);
            throw;
        }
    }

    private async Task RequireProjectAsync(Guid projectId, CancellationToken ct)
    {
        // Proyecto de otro workspace → no pasa el filtro de tenant → 404.
        if (!await projects.ExistsAsync(projectId, ct))
            throw new NotFoundException("Proyecto no encontrado.");
    }

    private async Task<RepositoryLink> RequireLinkAsync(Guid projectId, CancellationToken ct)
    {
        await RequireProjectAsync(projectId, ct);
        return await links.GetByProjectAsync(projectId, ct)
            ?? throw new NotFoundException("El proyecto no tiene un repositorio conectado.");
    }
}
