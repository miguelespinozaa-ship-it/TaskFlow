using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Application.Activities;
using TaskFlow.Application.Common.Paging;
using TaskFlow.Application.Repositories;
using TaskFlow.Domain.Repositories;
using TaskFlow.Domain.Workspaces;
using TaskFlow.Infrastructure.GitHub;
using TaskFlow.IntegrationTests.Infrastructure;

namespace TaskFlow.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class RepositoryTests(TaskFlowApiFactory factory) : ApiTestBase(factory)
{
    private async Task<(Session Owner, Guid ProjectId)> SetupAsync()
    {
        var owner = await RegisterAsync();
        return (owner, (await CreateProjectAsync(owner)).Id);
    }

    private static Task<HttpResponseMessage> LinkAsync(Session s, Guid projectId, string repository) =>
        s.Client.PutAsJsonAsync($"/api/v1/projects/{projectId}/repository", new { repository }, Json, Ct);

    private static Task<HttpResponseMessage> SyncAsync(Session s, Guid projectId) =>
        s.Client.PostAsync($"/api/v1/projects/{projectId}/repository/sync", null, Ct);

    private static async Task<List<CommitDto>> CommitsAsync(Session s, Guid projectId) =>
        (await s.Client.GetFromJsonAsync<CursorPage<CommitDto>>($"/api/v1/projects/{projectId}/commits?pageSize=100", Json, Ct))!.Items.ToList();

    [Fact]
    public async Task Conectar_importa_los_commits_con_su_explicacion_y_las_carpetas_que_tocaron()
    {
        var (owner, projectId) = await SetupAsync();
        var repo = Factory.GitHub.AddRepo();
        repo.Push("chore: initial commit");
        var pushed = repo.Push(
            "feat(api): add task endpoints\n\nExpone el CRUD de tareas y valida el título.\nCierra #12.",
            new ChangedFile("src/Api/Controllers/TasksController.cs", 80, 0),
            new ChangedFile("src/Api/Program.cs", 5, 2),
            new ChangedFile("tests/Api/TasksTests.cs", 40, 0),
            new ChangedFile("README.md", 3, 1));

        var result = await ReadAsync<SyncResult>(await LinkAsync(owner, projectId, repo.FullName));
        var commits = await CommitsAsync(owner, projectId);

        result.Imported.ShouldBe(2);
        result.Repository.FullName.ShouldBe(repo.FullName);
        result.Repository.DefaultBranch.ShouldBe("main");
        commits.Select(c => c.Title).ShouldBe(["feat(api): add task endpoints", "chore: initial commit"]);

        var commit = commits[0];
        commit.Sha.ShouldBe(pushed.Sha);
        commit.Body.ShouldBe("Expone el CRUD de tareas y valida el título.\nCierra #12.");
        commit.AuthorName.ShouldBe("Ana Dev");
        commit.HtmlUrl.ShouldBe($"https://github.com/{repo.FullName}/commit/{pushed.Sha}");
        (commit.FilesChanged, commit.Additions, commit.Deletions).ShouldBe((4, 128, 3));
        // Agrupado a 2 niveles y ordenado por cantidad de cambios; "" es la raíz del repo.
        commit.Folders.ShouldBe([
            new FolderChange("src/Api", 2, 85, 2),
            new FolderChange("tests/Api", 1, 40, 0),
            new FolderChange("", 1, 3, 1),
        ]);
        commits[1].Body.ShouldBeNull();
    }

    [Theory]
    [InlineData("https://github.com/{0}")]
    [InlineData("https://github.com/{0}.git")]
    [InlineData("github.com/{0}/tree/main/src")]
    [InlineData("git@github.com:{0}.git")]
    public async Task Acepta_la_URL_del_repositorio_en_sus_formas_habituales(string format)
    {
        var (owner, projectId) = await SetupAsync();
        var repo = Factory.GitHub.AddRepo();

        var result = await ReadAsync<SyncResult>(await LinkAsync(owner, projectId, string.Format(format, repo.FullName)));

        result.Repository.FullName.ShouldBe(repo.FullName);
    }

    [Theory]
    [InlineData("no-es-un-repo")]
    [InlineData("https://evil.example.com/acme/repo")]
    [InlineData("acme/../../orgs/secret")]
    [InlineData("acme/repo/extra")]
    public async Task Rechaza_entradas_que_no_son_un_repositorio_de_GitHub(string input)
    {
        var (owner, projectId) = await SetupAsync();

        var response = await LinkAsync(owner, projectId, input);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json, Ct))!.Errors.ShouldContainKey("Repository");
    }

    [Fact]
    public async Task Repositorio_inexistente_y_repositorio_privado_responden_igual()
    {
        var (owner, projectId) = await SetupAsync();
        var privado = Factory.GitHub.AddRepo(isPrivate: true);

        var missing = await LinkAsync(owner, projectId, "acme/no-existe");
        var hidden = await LinkAsync(owner, projectId, privado.FullName);

        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        hidden.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        // Mismo mensaje: no se confirma que un repositorio privado existe.
        var a = await missing.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json, Ct);
        var b = await hidden.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json, Ct);
        a!.Errors["Repository"].ShouldBe(b!.Errors["Repository"]);
        (await owner.Client.GetAsync($"/api/v1/projects/{projectId}/repository", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Sincronizar_trae_solo_los_commits_nuevos_y_es_idempotente()
    {
        var (owner, projectId) = await SetupAsync();
        var repo = Factory.GitHub.AddRepo();
        repo.Push("uno");
        await LinkAsync(owner, projectId, repo.FullName);
        repo.Push("dos");
        repo.Push("tres");

        var first = await ReadAsync<SyncResult>(await SyncAsync(owner, projectId));
        var second = await ReadAsync<SyncResult>(await SyncAsync(owner, projectId));

        first.Imported.ShouldBe(2);
        second.Imported.ShouldBe(0);
        (await CommitsAsync(owner, projectId)).Select(c => c.Title).ShouldBe(["tres", "dos", "uno"]);
        first.Repository.LastSyncedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Sin_cambios_usa_la_consulta_condicional_y_no_pide_el_detalle_de_ningun_commit()
    {
        var (owner, projectId) = await SetupAsync();
        var repo = Factory.GitHub.AddRepo();
        repo.Push("uno");
        await LinkAsync(owner, projectId, repo.FullName);
        var detailCallsAfterLink = repo.DetailCalls;

        await SyncAsync(owner, projectId);
        await SyncAsync(owner, projectId);

        // Las dos sincronizaciones mandaron el ETag guardado y GitHub respondió 304: cero costo de cuota.
        repo.NotModifiedResponses.ShouldBe(2);
        repo.DetailCalls.ShouldBe(detailCallsAfterLink);
    }

    [Fact]
    public async Task Con_mas_commits_pendientes_que_el_tope_los_importa_en_varias_sincronizaciones()
    {
        var (owner, projectId) = await SetupAsync();
        var repo = Factory.GitHub.AddRepo();
        for (var i = 1; i <= RepositoryService.MaxCommitsPerSync + 5; i++)
            repo.Push($"commit {i}");

        var link = await ReadAsync<SyncResult>(await LinkAsync(owner, projectId, repo.FullName));
        var sync = await ReadAsync<SyncResult>(await SyncAsync(owner, projectId));
        var again = await ReadAsync<SyncResult>(await SyncAsync(owner, projectId));

        link.Imported.ShouldBe(RepositoryService.MaxCommitsPerSync);
        // Si tras la primera tanda se hubiera guardado el ETag, esta consulta daría 304 y los 5 restantes no entrarían nunca.
        sync.Imported.ShouldBe(5);
        again.Imported.ShouldBe(0);
        (await CommitsAsync(owner, projectId)).Count.ShouldBe(RepositoryService.MaxCommitsPerSync + 5);
    }

    [Fact]
    public async Task Los_commits_se_paginan_por_cursor_sin_repetir()
    {
        var (owner, projectId) = await SetupAsync();
        var repo = Factory.GitHub.AddRepo();
        for (var i = 1; i <= 7; i++)
            repo.Push($"commit {i}");
        await LinkAsync(owner, projectId, repo.FullName);

        var page1 = await owner.Client.GetFromJsonAsync<CursorPage<CommitDto>>($"/api/v1/projects/{projectId}/commits?pageSize=5", Json, Ct);
        var page2 = await owner.Client.GetFromJsonAsync<CursorPage<CommitDto>>(
            $"/api/v1/projects/{projectId}/commits?pageSize=5&cursor={page1!.NextCursor}", Json, Ct);

        page1.Items.Select(c => c.Title).ShouldBe(["commit 7", "commit 6", "commit 5", "commit 4", "commit 3"]);
        page2!.Items.Select(c => c.Title).ShouldBe(["commit 2", "commit 1"]);
        page2.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task Si_GitHub_no_responde_devuelve_503_guarda_el_error_y_se_recupera_despues()
    {
        var (owner, projectId) = await SetupAsync();
        var repo = Factory.GitHub.AddRepo();
        repo.Push("uno");
        await LinkAsync(owner, projectId, repo.FullName);
        repo.Push("dos");

        Factory.GitHub.Unavailable[repo.FullName] = true;
        var failed = await SyncAsync(owner, projectId);
        var whileDown = await owner.Client.GetFromJsonAsync<RepositoryDto>($"/api/v1/projects/{projectId}/repository", Json, Ct);

        Factory.GitHub.Unavailable[repo.FullName] = false;
        var recovered = await ReadAsync<SyncResult>(await SyncAsync(owner, projectId));

        failed.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        whileDown!.LastSyncError.ShouldNotBeNullOrEmpty();
        (await CommitsAsync(owner, projectId)).Count.ShouldBe(2); // lo ya importado sigue visible durante la caída
        recovered.Imported.ShouldBe(1);
        recovered.Repository.LastSyncError.ShouldBeNull();
    }

    [Fact]
    public async Task Si_GitHub_falla_durante_la_primera_importacion_el_enlace_igual_queda_guardado()
    {
        var (owner, projectId) = await SetupAsync();
        var repo = Factory.GitHub.AddRepo();
        repo.Push("uno");

        Factory.GitHub.ImportUnavailable[repo.FullName] = true;
        var linked = await ReadAsync<SyncResult>(await LinkAsync(owner, projectId, repo.FullName));
        Factory.GitHub.ImportUnavailable[repo.FullName] = false;
        var retried = await ReadAsync<SyncResult>(await SyncAsync(owner, projectId));

        // Conectar no falla: el repositorio queda enlazado con el error a la vista, y "Sincronizar" lo completa.
        linked.Imported.ShouldBe(0);
        linked.Repository.LastSyncError.ShouldNotBeNullOrEmpty();
        retried.Imported.ShouldBe(1);
        retried.Repository.LastSyncError.ShouldBeNull();
    }

    [Fact]
    public async Task Desconectar_borra_el_enlace_y_sus_commits()
    {
        var (owner, projectId) = await SetupAsync();
        var repo = Factory.GitHub.AddRepo();
        repo.Push("uno");
        await LinkAsync(owner, projectId, repo.FullName);

        var response = await owner.Client.DeleteAsync($"/api/v1/projects/{projectId}/repository", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.Client.GetAsync($"/api/v1/projects/{projectId}/repository", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await CommitsAsync(owner, projectId)).ShouldBeEmpty();
        (await SyncAsync(owner, projectId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Cambiar_de_repositorio_reemplaza_los_commits_del_anterior()
    {
        var (owner, projectId) = await SetupAsync();
        var viejo = Factory.GitHub.AddRepo();
        viejo.Push("del repo viejo");
        var nuevo = Factory.GitHub.AddRepo(branch: "develop");
        nuevo.Push("del repo nuevo");
        await LinkAsync(owner, projectId, viejo.FullName);

        var result = await ReadAsync<SyncResult>(await LinkAsync(owner, projectId, nuevo.FullName));

        result.Repository.FullName.ShouldBe(nuevo.FullName);
        result.Repository.DefaultBranch.ShouldBe("develop");
        (await CommitsAsync(owner, projectId)).ShouldHaveSingleItem().Title.ShouldBe("del repo nuevo");
    }

    [Theory]
    [InlineData(WorkspaceRole.Admin, HttpStatusCode.OK, HttpStatusCode.OK)]
    [InlineData(WorkspaceRole.Member, HttpStatusCode.Forbidden, HttpStatusCode.OK)]
    [InlineData(WorkspaceRole.Viewer, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    public async Task Conectar_es_de_Admin_sincronizar_de_Member_y_leer_de_todos(
        WorkspaceRole role, HttpStatusCode expectedLink, HttpStatusCode expectedSync)
    {
        var (owner, projectId) = await SetupAsync();
        var repo = Factory.GitHub.AddRepo();
        repo.Push("uno");
        await LinkAsync(owner, projectId, repo.FullName);
        var actor = await RegisterAsync();
        await AddMemberAsync(owner, actor, role);
        await SwitchWorkspaceAsync(actor, owner.Auth.Workspace.Id);

        (await LinkAsync(actor, projectId, repo.FullName)).StatusCode.ShouldBe(expectedLink);
        (await SyncAsync(actor, projectId)).StatusCode.ShouldBe(expectedSync);
        (await CommitsAsync(actor, projectId)).ShouldHaveSingleItem();
        (await actor.Client.GetAsync($"/api/v1/projects/{projectId}/repository", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Otro_workspace_no_puede_ver_sincronizar_ni_desconectar_el_repositorio()
    {
        var (owner, projectId) = await SetupAsync();
        var repo = Factory.GitHub.AddRepo();
        repo.Push("secreto");
        await LinkAsync(owner, projectId, repo.FullName);
        var intruso = await RegisterAsync();

        (await intruso.Client.GetAsync($"/api/v1/projects/{projectId}/repository", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await intruso.Client.GetAsync($"/api/v1/projects/{projectId}/commits", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SyncAsync(intruso, projectId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await intruso.Client.DeleteAsync($"/api/v1/projects/{projectId}/repository", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CommitsAsync(owner, projectId)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task El_sincronizador_de_fondo_atiende_a_todos_los_workspaces_sin_mezclarlos()
    {
        var (a, projectA) = await SetupAsync();
        var (b, projectB) = await SetupAsync();
        var repoA = Factory.GitHub.AddRepo();
        var repoB = Factory.GitHub.AddRepo();
        await LinkAsync(a, projectA, repoA.FullName);
        await LinkAsync(b, projectB, repoB.FullName);
        repoA.Push("nuevo en A");
        repoB.Push("nuevo en B");

        var imported = await Factory.Services.GetRequiredService<RepositorySyncRunner>().RunOnceAsync(Ct);

        imported.ShouldBeGreaterThanOrEqualTo(2);
        (await CommitsAsync(a, projectA)).Select(c => c.Title).ShouldBe(["nuevo en A"]);
        (await CommitsAsync(b, projectB)).Select(c => c.Title).ShouldBe(["nuevo en B"]);
    }

    [Fact]
    public async Task El_sincronizador_de_fondo_saltea_los_proyectos_borrados()
    {
        var (owner, projectId) = await SetupAsync();
        var repo = Factory.GitHub.AddRepo();
        await LinkAsync(owner, projectId, repo.FullName);
        await owner.Client.DeleteAsync($"/api/v1/projects/{projectId}", Ct); // proyecto borrado (soft delete)
        repo.Push("después de borrar el proyecto");
        var listCalls = repo.ListCalls;

        await Factory.Services.GetRequiredService<RepositorySyncRunner>().RunOnceAsync(Ct);

        // El sincronizador saltea los enlaces de proyectos borrados: no consulta GitHub por ellos.
        repo.ListCalls.ShouldBe(listCalls);
    }

    [Fact]
    public async Task Conectar_y_desconectar_quedan_en_el_historial_pero_sincronizar_no()
    {
        var (owner, projectId) = await SetupAsync();
        var repo = Factory.GitHub.AddRepo();
        repo.Push("uno");

        await LinkAsync(owner, projectId, repo.FullName);
        await SyncAsync(owner, projectId);
        await SyncAsync(owner, projectId);
        await owner.Client.DeleteAsync($"/api/v1/projects/{projectId}/repository", Ct);

        var feed = await owner.Client.GetFromJsonAsync<CursorPage<ActivityDto>>("/api/v1/activity", Json, Ct);
        var repoEvents = feed!.Items.Where(e => e.EntityType == "repository").Reverse().ToList();

        // Las sincronizaciones cambian LastSyncedAt/ETag, pero eso es estado interno, no una acción de alguien.
        repoEvents.Select(e => e.Action).ShouldBe(["created", "deleted"]);
        repoEvents[0].Changes.GetProperty("name").GetString().ShouldBe(repo.Name);
    }
}
