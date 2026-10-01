using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.IntegrationTests.Infrastructure;

namespace TaskFlow.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class HealthCheckTests(TaskFlowApiFactory factory) : ApiTestBase(factory)
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Los_health_checks_son_publicos_y_responden_Healthy(string url)
    {
        using var client = Factory.CreateApiClient(); // sin token

        var response = await client.GetAsync(url, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        body.GetProperty("status").GetString().ShouldBe("Healthy");
    }

    [Fact]
    public async Task Ready_chequea_la_base_y_live_no()
    {
        using var client = Factory.CreateApiClient();

        var ready = await client.GetFromJsonAsync<JsonElement>("/health/ready", Json, Ct);
        var live = await client.GetFromJsonAsync<JsonElement>("/health/live", Json, Ct);

        ready.GetProperty("checks").GetProperty("postgres").GetString().ShouldBe("Healthy");
        // live no depende de nada externo: si Postgres cae, el proceso no tiene por qué reiniciarse.
        live.GetProperty("checks").EnumerateObject().ShouldBeEmpty();
    }

    [Fact]
    public async Task Ready_devuelve_503_sin_detalles_cuando_la_base_no_responde()
    {
        // Misma API, pero apuntando a un Postgres que no existe.
        using var broken = Factory.WithWebHostBuilder(b => b.UseSetting(
            "ConnectionStrings:Postgres", "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2"));
        using var client = broken.CreateClient();

        var ready = await client.GetAsync("/health/ready", Ct);
        var live = await client.GetAsync("/health/live", Ct);

        ready.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        live.StatusCode.ShouldBe(HttpStatusCode.OK);
        var text = await ready.Content.ReadAsStringAsync(Ct);
        text.ShouldContain("Unhealthy");
        // Endpoint público: no puede filtrar el host, el usuario ni el mensaje de la excepción.
        text.ShouldNotContain("127.0.0.1");
        text.ShouldNotContain("Exception", Case.Insensitive);
    }
}

[Collection(ApiCollection.Name)]
public sealed class RateLimitTests(TaskFlowApiFactory factory) : ApiTestBase(factory)
{
    private HttpClient ClientWithLimit(int permitLimit) =>
        Factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:Auth:PermitLimit", permitLimit.ToString()))
            .CreateClient(new() { HandleCookies = false });

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Incorrecta123" }, Json, Ct);

    [Fact]
    public async Task Superado_el_limite_el_login_responde_429_con_Retry_After()
    {
        using var client = ClientWithLimit(3);

        var allowed = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
            allowed.Add((await LoginAsync(client, UniqueEmail())).StatusCode);
        var blocked = await LoginAsync(client, UniqueEmail());

        // Emails distintos en cada intento: el bloqueo por cuenta de Identity no interviene, esto es el límite por IP.
        allowed.ShouldAllBe(s => s == HttpStatusCode.Unauthorized);
        blocked.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        blocked.Headers.RetryAfter.ShouldNotBeNull();
        blocked.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await blocked.Content.ReadFromJsonAsync<ProblemDetails>(Json, Ct))!.Status.ShouldBe(429);
    }

    [Fact]
    public async Task El_limite_tambien_cubre_el_registro_y_comparte_cuota_con_el_login()
    {
        using var client = ClientWithLimit(2);

        await LoginAsync(client, UniqueEmail());
        await LoginAsync(client, UniqueEmail());
        var register = await client.PostAsJsonAsync("/api/v1/auth/register",
            new { email = UniqueEmail(), password = Password, displayName = "X" }, Json, Ct);

        register.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task El_limite_no_afecta_a_los_demas_endpoints()
    {
        using var client = ClientWithLimit(1);
        await LoginAsync(client, UniqueEmail());
        (await LoginAsync(client, UniqueEmail())).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        // Renovar la sesión y los health checks siguen respondiendo: el límite es solo para login y registro.
        (await client.PostAsync("/api/v1/auth/refresh", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/health/live", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
