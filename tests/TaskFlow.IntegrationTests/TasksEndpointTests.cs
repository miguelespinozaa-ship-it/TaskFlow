using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.Tasks;
using TaskFlow.Domain.Tasks;
using TaskFlow.IntegrationTests.Infrastructure;

namespace TaskFlow.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class TasksEndpointTests(TaskFlowApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Crear_tarea_y_listarla_de_punta_a_punta()
    {
        var session = await RegisterAsync();
        var project = await CreateProjectAsync(session);
        var url = $"/api/v1/projects/{project.Id}/tasks";

        var create = await session.Client.PostAsJsonAsync(url, new { title = "Primera tarea", priority = "High" }, Json, Ct);

        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<TaskDto>(Json, Ct);
        created.ShouldNotBeNull();
        created.Title.ShouldBe("Primera tarea");
        created.Priority.ShouldBe(TaskPriority.High);
        created.Status.ShouldBe(TaskItemStatus.Todo);

        var list = await session.Client.GetFromJsonAsync<List<TaskDto>>(url, Json, Ct);

        list.ShouldNotBeNull();
        list.ShouldHaveSingleItem().Id.ShouldBe(created.Id);
    }

    [Fact]
    public async Task Tareas_nuevas_se_agregan_al_final_de_la_columna()
    {
        var session = await RegisterAsync();
        var project = await CreateProjectAsync(session);
        var url = $"/api/v1/projects/{project.Id}/tasks";

        var first = await (await session.Client.PostAsJsonAsync(url, new { title = "A" }, Json, Ct))
            .Content.ReadFromJsonAsync<TaskDto>(Json, Ct);
        var second = await (await session.Client.PostAsJsonAsync(url, new { title = "B" }, Json, Ct))
            .Content.ReadFromJsonAsync<TaskDto>(Json, Ct);

        second!.Position.ShouldBeGreaterThan(first!.Position);
    }

    [Fact]
    public async Task Crear_tarea_en_proyecto_inexistente_devuelve_404()
    {
        var session = await RegisterAsync();

        var response = await session.Client.PostAsJsonAsync(
            $"/api/v1/projects/{Guid.CreateVersion7()}/tasks", new { title = "X" }, Json, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Titulo_vacio_devuelve_400_con_errores_por_campo()
    {
        var session = await RegisterAsync();
        var project = await CreateProjectAsync(session);

        var response = await session.Client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/tasks", new { title = "" }, Json, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json, Ct);
        problem!.Errors.ShouldContainKey("Title");
    }

    [Fact]
    public async Task Crear_tarea_en_proyecto_archivado_devuelve_422()
    {
        var session = await RegisterAsync();
        var project = await CreateProjectAsync(session);
        await Factory.ArchiveProjectAsync(project.Id);

        var response = await session.Client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/tasks", new { title = "X" }, Json, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Crear_proyecto_con_prefijo_invalido_devuelve_400()
    {
        var session = await RegisterAsync();

        var response = await session.Client.PostAsJsonAsync("/api/v1/projects", new { name = "P", keyPrefix = "minus" }, Json, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
