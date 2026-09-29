using System.Net;
using System.Net.Http.Json;
using TaskFlow.Application.Common.Paging;
using TaskFlow.Application.Labels;
using TaskFlow.Application.Tasks;
using TaskFlow.Domain.Tasks;
using TaskFlow.IntegrationTests.Infrastructure;

namespace TaskFlow.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class SearchPaginationTests(TaskFlowApiFactory factory) : ApiTestBase(factory)
{
    private Task<CursorPage<TaskDto>> SearchAsync(Session s, string query) =>
        s.Client.GetFromJsonAsync<CursorPage<TaskDto>>($"/api/v1/tasks?{query}", Json, Ct)!;

    [Fact]
    public async Task Recorrer_todas_las_paginas_devuelve_cada_tarea_una_vez_en_orden()
    {
        var s = await RegisterAsync();
        var project = await CreateProjectAsync(s);
        for (var i = 0; i < 25; i++)
            await CreateTaskAsync(s, project.Id, $"Tarea {i}");

        var seen = new List<TaskDto>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await SearchAsync(s, $"pageSize=10{(cursor is null ? "" : $"&cursor={cursor}")}");
            seen.AddRange(page.Items);
            cursor = page.NextCursor;
            pages++;
        } while (cursor is not null);

        pages.ShouldBe(3);
        seen.Count.ShouldBe(25);
        seen.Select(t => t.Id).Distinct().Count().ShouldBe(25);
        seen.Select(t => t.CreatedAt).ShouldBeInOrder(SortDirection.Descending);
    }

    [Fact]
    public async Task Una_tarea_creada_mientras_se_pagina_no_duplica_ni_salta_elementos()
    {
        // El problema clásico de OFFSET: si se inserta una fila entre la página 1 y la 2, OFFSET 10
        // devuelve de nuevo la última de la página 1. Con cursor no pasa.
        var s = await RegisterAsync();
        var project = await CreateProjectAsync(s);
        for (var i = 0; i < 15; i++)
            await CreateTaskAsync(s, project.Id, $"Tarea {i}");

        var page1 = await SearchAsync(s, "pageSize=10");
        await CreateTaskAsync(s, project.Id, "Nueva durante la paginación");
        var page2 = await SearchAsync(s, $"pageSize=10&cursor={page1.NextCursor}");

        page2.Items.Count.ShouldBe(5);
        page1.Items.Select(t => t.Id).Intersect(page2.Items.Select(t => t.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Filtros_por_estado_proyecto_y_etiqueta_se_combinan()
    {
        var s = await RegisterAsync();
        var p1 = await CreateProjectAsync(s, "Uno");
        var p2 = await CreateProjectAsync(s, "Dos");
        var label = await ReadAsync<LabelDto>(await s.Client.PostAsJsonAsync("/api/v1/labels", new { name = "urgente", color = "#ff0000" }, Json, Ct));

        var target = await CreateTaskAsync(s, p1.Id, extra: new { title = "Objetivo", labelIds = new[] { label.Id } });
        await CreateTaskAsync(s, p1.Id, "Sin etiqueta");
        await CreateTaskAsync(s, p2.Id, extra: new { title = "Otro proyecto", labelIds = new[] { label.Id } });
        var done = await CreateTaskAsync(s, p1.Id, extra: new { title = "Hecha", labelIds = new[] { label.Id } });
        await s.Client.PostAsJsonAsync($"/api/v1/tasks/{done.Id}/move", new { status = "Done" }, Json, Ct);

        var result = await SearchAsync(s, $"projectId={p1.Id}&labelId={label.Id}&status=Todo");

        result.Items.ShouldHaveSingleItem().Id.ShouldBe(target.Id);
    }

    [Theory]
    [InlineData("factura")]      // singular encuentra plural (stemming)
    [InlineData("FACTURAS")]     // sin distinguir mayúsculas
    [InlineData("revisar")]
    public async Task Busqueda_full_text_en_español(string term)
    {
        var s = await RegisterAsync();
        var project = await CreateProjectAsync(s);
        var match = await CreateTaskAsync(s, project.Id, extra: new { title = "Revisar facturas del mes", description = "Contabilidad" });
        await CreateTaskAsync(s, project.Id, "Deploy a producción");

        var result = await SearchAsync(s, $"search={Uri.EscapeDataString(term)}");

        result.Items.ShouldHaveSingleItem().Id.ShouldBe(match.Id);
    }

    [Fact]
    public async Task La_busqueda_tambien_mira_la_descripcion_y_se_actualiza_al_editar()
    {
        var s = await RegisterAsync();
        var project = await CreateProjectAsync(s);
        var task = await CreateTaskAsync(s, project.Id, extra: new { title = "Tarea", description = "migrar la base de datos" });

        (await SearchAsync(s, "search=migrar")).Items.ShouldHaveSingleItem();

        await s.Client.PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new { description = "otra cosa" }, Json, Ct);

        // La columna tsvector es GENERADA por Postgres: no hay que mantenerla a mano.
        (await SearchAsync(s, "search=migrar")).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task La_busqueda_no_cruza_tenants_ni_devuelve_borradas()
    {
        var a = await RegisterAsync();
        var b = await RegisterAsync();
        var projectA = await CreateProjectAsync(a);
        var projectB = await CreateProjectAsync(b);
        await CreateTaskAsync(a, projectA.Id, "Secreto compartido");
        var borrada = await CreateTaskAsync(b, projectB.Id, "Secreto borrado");
        await b.Client.DeleteAsync($"/api/v1/tasks/{borrada.Id}", Ct);

        (await SearchAsync(b, "search=secreto")).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cursor_invalido_devuelve_400()
    {
        var s = await RegisterAsync();

        var response = await s.Client.GetAsync("/api/v1/tasks?cursor=no-es-un-cursor", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
