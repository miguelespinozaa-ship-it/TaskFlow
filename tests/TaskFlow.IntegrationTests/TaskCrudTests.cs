using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Application.Labels;
using TaskFlow.Application.Tasks;
using TaskFlow.Domain.Tasks;
using TaskFlow.Domain.Workspaces;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.IntegrationTests.Infrastructure;

namespace TaskFlow.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class TaskCrudTests(TaskFlowApiFactory factory) : ApiTestBase(factory)
{
    private async Task<(Session S, Guid ProjectId)> SetupAsync()
    {
        var session = await RegisterAsync();
        return (session, (await CreateProjectAsync(session)).Id);
    }

    private Task<List<TaskDto>> BoardAsync(Session s, Guid projectId) =>
        s.Client.GetFromJsonAsync<List<TaskDto>>($"/api/v1/projects/{projectId}/tasks", Json, Ct)!;

    private async Task<TaskDto> MoveAsync(Session s, Guid taskId, TaskItemStatus status, Guid? afterTaskId) =>
        await ReadAsync<TaskDto>(await s.Client.PostAsJsonAsync(
            $"/api/v1/tasks/{taskId}/move", new { status, afterTaskId }, Json, Ct));

    [Fact]
    public async Task Get_devuelve_el_detalle_y_la_Location_del_create_apunta_ahi()
    {
        var (s, projectId) = await SetupAsync();

        var create = await s.Client.PostAsJsonAsync($"/api/v1/projects/{projectId}/tasks", new { title = "Detalle" }, Json, Ct);
        var task = await s.Client.GetFromJsonAsync<TaskDto>(create.Headers.Location, Json, Ct);

        task!.Title.ShouldBe("Detalle");
        task.ReporterId.ShouldBe(s.Auth.User.Id);
    }

    [Fact]
    public async Task Patch_cambia_solo_los_campos_enviados()
    {
        var (s, projectId) = await SetupAsync();
        var task = await CreateTaskAsync(s, projectId, extra: new { title = "Original", description = "desc", priority = "Low" });
        var due = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        var updated = await ReadAsync<TaskDto>(await s.Client.PatchAsJsonAsync(
            $"/api/v1/tasks/{task.Id}", new { title = "Renombrada", dueAt = due }, Json, Ct));

        updated.Title.ShouldBe("Renombrada");
        updated.Description.ShouldBe("desc");          // no enviado → sin cambios
        updated.Priority.ShouldBe(TaskPriority.Low);
        updated.DueAt.ShouldBe(due);

        var cleared = await ReadAsync<TaskDto>(await s.Client.PatchAsJsonAsync(
            $"/api/v1/tasks/{task.Id}", new { description = "", clearDueAt = true }, Json, Ct));
        cleared.Description.ShouldBeNull();
        cleared.DueAt.ShouldBeNull();
    }

    [Fact]
    public async Task Mover_entre_dos_tareas_la_deja_en_medio_sin_tocar_las_demas()
    {
        var (s, projectId) = await SetupAsync();
        var a = await CreateTaskAsync(s, projectId, "A");
        var b = await CreateTaskAsync(s, projectId, "B");
        var c = await CreateTaskAsync(s, projectId, "C");

        var moved = await MoveAsync(s, c.Id, TaskItemStatus.Todo, afterTaskId: a.Id);

        moved.Position.ShouldBeGreaterThan(a.Position);
        moved.Position.ShouldBeLessThan(b.Position);
        (await BoardAsync(s, projectId)).Select(t => t.Title).ShouldBe(["A", "C", "B"]);
    }

    [Fact]
    public async Task Mover_sin_referencia_la_deja_primera_y_a_Done_marca_completada()
    {
        var (s, projectId) = await SetupAsync();
        var a = await CreateTaskAsync(s, projectId, "A");
        var b = await CreateTaskAsync(s, projectId, "B");

        await MoveAsync(s, b.Id, TaskItemStatus.Todo, afterTaskId: null);
        var done = await MoveAsync(s, a.Id, TaskItemStatus.Done, afterTaskId: null);

        (await BoardAsync(s, projectId)).Where(t => t.Status == TaskItemStatus.Todo).Select(t => t.Title).ShouldBe(["B"]);
        done.Status.ShouldBe(TaskItemStatus.Done);
        done.CompletedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Mover_despues_de_una_tarea_de_otra_columna_devuelve_400()
    {
        var (s, projectId) = await SetupAsync();
        var a = await CreateTaskAsync(s, projectId, "A");
        var b = await CreateTaskAsync(s, projectId, "B");

        var response = await s.Client.PostAsJsonAsync(
            $"/api/v1/tasks/{b.Id}/move", new { status = TaskItemStatus.Done, afterTaskId = a.Id }, Json, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cincuenta_inserciones_en_el_mismo_hueco_rebalancean_la_columna_y_mantienen_el_orden()
    {
        // Cada inserción justo después de "Primera" parte el hueco a la mitad. Hacia la ~43, numeric(20,10)
        // ya no distingue dos posiciones; el servidor renumera la columna mucho antes (~29).
        var (s, projectId) = await SetupAsync();
        var first = await CreateTaskAsync(s, projectId, "Primera");
        await CreateTaskAsync(s, projectId, "Ultima");
        var inserted = new List<string>();

        for (var i = 1; i <= 50; i++)
        {
            var task = await CreateTaskAsync(s, projectId, $"T{i:00}");
            await MoveAsync(s, task.Id, TaskItemStatus.Todo, afterTaskId: first.Id);
            inserted.Insert(0, task.Title); // cada nueva queda inmediatamente debajo de "Primera"
        }

        var board = await BoardAsync(s, projectId);
        board.Select(t => t.Title).ShouldBe(["Primera", .. inserted, "Ultima"]);
        board.Select(t => t.Position).Distinct().Count().ShouldBe(board.Count);
    }

    [Fact]
    public async Task Asignar_a_un_miembro_funciona_y_a_un_ajeno_devuelve_400()
    {
        var (owner, projectId) = await SetupAsync();
        var member = await RegisterAsync();
        await AddMemberAsync(owner, member, WorkspaceRole.Member);
        var outsider = await RegisterAsync();
        var task = await CreateTaskAsync(owner, projectId);

        var assigned = await ReadAsync<TaskDto>(await owner.Client.PostAsJsonAsync(
            $"/api/v1/tasks/{task.Id}/assign", new { assigneeId = member.Auth.User.Id }, Json, Ct));
        var toOutsider = await owner.Client.PostAsJsonAsync(
            $"/api/v1/tasks/{task.Id}/assign", new { assigneeId = outsider.Auth.User.Id }, Json, Ct);
        var unassigned = await ReadAsync<TaskDto>(await owner.Client.PostAsJsonAsync(
            $"/api/v1/tasks/{task.Id}/assign", new { assigneeId = (Guid?)null }, Json, Ct));

        assigned.AssigneeId.ShouldBe(member.Auth.User.Id);
        toOutsider.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        unassigned.AssigneeId.ShouldBeNull();
    }

    [Fact]
    public async Task Etiquetas_se_reemplazan_y_las_de_otro_workspace_se_rechazan()
    {
        var (s, projectId) = await SetupAsync();
        var bug = await ReadAsync<LabelDto>(await s.Client.PostAsJsonAsync("/api/v1/labels", new { name = "bug", color = "#ff0000" }, Json, Ct));
        var ux = await ReadAsync<LabelDto>(await s.Client.PostAsJsonAsync("/api/v1/labels", new { name = "ux", color = "#00ff00" }, Json, Ct));
        var ajeno = await RegisterAsync();
        var foreign = await ReadAsync<LabelDto>(await ajeno.Client.PostAsJsonAsync("/api/v1/labels", new { name = "x", color = "#0000ff" }, Json, Ct));
        var task = await CreateTaskAsync(s, projectId, extra: new { title = "Con etiquetas", labelIds = new[] { bug.Id } });

        var replaced = await ReadAsync<TaskDto>(await s.Client.PutAsJsonAsync(
            $"/api/v1/tasks/{task.Id}/labels", new { labelIds = new[] { ux.Id } }, Json, Ct));
        var withForeign = await s.Client.PutAsJsonAsync(
            $"/api/v1/tasks/{task.Id}/labels", new { labelIds = new[] { ux.Id, foreign.Id } }, Json, Ct);

        task.LabelIds.ShouldBe([bug.Id]);
        replaced.LabelIds.ShouldBe([ux.Id]);
        withForeign.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_es_soft_la_fila_sigue_en_la_base_pero_la_API_no_la_ve()
    {
        var (s, projectId) = await SetupAsync();
        var task = await CreateTaskAsync(s, projectId, "Borrame");

        var delete = await s.Client.DeleteAsync($"/api/v1/tasks/{task.Id}", Ct);

        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await s.Client.GetAsync($"/api/v1/tasks/{task.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await BoardAsync(s, projectId)).ShouldBeEmpty();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Tasks.IgnoreQueryFilters([QueryFilters.SoftDelete]).SingleAsync(t => t.Id == task.Id, Ct);
        row.DeletedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Soft_delete_de_una_tarea_con_etiquetas_no_borra_sus_filas_de_task_labels()
    {
        var (s, projectId) = await SetupAsync();
        var label = await ReadAsync<LabelDto>(await s.Client.PostAsJsonAsync("/api/v1/labels", new { name = "keep", color = "#123456" }, Json, Ct));
        var task = await CreateTaskAsync(s, projectId, extra: new { title = "T", labelIds = new[] { label.Id } });

        await s.Client.DeleteAsync($"/api/v1/tasks/{task.Id}", Ct);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Tasks.IgnoreQueryFilters([QueryFilters.SoftDelete]).Include(t => t.Labels).SingleAsync(t => t.Id == task.Id, Ct);
        row.Labels.ShouldHaveSingleItem().LabelId.ShouldBe(label.Id);
    }

    [Theory]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("MOVE")]
    public async Task Tarea_de_otro_workspace_es_404_para_toda_operacion(string operation)
    {
        var (owner, projectId) = await SetupAsync();
        var task = await CreateTaskAsync(owner, projectId);
        var intruso = await RegisterAsync();

        var response = operation switch
        {
            "PATCH" => await intruso.Client.PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new { title = "x" }, Json, Ct),
            "DELETE" => await intruso.Client.DeleteAsync($"/api/v1/tasks/{task.Id}", Ct),
            _ => await intruso.Client.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/move", new { status = "Done" }, Json, Ct),
        };

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Viewer_no_puede_editar_ni_borrar()
    {
        var (owner, projectId) = await SetupAsync();
        var task = await CreateTaskAsync(owner, projectId);
        var viewer = await RegisterAsync();
        await AddMemberAsync(owner, viewer, WorkspaceRole.Viewer);
        await SwitchWorkspaceAsync(viewer, owner.Auth.Workspace.Id);

        (await viewer.Client.PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new { title = "x" }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.Client.DeleteAsync($"/api/v1/tasks/{task.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.Client.GetAsync($"/api/v1/tasks/{task.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
