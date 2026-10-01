using System.Collections.Concurrent;
using System.Security.Cryptography;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Repositories;

namespace TaskFlow.IntegrationTests.Infrastructure;

/// <summary>GitHub en memoria. Cada test crea sus propios repos (nombres únicos), así que no comparten estado.</summary>
public sealed class FakeGitHubClient : IGitHubClient
{
    private readonly ConcurrentDictionary<string, FakeRepo> _repos = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Simula GitHub caído / sin cuota para el repo indicado (clave: "owner/name").</summary>
    public ConcurrentDictionary<string, bool> Unavailable { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>El repo existe, pero falla al pedir sus commits (cuota agotada a mitad de camino).</summary>
    public ConcurrentDictionary<string, bool> ImportUnavailable { get; } = new(StringComparer.OrdinalIgnoreCase);

    public FakeRepo AddRepo(bool isPrivate = false, string branch = "main")
    {
        var repo = new FakeRepo("acme", $"repo-{Guid.NewGuid():N}", branch, isPrivate);
        _repos[repo.FullName] = repo;
        return repo;
    }

    public Task<GitHubRepository?> GetRepositoryAsync(RepositoryName repository, CancellationToken ct)
    {
        ThrowIfUnavailable(repository);
        return Task.FromResult(_repos.TryGetValue(repository.FullName, out var r)
            ? new GitHubRepository(r.Owner, r.Name, r.Branch, r.IsPrivate)
            : null);
    }

    public Task<GitHubCommitList> ListCommitsAsync(RepositoryName repository, string branch, string? etag, int count, CancellationToken ct)
    {
        ThrowIfUnavailable(repository, import: true);
        var repo = _repos[repository.FullName];
        Interlocked.Increment(ref repo.ListCalls);

        var current = $"\"v{repo.Commits.Count}\"";
        if (etag == current)
        {
            Interlocked.Increment(ref repo.NotModifiedResponses);
            return Task.FromResult(new GitHubCommitList(true, etag, []));
        }
        return Task.FromResult(new GitHubCommitList(false, current, [.. repo.Commits.Take(count).Select(c => c.Sha)]));
    }

    public Task<GitHubCommit?> GetCommitAsync(RepositoryName repository, string sha, CancellationToken ct)
    {
        ThrowIfUnavailable(repository, import: true);
        var repo = _repos[repository.FullName];
        Interlocked.Increment(ref repo.DetailCalls);
        return Task.FromResult(repo.Commits.FirstOrDefault(c => c.Sha == sha));
    }

    private void ThrowIfUnavailable(RepositoryName repository, bool import = false)
    {
        var importDown = import && ImportUnavailable.TryGetValue(repository.FullName, out var failing) && failing;
        if (importDown || (Unavailable.TryGetValue(repository.FullName, out var down) && down))
            throw new GitHubUnavailableException("Se alcanzó el límite de consultas a GitHub.");
    }
}

public sealed class FakeRepo(string owner, string name, string branch, bool isPrivate)
{
    private DateTime _clock = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

    public string Owner { get; } = owner;
    public string Name { get; } = name;
    public string Branch { get; } = branch;
    public bool IsPrivate { get; } = isPrivate;
    public string FullName => $"{Owner}/{Name}";

    /// <summary>Del más nuevo al más viejo, como los devuelve GitHub.</summary>
    public List<GitHubCommit> Commits { get; } = [];

    public int ListCalls;
    public int DetailCalls;
    public int NotModifiedResponses;

    public GitHubCommit Push(string message, params ChangedFile[] files)
    {
        _clock = _clock.AddMinutes(10);
        var commit = new GitHubCommit(
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(20)), message, "Ana Dev", "anadev",
            "https://avatars.githubusercontent.com/u/1?v=4", _clock,
            files.Length > 0 ? files : [new ChangedFile("README.md", 1, 0)]);
        Commits.Insert(0, commit);
        return commit;
    }
}
