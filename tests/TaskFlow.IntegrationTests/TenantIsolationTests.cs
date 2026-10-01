using System.Net;
using System.Net.Http.Json;
using TaskFlow.Application.Projects;
using TaskFlow.Application.Tasks;
using TaskFlow.Domain.Workspaces;
using TaskFlow.IntegrationTests.Infrastructure;

namespace TaskFlow.IntegrationTests;

/// <summary>El test que demuestra que el aislamiento entre tenants funciona: un token de la empresa A no ve nada de B.</summary>
[Collection(ApiCollection.Name)]
public sealed class TenantIsolationTests(TaskFlowApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Tareas_de_otro_workspace_devuelven_404_no_403()
    {
        var empresaA = await RegisterAsync();
        var projectA = await CreateProjectAsync(empresaA);
        await empresaA.Client.PostAsJsonAsync($"/api/v1/projects/{projectA.Id}/tasks", new { title = "Secreto de A" }, Json, Ct);
        var empresaB = await RegisterAsync();

        var read = await empresaB.Client.GetAsync($"/api/v1/projects/{projectA.Id}/tasks", Ct);
        var write = await empresaB.Client.PostAsJsonAsync($"/api/v1/projects/{projectA.Id}/tasks", new { title = "Intruso" }, Json, Ct);

        // 404 y NO 403: un 403 le confirmaría a B que el proyecto existe.
        read.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        write.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var tasksA = await empresaA.Client.GetFromJsonAsync<List<TaskDto>>($"/api/v1/projects/{projectA.Id}/tasks", Json, Ct);
        tasksA!.ShouldHaveSingleItem().Title.ShouldBe("Secreto de A");
    }

    [Fact]
    public async Task Cada_workspace_solo_lista_sus_proyectos()
    {
        var empresaA = await RegisterAsync();
        var empresaB = await RegisterAsync();
        var projectA = await CreateProjectAsync(empresaA, "Proyecto A");
        var projectB = await CreateProjectAsync(empresaB, "Proyecto B");

        var listA = await empresaA.Client.GetFromJsonAsync<List<ProjectDto>>("/api/v1/projects", Json, Ct);
        var listB = await empresaB.Client.GetFromJsonAsync<List<ProjectDto>>("/api/v1/projects", Json, Ct);

        listA!.Select(p => p.Id).ShouldBe([projectA.Id]);
        listB!.Select(p => p.Id).ShouldBe([projectB.Id]);
    }

    [Fact]
    public async Task Proyecto_creado_queda_en_el_workspace_del_token_aunque_el_body_diga_otro()
    {
        var empresaA = await RegisterAsync();
        var empresaB = await RegisterAsync();

        // El body trae un workspaceId ajeno: se ignora, el tenant sale SOLO del token.
        var response = await empresaA.Client.PostAsJsonAsync("/api/v1/projects",
            new { name = "Inyección", keyPrefix = "IN", workspaceId = empresaB.Auth.Workspace.Id }, Json, Ct);

        var created = await response.Content.ReadFromJsonAsync<ProjectDto>(Json, Ct);
        created!.WorkspaceId.ShouldBe(empresaA.Auth.Workspace.Id);
        (await empresaB.Client.GetFromJsonAsync<List<ProjectDto>>("/api/v1/projects", Json, Ct))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task No_se_puede_cambiar_a_un_workspace_ajeno()
    {
        var empresaA = await RegisterAsync();
        var intruso = await RegisterAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/switch-workspace")
        {
            Content = JsonContent.Create(new { workspaceId = empresaA.Auth.Workspace.Id }, options: Json),
        };
        var response = await intruso.Client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Miembro_agregado_puede_cambiar_de_workspace_y_ve_sus_proyectos()
    {
        var owner = await RegisterAsync();
        var project = await CreateProjectAsync(owner, "Compartido");
        var member = await RegisterAsync();
        await AddMemberAsync(owner, member, WorkspaceRole.Member);

        await SwitchWorkspaceAsync(member, owner.Auth.Workspace.Id);

        member.Auth.Workspace.Id.ShouldBe(owner.Auth.Workspace.Id);
        member.Auth.Workspace.Role.ShouldBe(WorkspaceRole.Member);
        var projects = await member.Client.GetFromJsonAsync<List<ProjectDto>>("/api/v1/projects", Json, Ct);
        projects!.ShouldHaveSingleItem().Id.ShouldBe(project.Id);
    }
}
