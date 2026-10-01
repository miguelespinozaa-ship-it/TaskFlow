using System.Net;
using System.Net.Http.Json;
using TaskFlow.Application.Comments;
using TaskFlow.Application.Common.Paging;
using TaskFlow.Application.Labels;
using TaskFlow.Application.Projects;
using TaskFlow.Application.Tasks;
using TaskFlow.Domain.Workspaces;
using TaskFlow.IntegrationTests.Infrastructure;

namespace TaskFlow.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ProjectsTests(TaskFlowApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Archivar_lo_oculta_del_listado_salvo_includeArchived_y_bloquea_crear_tareas()
    {
        var s = await RegisterAsync();
        var project = await CreateProjectAsync(s);

        var archived = await ReadAsync<ProjectDto>(await s.Client.PostAsync($"/api/v1/projects/{project.Id}/archive", null, Ct));

        archived.IsArchived.ShouldBeTrue();
        (await s.Client.GetFromJsonAsync<List<ProjectDto>>("/api/v1/projects", Json, Ct))!.ShouldBeEmpty();
        (await s.Client.GetFromJsonAsync<List<ProjectDto>>("/api/v1/projects?includeArchived=true", Json, Ct))!.ShouldHaveSingleItem();
        (await s.Client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/tasks", new { title = "x" }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        await s.Client.PostAsync($"/api/v1/projects/{project.Id}/unarchive", null, Ct);
        (await s.Client.GetFromJsonAsync<List<ProjectDto>>("/api/v1/projects", Json, Ct))!.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Patch_renombra_el_proyecto()
    {
        var s = await RegisterAsync();
        var project = await CreateProjectAsync(s, "Viejo");

        var updated = await ReadAsync<ProjectDto>(await s.Client.PatchAsJsonAsync(
            $"/api/v1/projects/{project.Id}", new { name = "Nuevo" }, Json, Ct));

        updated.Name.ShouldBe("Nuevo");
    }

    [Fact]
    public async Task Borrar_el_proyecto_borra_sus_tareas()
    {
        var s = await RegisterAsync();
        var project = await CreateProjectAsync(s);
        var task = await CreateTaskAsync(s, project.Id, "Huérfana");

        (await s.Client.DeleteAsync($"/api/v1/projects/{project.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await s.Client.GetAsync($"/api/v1/projects/{project.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await s.Client.GetAsync($"/api/v1/tasks/{task.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await s.Client.GetFromJsonAsync<CursorPage<TaskDto>>("/api/v1/tasks", Json, Ct))!.Items.ShouldBeEmpty();
    }

    private static Task<HttpResponseMessage> CreateAsync(Session s, string keyPrefix) =>
        s.Client.PostAsJsonAsync("/api/v1/projects", new { name = "Proyecto", keyPrefix }, Json, Ct);

    [Fact]
    public async Task Prefijo_repetido_en_el_mismo_workspace_devuelve_409()
    {
        var s = await RegisterAsync();
        (await CreateAsync(s, "WEB")).StatusCode.ShouldBe(HttpStatusCode.Created);

        var duplicate = await CreateAsync(s, "WEB");

        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await s.Client.GetFromJsonAsync<List<ProjectDto>>("/api/v1/projects", Json, Ct))!.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task El_mismo_prefijo_se_puede_usar_en_otro_workspace_y_tras_borrar_el_proyecto()
    {
        var a = await RegisterAsync();
        var b = await RegisterAsync();
        var first = await ReadAsync<ProjectDto>(await CreateAsync(a, "WEB"));

        (await CreateAsync(b, "WEB")).StatusCode.ShouldBe(HttpStatusCode.Created);

        await a.Client.DeleteAsync($"/api/v1/projects/{first.Id}", Ct);
        (await CreateAsync(a, "WEB")).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Diez_creaciones_simultaneas_con_el_mismo_prefijo_dejan_un_solo_proyecto()
    {
        // La validación "¿ya existe?" no alcanza: varias requests la pasan a la vez. El índice único decide.
        var s = await RegisterAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => CreateAsync(s, "RACE")));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.Created).ShouldAllBe(r => r.StatusCode == HttpStatusCode.Conflict);
        (await s.Client.GetFromJsonAsync<List<ProjectDto>>("/api/v1/projects", Json, Ct))!.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(WorkspaceRole.Member, HttpStatusCode.Forbidden)]
    [InlineData(WorkspaceRole.Admin, HttpStatusCode.NoContent)]
    public async Task Solo_Admin_y_Owner_borran_proyectos(WorkspaceRole role, HttpStatusCode expected)
    {
        var owner = await RegisterAsync();
        var project = await CreateProjectAsync(owner);
        var actor = await RegisterAsync();
        await AddMemberAsync(owner, actor, role);
        await SwitchWorkspaceAsync(actor, owner.Auth.Workspace.Id);

        (await actor.Client.DeleteAsync($"/api/v1/projects/{project.Id}", Ct)).StatusCode.ShouldBe(expected);
    }
}

[Collection(ApiCollection.Name)]
public sealed class CommentsTests(TaskFlowApiFactory factory) : ApiTestBase(factory)
{
    private async Task<(Session Owner, Session Member, TaskDto Task)> SetupAsync()
    {
        var owner = await RegisterAsync();
        var project = await CreateProjectAsync(owner);
        var task = await CreateTaskAsync(owner, project.Id);
        var member = await RegisterAsync();
        await AddMemberAsync(owner, member, WorkspaceRole.Member);
        await SwitchWorkspaceAsync(member, owner.Auth.Workspace.Id);
        return (owner, member, task);
    }

    private static async Task<CommentDto> CommentAsync(Session s, Guid taskId, string body) =>
        await ReadAsync<CommentDto>(await s.Client.PostAsJsonAsync($"/api/v1/tasks/{taskId}/comments", new { body }, Json, Ct));

    [Fact]
    public async Task Comentarios_se_listan_en_orden_con_el_nombre_del_autor()
    {
        var (owner, member, task) = await SetupAsync();
        await CommentAsync(owner, task.Id, "primero");
        await CommentAsync(member, task.Id, "segundo");

        var list = (await owner.Client.GetFromJsonAsync<List<CommentDto>>($"/api/v1/tasks/{task.Id}/comments", Json, Ct))!;

        list.Select(c => c.Body).ShouldBe(["primero", "segundo"]);
        list[1].AuthorName.ShouldBe("Test User");
        list[1].AuthorId.ShouldBe(member.Auth.User.Id);
    }

    [Fact]
    public async Task Solo_el_autor_edita_aunque_el_otro_sea_Owner()
    {
        var (owner, member, task) = await SetupAsync();
        var comment = await CommentAsync(member, task.Id, "mío");

        var byOwner = await owner.Client.PatchAsJsonAsync($"/api/v1/comments/{comment.Id}", new { body = "del owner" }, Json, Ct);
        var byAuthor = await ReadAsync<CommentDto>(await member.Client.PatchAsJsonAsync(
            $"/api/v1/comments/{comment.Id}", new { body = "editado" }, Json, Ct));

        byOwner.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        byAuthor.Body.ShouldBe("editado");
        byAuthor.EditedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Un_Member_no_borra_comentarios_ajenos_pero_el_Owner_si_modera()
    {
        var (owner, member, task) = await SetupAsync();
        var ofOwner = await CommentAsync(owner, task.Id, "del owner");
        var ofMember = await CommentAsync(member, task.Id, "del member");

        (await member.Client.DeleteAsync($"/api/v1/comments/{ofOwner.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await owner.Client.DeleteAsync($"/api/v1/comments/{ofMember.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var list = await owner.Client.GetFromJsonAsync<List<CommentDto>>($"/api/v1/tasks/{task.Id}/comments", Json, Ct);
        list!.ShouldHaveSingleItem().Id.ShouldBe(ofOwner.Id);
    }

    [Fact]
    public async Task Comentar_una_tarea_de_otro_workspace_devuelve_404()
    {
        var (_, _, task) = await SetupAsync();
        var intruso = await RegisterAsync();

        var response = await intruso.Client.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/comments", new { body = "hola" }, Json, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}

[Collection(ApiCollection.Name)]
public sealed class LabelsTests(TaskFlowApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Nombre_duplicado_sin_importar_mayusculas_devuelve_409()
    {
        var s = await RegisterAsync();
        await s.Client.PostAsJsonAsync("/api/v1/labels", new { name = "Bug", color = "#ff0000" }, Json, Ct);

        var response = await s.Client.PostAsJsonAsync("/api/v1/labels", new { name = "bug", color = "#00ff00" }, Json, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task El_mismo_nombre_en_otro_workspace_esta_permitido()
    {
        var a = await RegisterAsync();
        var b = await RegisterAsync();
        await a.Client.PostAsJsonAsync("/api/v1/labels", new { name = "bug", color = "#ff0000" }, Json, Ct);

        var response = await b.Client.PostAsJsonAsync("/api/v1/labels", new { name = "bug", color = "#ff0000" }, Json, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await b.Client.GetFromJsonAsync<List<LabelDto>>("/api/v1/labels", Json, Ct))!.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Borrar_una_etiqueta_la_quita_de_las_tareas()
    {
        var s = await RegisterAsync();
        var project = await CreateProjectAsync(s);
        var label = await ReadAsync<LabelDto>(await s.Client.PostAsJsonAsync("/api/v1/labels", new { name = "tmp", color = "#abcdef" }, Json, Ct));
        var task = await CreateTaskAsync(s, project.Id, extra: new { title = "T", labelIds = new[] { label.Id } });

        (await s.Client.DeleteAsync($"/api/v1/labels/{label.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await s.Client.GetFromJsonAsync<TaskDto>($"/api/v1/tasks/{task.Id}", Json, Ct))!.LabelIds.ShouldBeEmpty();
    }
}
