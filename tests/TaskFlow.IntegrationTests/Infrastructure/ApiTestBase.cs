using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaskFlow.Application.Auth;
using TaskFlow.Application.Projects;
using TaskFlow.Domain.Workspaces;

namespace TaskFlow.IntegrationTests.Infrastructure;

/// <summary>Sesión de un usuario de test: cliente con Bearer + la cookie de refresh vigente.</summary>
public sealed class Session(HttpClient client, string email)
{
    public HttpClient Client { get; } = client;
    public string Email { get; } = email;
    public AuthResponse Auth { get; private set; } = null!;
    public string? RefreshCookie { get; private set; }

    public void Apply(AuthResponse auth, string? refreshCookie)
    {
        Auth = auth;
        RefreshCookie = refreshCookie ?? RefreshCookie;
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
    }
}

public abstract class ApiTestBase(TaskFlowApiFactory factory)
{
    public const string Password = "Passw0rd123";

    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    protected TaskFlowApiFactory Factory { get; } = factory;
    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected static string UniqueEmail() => $"user-{Guid.NewGuid():N}@test.dev";

    protected async Task<Session> RegisterAsync(string? email = null)
    {
        email ??= UniqueEmail();
        var session = new Session(Factory.CreateApiClient(), email);
        var response = await session.Client.PostAsJsonAsync(
            "/api/v1/auth/register", new { email, password = Password, displayName = "Test User" }, Json, Ct);
        response.EnsureSuccessStatusCode();
        session.Apply((await response.Content.ReadFromJsonAsync<AuthResponse>(Json, Ct))!, ReadRefreshCookie(response));
        return session;
    }

    protected async Task<ProjectDto> CreateProjectAsync(Session session, string name = "Proyecto")
    {
        var response = await session.Client.PostAsJsonAsync(
            "/api/v1/projects", new { name, keyPrefix = "PR" }, Json, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectDto>(Json, Ct))!;
    }

    protected async Task AddMemberAsync(Session admin, Session user, WorkspaceRole role)
    {
        var response = await admin.Client.PostAsJsonAsync(
            "/api/v1/workspaces/current/members", new { email = user.Email, role }, Json, Ct);
        response.EnsureSuccessStatusCode();
    }

    protected async Task SwitchWorkspaceAsync(Session session, Guid workspaceId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/switch-workspace")
        {
            Content = JsonContent.Create(new { workspaceId }, options: Json),
        };
        AddRefreshCookie(request, session.RefreshCookie);
        var response = await session.Client.SendAsync(request, Ct);
        response.EnsureSuccessStatusCode();
        session.Apply((await response.Content.ReadFromJsonAsync<AuthResponse>(Json, Ct))!, ReadRefreshCookie(response));
    }

    /// <summary>POST /auth/refresh presentando SOLO la cookie (sin Bearer), como hace el navegador.</summary>
    protected async Task<HttpResponseMessage> RefreshAsync(string? refreshCookie)
    {
        using var client = Factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        AddRefreshCookie(request, refreshCookie);
        return await client.SendAsync(request, Ct);
    }

    protected static void AddRefreshCookie(HttpRequestMessage request, string? value)
    {
        if (value is not null)
            request.Headers.Add("Cookie", $"tf_refresh={value}");
    }

    protected static string? ReadRefreshCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.Where(c => c.StartsWith("tf_refresh=", StringComparison.Ordinal))
                .Select(c => c["tf_refresh=".Length..c.IndexOf(';')])
                .FirstOrDefault(v => v.Length > 0)
            : null;
}
