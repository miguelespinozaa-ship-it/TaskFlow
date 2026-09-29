using System.Net.Http.Json;
using TaskFlow.Application.Activities;
using TaskFlow.Application.Common.Paging;
using TaskFlow.Application.Labels;
using TaskFlow.IntegrationTests.Infrastructure;

namespace TaskFlow.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ActivityTests(TaskFlowApiFactory factory) : ApiTestBase(factory)
{
    private Task<CursorPage<ActivityDto>> TaskActivityAsync(Session s, Guid taskId) =>
        s.Client.GetFromJsonAsync<CursorPage<ActivityDto>>($"/api/v1/tasks/{taskId}/activity", Json, Ct)!;

    [Fact]
    public async Task Cada_cambio_de_una_tarea_queda_auditado_con_actor_y_diff()
    {
        var s = await RegisterAsync();
        var project = await CreateProjectAsync(s);
        var task = await CreateTaskAsync(s, project.Id, "Original");

        await s.Client.PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new { title = "Renombrada", priority = "Urgent" }, Json, Ct);
        await s.Client.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/move", new { status = "Done" }, Json, Ct);
        await s.Client.DeleteAsync($"/api/v1/tasks/{task.Id}", Ct);

        // La tarea ya está borrada: el historial se consulta desde el feed del workspace.
        var feed = await s.Client.GetFromJsonAsync<CursorPage<ActivityDto>>("/api/v1/activity", Json, Ct);
        var history = feed!.Items.Where(a => a.EntityId == task.Id).Reverse().ToList();

        history.Select(a => a.Action).ShouldBe(["created", "updated", "updated", "deleted"]);
        history.ShouldAllBe(a => a.ActorId == s.Auth.User.Id && a.ActorName == "Test User" && a.EntityType == "task");

        history[0].Changes.GetProperty("title").GetString().ShouldBe("Original");
        var rename = history[1].Changes;
        rename.GetProperty("title").GetProperty("from").GetString().ShouldBe("Original");
        rename.GetProperty("title").GetProperty("to").GetString().ShouldBe("Renombrada");
        rename.GetProperty("priority").GetProperty("to").GetString().ShouldBe("Urgent");
        history[2].Changes.GetProperty("status").GetProperty("to").GetString().ShouldBe("Done");
    }

    [Fact]
    public async Task Cambiar_etiquetas_se_registra_como_update_de_la_tarea()
    {
        var s = await RegisterAsync();
        var project = await CreateProjectAsync(s);
        var label = await ReadAsync<LabelDto>(await s.Client.PostAsJsonAsync("/api/v1/labels", new { name = "bug", color = "#ff0000" }, Json, Ct));
        var task = await CreateTaskAsync(s, project.Id);

        await s.Client.PutAsJsonAsync($"/api/v1/tasks/{task.Id}/labels", new { labelIds = new[] { label.Id } }, Json, Ct);

        var latest = (await TaskActivityAsync(s, task.Id)).Items[0];
        latest.Action.ShouldBe("updated");
        latest.Changes.GetProperty("labels").GetProperty("added")[0].GetGuid().ShouldBe(label.Id);
    }

    [Fact]
    public async Task Un_update_que_no_cambia_nada_no_genera_actividad()
    {
        var s = await RegisterAsync();
        var project = await CreateProjectAsync(s);
        var task = await CreateTaskAsync(s, project.Id, "Igual");

        await s.Client.PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new { title = "Igual" }, Json, Ct);

        (await TaskActivityAsync(s, task.Id)).Items.ShouldHaveSingleItem().Action.ShouldBe("created");
    }

    [Fact]
    public async Task El_feed_es_por_workspace_y_se_pagina()
    {
        var a = await RegisterAsync();
        var b = await RegisterAsync();
        var project = await CreateProjectAsync(a);
        for (var i = 0; i < 5; i++)
            await CreateTaskAsync(a, project.Id, $"T{i}");

        var page1 = await a.Client.GetFromJsonAsync<CursorPage<ActivityDto>>("/api/v1/activity?pageSize=4", Json, Ct);
        var page2 = await a.Client.GetFromJsonAsync<CursorPage<ActivityDto>>($"/api/v1/activity?pageSize=4&cursor={page1!.NextCursor}", Json, Ct);
        var feedB = await b.Client.GetFromJsonAsync<CursorPage<ActivityDto>>("/api/v1/activity", Json, Ct);

        // 1 proyecto + 5 tareas = 6 registros
        page1.Items.Count.ShouldBe(4);
        page2!.Items.Count.ShouldBe(2);
        page2.NextCursor.ShouldBeNull();
        feedB!.Items.ShouldBeEmpty();
    }
}
