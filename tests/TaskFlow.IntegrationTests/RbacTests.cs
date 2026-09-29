using System.Net;
using System.Net.Http.Json;
using TaskFlow.Domain.Workspaces;
using TaskFlow.IntegrationTests.Infrastructure;

namespace TaskFlow.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class RbacTests(TaskFlowApiFactory factory) : ApiTestBase(factory)
{
    /// <summary>Owner del workspace + un usuario con <paramref name="role"/> ya "logueado" en ese workspace.</summary>
    private async Task<(Session Owner, Session Actor, Guid ProjectId)> WorkspaceWithRoleAsync(WorkspaceRole role)
    {
        var owner = await RegisterAsync();
        var project = await CreateProjectAsync(owner);
        if (role == WorkspaceRole.Owner)
            return (owner, owner, project.Id);

        var actor = await RegisterAsync();
        await AddMemberAsync(owner, actor, role);
        await SwitchWorkspaceAsync(actor, owner.Auth.Workspace.Id);
        return (owner, actor, project.Id);
    }

    [Theory]
    [InlineData(WorkspaceRole.Owner, true)]
    [InlineData(WorkspaceRole.Admin, true)]
    [InlineData(WorkspaceRole.Member, true)]
    [InlineData(WorkspaceRole.Viewer, false)]
    public async Task Solo_Member_y_arriba_pueden_crear_tareas(WorkspaceRole role, bool permitido)
    {
        var (_, actor, projectId) = await WorkspaceWithRoleAsync(role);

        var response = await actor.Client.PostAsJsonAsync($"/api/v1/projects/{projectId}/tasks", new { title = "T" }, Json, Ct);

        response.StatusCode.ShouldBe(permitido ? HttpStatusCode.Created : HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(WorkspaceRole.Owner)]
    [InlineData(WorkspaceRole.Admin)]
    [InlineData(WorkspaceRole.Member)]
    [InlineData(WorkspaceRole.Viewer)]
    public async Task Todos_los_roles_pueden_leer(WorkspaceRole role)
    {
        var (_, actor, projectId) = await WorkspaceWithRoleAsync(role);

        (await actor.Client.GetAsync("/api/v1/projects", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await actor.Client.GetAsync($"/api/v1/projects/{projectId}/tasks", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(WorkspaceRole.Owner, true)]
    [InlineData(WorkspaceRole.Admin, true)]
    [InlineData(WorkspaceRole.Member, false)]
    [InlineData(WorkspaceRole.Viewer, false)]
    public async Task Solo_Admin_y_Owner_pueden_agregar_miembros(WorkspaceRole role, bool permitido)
    {
        var (_, actor, _) = await WorkspaceWithRoleAsync(role);
        var nuevo = await RegisterAsync();

        var response = await actor.Client.PostAsJsonAsync("/api/v1/workspaces/current/members",
            new { email = nuevo.Email, role = WorkspaceRole.Member }, Json, Ct);

        response.StatusCode.ShouldBe(permitido ? HttpStatusCode.Created : HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Nadie_puede_asignar_el_rol_Owner()
    {
        var owner = await RegisterAsync();
        var otro = await RegisterAsync();

        var response = await owner.Client.PostAsJsonAsync("/api/v1/workspaces/current/members",
            new { email = otro.Email, role = WorkspaceRole.Owner }, Json, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Agregar_dos_veces_al_mismo_miembro_devuelve_409()
    {
        var owner = await RegisterAsync();
        var otro = await RegisterAsync();
        await AddMemberAsync(owner, otro, WorkspaceRole.Member);

        var response = await owner.Client.PostAsJsonAsync("/api/v1/workspaces/current/members",
            new { email = otro.Email, role = WorkspaceRole.Viewer }, Json, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task El_rol_es_por_workspace_Owner_en_el_propio_Viewer_en_el_ajeno()
    {
        var (owner, viewer, projectId) = await WorkspaceWithRoleAsync(WorkspaceRole.Viewer);

        (await viewer.Client.PostAsJsonAsync($"/api/v1/projects/{projectId}/tasks", new { title = "T" }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // El mismo usuario: Viewer en el workspace ajeno (el activo) y Owner de su workspace personal.
        var me = await viewer.Client.GetFromJsonAsync<TaskFlow.Application.Auth.MeResponse>("/api/v1/auth/me", Json, Ct);
        me!.CurrentWorkspaceId.ShouldBe(owner.Auth.Workspace.Id);
        me.Workspaces.ShouldContain(w => w.Id == owner.Auth.Workspace.Id && w.Role == WorkspaceRole.Viewer);
        me.Workspaces.ShouldContain(w => w.Id != owner.Auth.Workspace.Id && w.Role == WorkspaceRole.Owner);
    }
}
