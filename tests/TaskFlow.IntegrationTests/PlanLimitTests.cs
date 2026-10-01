using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.Workspaces;
using TaskFlow.Domain.Workspaces;
using TaskFlow.IntegrationTests.Infrastructure;

namespace TaskFlow.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class PlanLimitTests(TaskFlowApiFactory factory) : ApiTestBase(factory)
{
    private static Task<HttpResponseMessage> InviteAsync(Session admin, Session user) =>
        admin.Client.PostAsJsonAsync("/api/v1/workspaces/current/members", new { email = user.Email, role = WorkspaceRole.Member }, Json, Ct);

    private static Task<HttpResponseMessage> NewProjectAsync(Session s) =>
        s.Client.PostAsJsonAsync("/api/v1/projects", new { name = "P", keyPrefix = UniquePrefix() }, Json, Ct);

    private static Task<HttpResponseMessage> ChangePlanAsync(Session s, WorkspacePlan plan) =>
        s.Client.PutAsJsonAsync("/api/v1/workspaces/current/plan", new { plan }, Json, Ct);

    [Fact]
    public async Task Free_admite_tres_miembros_el_cuarto_devuelve_402_y_Pro_lo_destraba()
    {
        var owner = await RegisterAsync(); // miembro 1
        (await InviteAsync(owner, await RegisterAsync())).StatusCode.ShouldBe(HttpStatusCode.Created); // 2
        (await InviteAsync(owner, await RegisterAsync())).StatusCode.ShouldBe(HttpStatusCode.Created); // 3
        var fourth = await RegisterAsync();

        var blocked = await InviteAsync(owner, fourth);

        blocked.StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);
        (await blocked.Content.ReadFromJsonAsync<ProblemDetails>(Json, Ct))!.Title!.ShouldContain("3 miembros");

        var usage = await ReadAsync<WorkspaceUsageDto>(await ChangePlanAsync(owner, WorkspacePlan.Pro));
        usage.ShouldBe(new WorkspaceUsageDto(WorkspacePlan.Pro, 3, null, 0, null));
        (await InviteAsync(owner, fourth)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Free_admite_veinte_proyectos_y_borrar_uno_libera_el_cupo()
    {
        var owner = await RegisterAsync();
        Guid lastId = default;
        for (var i = 0; i < 20; i++)
            lastId = (await ReadAsync<Application.Projects.ProjectDto>(await NewProjectAsync(owner))).Id;

        (await NewProjectAsync(owner)).StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);
        var usage = await owner.Client.GetFromJsonAsync<WorkspaceUsageDto>("/api/v1/workspaces/current", Json, Ct);
        usage.ShouldBe(new WorkspaceUsageDto(WorkspacePlan.Free, 1, 3, 20, 20));

        await owner.Client.DeleteAsync($"/api/v1/projects/{lastId}", Ct);
        (await NewProjectAsync(owner)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Al_volver_a_Free_con_uso_de_mas_se_conserva_lo_existente_pero_no_se_puede_agregar()
    {
        var owner = await RegisterAsync();
        await ChangePlanAsync(owner, WorkspacePlan.Pro);
        for (var i = 0; i < 3; i++)
            (await InviteAsync(owner, await RegisterAsync())).StatusCode.ShouldBe(HttpStatusCode.Created); // 4 miembros

        var usage = await ReadAsync<WorkspaceUsageDto>(await ChangePlanAsync(owner, WorkspacePlan.Free));

        usage.Members.ShouldBe(4);
        usage.MaxMembers.ShouldBe(3);
        (await owner.Client.GetFromJsonAsync<List<MemberDto>>("/api/v1/workspaces/current/members", Json, Ct))!.Count.ShouldBe(4);
        (await InviteAsync(owner, await RegisterAsync())).StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);
    }

    [Theory]
    [InlineData(WorkspaceRole.Admin)]
    [InlineData(WorkspaceRole.Member)]
    public async Task Solo_el_Owner_cambia_el_plan(WorkspaceRole role)
    {
        var owner = await RegisterAsync();
        var actor = await RegisterAsync();
        await AddMemberAsync(owner, actor, role);
        await SwitchWorkspaceAsync(actor, owner.Auth.Workspace.Id);

        (await ChangePlanAsync(actor, WorkspacePlan.Pro)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await actor.Client.GetFromJsonAsync<WorkspaceUsageDto>("/api/v1/workspaces/current", Json, Ct))!.Plan.ShouldBe(WorkspacePlan.Free);
    }

    [Fact]
    public async Task El_plan_es_por_workspace_cambiar_uno_no_afecta_a_otro()
    {
        var a = await RegisterAsync();
        var b = await RegisterAsync();

        await ChangePlanAsync(a, WorkspacePlan.Pro);

        (await b.Client.GetFromJsonAsync<WorkspaceUsageDto>("/api/v1/workspaces/current", Json, Ct))!.Plan.ShouldBe(WorkspacePlan.Free);
    }
}
