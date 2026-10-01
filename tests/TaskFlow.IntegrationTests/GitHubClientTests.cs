using System.Net;
using System.Text;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Repositories;
using TaskFlow.Infrastructure.GitHub;

namespace TaskFlow.IntegrationTests;

/// <summary>
/// Prueba el cliente real de GitHub contra respuestas HTTP simuladas (misma forma que la API v3):
/// qué URL pide, qué headers manda y cómo interpreta cada respuesta. No sale a la red.
/// </summary>
public sealed class GitHubClientTests
{
    private static readonly RepositoryName Repo = new("acme", "web");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static (GitHubClient Client, StubHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        return (new GitHubClient(new HttpClient(handler) { BaseAddress = GitHubClient.BaseAddress }), handler);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task GetRepository_lee_rama_por_defecto_y_visibilidad()
    {
        var (client, handler) = Create(_ => Json("""
            { "name": "Web", "private": false, "default_branch": "develop", "owner": { "login": "Acme" } }
            """));

        var repo = await client.GetRepositoryAsync(Repo, Ct);

        repo.ShouldBe(new GitHubRepository("Acme", "Web", "develop", false));
        handler.Requests.Single().RequestUri!.ToString().ShouldBe("https://api.github.com/repos/acme/web");
    }

    [Fact]
    public async Task GetRepository_404_es_null()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        (await client.GetRepositoryAsync(Repo, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task ListCommits_manda_el_ETag_y_un_304_es_NotModified()
    {
        var (client, handler) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotModified));

        var list = await client.ListCommitsAsync(Repo, "main", "W/\"abc123\"", 30, Ct);

        list.NotModified.ShouldBeTrue();
        list.ETag.ShouldBe("W/\"abc123\"");
        var request = handler.Requests.Single();
        request.Headers.IfNoneMatch.Single().ToString().ShouldBe("W/\"abc123\"");
        request.RequestUri!.PathAndQuery.ShouldBe("/repos/acme/web/commits?sha=main&per_page=30");
    }

    [Fact]
    public async Task ListCommits_devuelve_los_sha_y_el_ETag_nuevo()
    {
        var (client, _) = Create(_ =>
        {
            var response = Json("""[ { "sha": "aaa" }, { "sha": "bbb" } ]""");
            response.Headers.TryAddWithoutValidation("ETag", "W/\"nuevo\"");
            return response;
        });

        var list = await client.ListCommitsAsync(Repo, "main", null, 30, Ct);

        list.NotModified.ShouldBeFalse();
        list.Shas.ShouldBe(["aaa", "bbb"]);
        list.ETag.ShouldBe("W/\"nuevo\"");
    }

    [Fact]
    public async Task ListCommits_escapa_el_nombre_de_la_rama()
    {
        var (client, handler) = Create(_ => Json("[]"));

        await client.ListCommitsAsync(Repo, "feature/x&per_page=1000", null, 30, Ct);

        // La rama viene de GitHub, pero igual se escapa: no puede inyectar parámetros en la query.
        handler.Requests.Single().RequestUri!.Query.ShouldBe("?sha=feature%2Fx%26per_page%3D1000&per_page=30");
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]   // repositorio vacío
    [InlineData(HttpStatusCode.NotFound)]   // rama borrada
    public async Task ListCommits_en_repo_vacio_o_rama_inexistente_es_lista_vacia(HttpStatusCode status)
    {
        var (client, _) = Create(_ => new HttpResponseMessage(status));

        var list = await client.ListCommitsAsync(Repo, "main", null, 30, Ct);

        list.NotModified.ShouldBeFalse();
        list.Shas.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetCommit_lee_mensaje_autor_fecha_y_archivos()
    {
        var (client, _) = Create(_ => Json("""
            {
              "sha": "0123456789abcdef0123456789abcdef01234567",
              "commit": {
                "message": "feat: algo\n\nDetalle.",
                "author": { "name": "Ana Dev", "date": "2026-09-01T10:00:00Z" },
                "committer": { "name": "GitHub", "date": "2026-09-01T12:30:00+02:00" }
              },
              "author": { "login": "anadev", "avatar_url": "https://avatars.githubusercontent.com/u/1?v=4" },
              "files": [
                { "filename": "src/a.cs", "additions": 10, "deletions": 2 },
                { "filename": "README.md", "additions": 1, "deletions": 0 }
              ]
            }
            """));

        var commit = await client.GetCommitAsync(Repo, "0123456789abcdef0123456789abcdef01234567", Ct);

        commit.ShouldNotBeNull();
        commit.Message.ShouldBe("feat: algo\n\nDetalle.");
        commit.AuthorName.ShouldBe("Ana Dev");
        commit.AuthorLogin.ShouldBe("anadev");
        commit.AuthorAvatarUrl.ShouldBe("https://avatars.githubusercontent.com/u/1?v=4");
        commit.CommittedAt.ShouldBe(new DateTime(2026, 9, 1, 10, 30, 0, DateTimeKind.Utc)); // normalizado a UTC
        commit.Files.ShouldBe([new ChangedFile("src/a.cs", 10, 2), new ChangedFile("README.md", 1, 0)]);
    }

    [Theory]
    [InlineData("null")]                                                   // commit sin cuenta de GitHub asociada
    [InlineData("""{ "login": "x", "avatar_url": "http://avatars.githubusercontent.com/u/1" }""")]   // no https
    [InlineData("""{ "login": "x", "avatar_url": "https://evil.example.com/track.png" }""")]          // otro dominio
    [InlineData("""{ "login": "x", "avatar_url": "javascript:alert(1)" }""")]
    public async Task GetCommit_solo_acepta_avatares_https_del_dominio_de_GitHub(string author)
    {
        var (client, _) = Create(_ => Json($$"""
            {
              "sha": "0123456789abcdef0123456789abcdef01234567",
              "commit": { "message": "x", "author": { "name": "A", "date": "2026-09-01T10:00:00Z" },
                          "committer": { "name": "A", "date": "2026-09-01T10:00:00Z" } },
              "author": {{author}},
              "files": []
            }
            """));

        var commit = await client.GetCommitAsync(Repo, "0123456789abcdef0123456789abcdef01234567", Ct);

        commit!.AuthorAvatarUrl.ShouldBeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Limite_de_requests_agotado_es_GitHubUnavailable_con_hora_de_reintento(HttpStatusCode status)
    {
        var (client, _) = Create(_ =>
        {
            var response = Json("""{ "message": "API rate limit exceeded" }""", status);
            response.Headers.Add("X-RateLimit-Remaining", "0");
            response.Headers.Add("X-RateLimit-Reset", "1790800000");
            return response;
        });

        var ex = await Should.ThrowAsync<GitHubUnavailableException>(() => client.GetRepositoryAsync(Repo, Ct));

        ex.RetryAt.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1790800000));
        ex.Message.ShouldContain("límite");
    }

    [Fact]
    public async Task Error_5xx_y_fallo_de_red_son_GitHubUnavailable()
    {
        var (serverError, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var (networkError, _) = Create(_ => throw new HttpRequestException("sin red"));

        await Should.ThrowAsync<GitHubUnavailableException>(() => serverError.GetRepositoryAsync(Repo, Ct));
        await Should.ThrowAsync<GitHubUnavailableException>(() => networkError.GetRepositoryAsync(Repo, Ct));
    }
}
