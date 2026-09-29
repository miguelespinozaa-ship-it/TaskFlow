using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TaskFlow.Application.Auth;
using TaskFlow.Domain.Workspaces;
using TaskFlow.IntegrationTests.Infrastructure;

namespace TaskFlow.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(TaskFlowApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Register_crea_usuario_y_workspace_personal_como_Owner()
    {
        var session = await RegisterAsync();

        session.Auth.AccessToken.ShouldNotBeNullOrEmpty();
        session.Auth.Workspace.Role.ShouldBe(WorkspaceRole.Owner);
        session.RefreshCookie.ShouldNotBeNull();

        var me = await session.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me", Json, Ct);
        me!.User.Email.ShouldBe(session.Email);
        me.CurrentWorkspaceId.ShouldBe(session.Auth.Workspace.Id);
        me.Workspaces.ShouldHaveSingleItem().Role.ShouldBe(WorkspaceRole.Owner);
    }

    [Fact]
    public async Task El_refresh_token_va_en_cookie_httpOnly_y_no_en_el_body()
    {
        using var client = Factory.CreateApiClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register",
            new { email = UniqueEmail(), password = Password, displayName = "X" }, Json, Ct);

        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("tf_refresh=", StringComparison.Ordinal));
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("samesite=strict", Case.Insensitive);
        cookie.ShouldContain("path=/api/v1/auth", Case.Insensitive);

        var body = await response.Content.ReadAsStringAsync(Ct);
        body.ShouldNotContain("refresh", Case.Insensitive);
    }

    [Fact]
    public async Task Register_con_email_existente_devuelve_409()
    {
        var existing = await RegisterAsync();
        using var client = Factory.CreateApiClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register",
            new { email = existing.Email.ToUpperInvariant(), password = Password, displayName = "Otro" }, Json, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Register_con_contraseña_debil_devuelve_400_en_Password()
    {
        using var client = Factory.CreateApiClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register",
            new { email = UniqueEmail(), password = "corta", displayName = "X" }, Json, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json, Ct);
        problem!.Errors.ShouldContainKey("Password");
    }

    [Fact]
    public async Task Login_correcto_devuelve_token_del_workspace_personal()
    {
        var registered = await RegisterAsync();
        using var client = Factory.CreateApiClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = registered.Email, password = Password }, Json, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(Json, Ct);
        auth!.Workspace.Id.ShouldBe(registered.Auth.Workspace.Id);
    }

    [Fact]
    public async Task Login_con_email_inexistente_y_con_contraseña_mala_responden_igual()
    {
        // Si las respuestas difirieran, un atacante podría enumerar qué emails están registrados.
        var registered = await RegisterAsync();
        using var client = Factory.CreateApiClient();

        var wrongPassword = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = registered.Email, password = "Incorrecta123" }, Json, Ct);
        var unknownEmail = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = UniqueEmail(), password = "Incorrecta123" }, Json, Ct);

        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknownEmail.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var a = await wrongPassword.Content.ReadFromJsonAsync<ProblemDetails>(Json, Ct);
        var b = await unknownEmail.Content.ReadFromJsonAsync<ProblemDetails>(Json, Ct);
        a!.Title.ShouldBe(b!.Title);
    }

    [Fact]
    public async Task Cinco_intentos_fallidos_bloquean_la_cuenta_aunque_despues_la_contraseña_sea_correcta()
    {
        var registered = await RegisterAsync();
        using var client = Factory.CreateApiClient();

        for (var i = 0; i < 5; i++)
            await client.PostAsJsonAsync("/api/v1/auth/login", new { email = registered.Email, password = "Mala12345" }, Json, Ct);

        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = registered.Email, password = Password }, Json, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/api/v1/projects")]
    [InlineData("/api/v1/workspaces")]
    [InlineData("/api/v1/auth/me")]
    public async Task Endpoints_de_negocio_sin_token_devuelven_401(string url)
    {
        using var client = Factory.CreateApiClient();

        var response = await client.GetAsync(url, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_con_claims_validos_pero_firmado_con_otra_clave_devuelve_401()
    {
        // Un atacante que se fabrica un token de Owner para el workspace de otro: todo correcto salvo la firma.
        var victim = await RegisterAsync();
        var forged = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "taskflow-api",
            Audience = "taskflow-web",
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes("clave-del-atacante-0123456789-abcdefghijk")),
                SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = Guid.NewGuid().ToString(),
                ["workspace_id"] = victim.Auth.Workspace.Id.ToString(),
                ["role"] = "Owner",
            },
        });
        using var client = Factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", forged);

        var response = await client.GetAsync("/api/v1/projects", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
