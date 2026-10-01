using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Repositories;

namespace TaskFlow.Infrastructure.GitHub;

/// <summary>
/// Cliente de la API REST de GitHub. El host es fijo (BaseAddress se configura en DI y no depende de
/// ningún dato del usuario): lo único variable en la URL es owner/name, ya validado por RepositoryName.
/// </summary>
public sealed class GitHubClient(HttpClient http) : IGitHubClient
{
    public static readonly Uri BaseAddress = new("https://api.github.com/");

    public async Task<GitHubRepository?> GetRepositoryAsync(RepositoryName repository, CancellationToken ct)
    {
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, RepoPath(repository)), ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        EnsureSuccess(response);

        using var json = await ReadJsonAsync(response, ct);
        var root = json.RootElement;
        return new GitHubRepository(
            root.GetProperty("owner").GetProperty("login").GetString()!,
            root.GetProperty("name").GetString()!,
            root.GetProperty("default_branch").GetString()!,
            root.GetProperty("private").GetBoolean());
    }

    public async Task<GitHubCommitList> ListCommitsAsync(
        RepositoryName repository, string branch, string? etag, int count, CancellationToken ct)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get, $"{RepoPath(repository)}/commits?sha={Uri.EscapeDataString(branch)}&per_page={count}");
        // Consulta condicional: si nada cambió, GitHub responde 304 y NO descuenta del límite de requests.
        if (etag is not null && EntityTagHeaderValue.TryParse(etag, out var tag))
            request.Headers.IfNoneMatch.Add(tag);

        using var response = await SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotModified)
            return new GitHubCommitList(true, etag, []);
        // 404 = rama o repo borrado; 409 = repositorio vacío. En ambos casos no hay commits que traer.
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict)
            return new GitHubCommitList(false, null, []);
        EnsureSuccess(response);

        using var json = await ReadJsonAsync(response, ct);
        var shas = json.RootElement.EnumerateArray().Select(c => c.GetProperty("sha").GetString()!).ToList();
        return new GitHubCommitList(false, response.Headers.ETag?.ToString(), shas);
    }

    public async Task<GitHubCommit?> GetCommitAsync(RepositoryName repository, string sha, CancellationToken ct)
    {
        using var response = await SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"{RepoPath(repository)}/commits/{Uri.EscapeDataString(sha)}"), ct);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity)
            return null;
        EnsureSuccess(response);

        using var json = await ReadJsonAsync(response, ct);
        var root = json.RootElement;
        var commit = root.GetProperty("commit");
        var gitAuthor = commit.GetProperty("author");
        // "author" (la cuenta de GitHub) es null cuando el email del commit no está asociado a ningún usuario.
        var account = root.TryGetProperty("author", out var a) && a.ValueKind == JsonValueKind.Object ? a : (JsonElement?)null;

        var files = root.TryGetProperty("files", out var f) && f.ValueKind == JsonValueKind.Array
            ? f.EnumerateArray().Select(x => new ChangedFile(
                x.GetProperty("filename").GetString()!,
                x.GetProperty("additions").GetInt32(),
                x.GetProperty("deletions").GetInt32())).ToList()
            : [];

        return new GitHubCommit(
            root.GetProperty("sha").GetString()!,
            commit.GetProperty("message").GetString() ?? string.Empty,
            gitAuthor.GetProperty("name").GetString() ?? string.Empty,
            account?.GetProperty("login").GetString(),
            SafeAvatarUrl(account?.GetProperty("avatar_url").GetString()),
            commit.GetProperty("committer").GetProperty("date").GetDateTimeOffset().UtcDateTime,
            files);
    }

    private static string RepoPath(RepositoryName r) => $"repos/{Uri.EscapeDataString(r.Owner)}/{Uri.EscapeDataString(r.Name)}";

    /// <summary>
    /// El avatar termina en un &lt;img src&gt; del navegador de otros usuarios: solo se acepta si es https
    /// y del dominio de avatares de GitHub.
    /// </summary>
    private static string? SafeAvatarUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            ? uri.ToString()
            : null;

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new GitHubUnavailableException("No se pudo conectar con GitHub.", inner: ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new GitHubUnavailableException("GitHub tardó demasiado en responder.", inner: ex);
        }
        finally
        {
            request.Dispose();
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        // Límite de requests: GitHub responde 403 o 429 con X-RateLimit-Remaining: 0 y la hora de reinicio.
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            var exhausted = response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.FirstOrDefault() == "0";
            if (exhausted || response.Headers.RetryAfter is not null)
            {
                DateTimeOffset? retryAt =
                    response.Headers.TryGetValues("X-RateLimit-Reset", out var reset) && long.TryParse(reset.FirstOrDefault(), out var epoch)
                        ? DateTimeOffset.FromUnixTimeSeconds(epoch)
                        : null;
                // En UTC: el servidor no sabe en qué zona horaria está quien lee el mensaje.
                var when = retryAt is { } t ? $" Se puede reintentar a las {t.UtcDateTime:HH:mm} UTC." : string.Empty;
                throw new GitHubUnavailableException($"Se alcanzó el límite de consultas a GitHub.{when}", retryAt);
            }
        }

        throw new GitHubUnavailableException($"GitHub respondió {(int)response.StatusCode}.");
    }
}
