using System.Net;
using System.Net.Http.Json;
using TaskFlow.Application.Auth;
using TaskFlow.IntegrationTests.Infrastructure;

namespace TaskFlow.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class RefreshTokenTests(TaskFlowApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Refresh_rota_el_token_y_devuelve_uno_distinto()
    {
        var session = await RegisterAsync();

        var response = await RefreshAsync(session.RefreshCookie);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rotated = ReadRefreshCookie(response);
        rotated.ShouldNotBeNull();
        rotated.ShouldNotBe(session.RefreshCookie);
        (await response.Content.ReadFromJsonAsync<AuthResponse>(Json, Ct))!.AccessToken.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task RefreshToken_ya_usado_revoca_toda_la_familia()
    {
        var session = await RegisterAsync();
        var original = session.RefreshCookie;

        var legit = await RefreshAsync(original);           // uso legítimo
        var rotated = ReadRefreshCookie(legit);
        var reuse = await RefreshAsync(original);           // reuso del viejo → posible robo

        legit.StatusCode.ShouldBe(HttpStatusCode.OK);
        reuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // No sabemos quién es el atacante: el token rotado (el "bueno") también muere.
        var afterAttack = await RefreshAsync(rotated);
        afterAttack.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dos_refresh_simultaneos_con_el_mismo_token_solo_uno_gana()
    {
        var session = await RegisterAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => RefreshAsync(session.RefreshCookie)));

        results.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task Logout_revoca_la_sesion_y_borra_la_cookie()
    {
        var session = await RegisterAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        AddRefreshCookie(request, session.RefreshCookie);
        var logout = await session.Client.SendAsync(request, Ct);

        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        logout.Headers.GetValues("Set-Cookie").ShouldContain(c => c.StartsWith("tf_refresh=;", StringComparison.Ordinal));
        (await RefreshAsync(session.RefreshCookie)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_sin_cookie_o_con_basura_devuelve_401()
    {
        (await RefreshAsync(null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await RefreshAsync("no-es-un-token")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
