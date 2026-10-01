using System.Net;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using TaskFlow.Domain.Workspaces;
using TaskFlow.IntegrationTests.Infrastructure;

namespace TaskFlow.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class RealtimeTests(TaskFlowApiFactory factory) : ApiTestBase(factory)
{
    /// <summary>Conexión al hub con el token dado. Cuenta los avisos "WorkspaceChanged" que recibe.</summary>
    private async Task<(HubConnection Connection, Func<int> Received)> ConnectAsync(string? accessToken)
    {
        var received = 0;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(Factory.Server.BaseAddress, "hubs/workspace"), options =>
            {
                // El servidor de tests no abre sockets reales: se usa su handler en memoria con long polling.
                options.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult(accessToken);
            })
            .Build();
        connection.On("WorkspaceChanged", () => Interlocked.Increment(ref received));
        await connection.StartAsync(Ct);
        return (connection, () => Volatile.Read(ref received));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 50 && !condition(); i++)
            await Task.Delay(100, Ct);
    }

    [Fact]
    public async Task Un_cambio_avisa_a_quien_esta_en_el_mismo_workspace_y_no_a_los_demas()
    {
        var owner = await RegisterAsync();
        var project = await CreateProjectAsync(owner);
        var member = await RegisterAsync();
        await AddMemberAsync(owner, member, WorkspaceRole.Member);
        await SwitchWorkspaceAsync(member, owner.Auth.Workspace.Id);
        var outsider = await RegisterAsync();

        var (memberConn, memberReceived) = await ConnectAsync(member.Auth.AccessToken);
        var (outsiderConn, outsiderReceived) = await ConnectAsync(outsider.Auth.AccessToken);
        await using var _ = memberConn;
        await using var __ = outsiderConn;

        await CreateTaskAsync(owner, project.Id, "Nueva");
        await WaitUntilAsync(() => memberReceived() > 0);

        memberReceived().ShouldBe(1);
        // El de afuera sí recibe avisos de SU workspace (prueba de que su conexión funciona)…
        await CreateProjectAsync(outsider);
        await WaitUntilAsync(() => outsiderReceived() > 0);
        outsiderReceived().ShouldBe(1);
        // …pero ese cambio no le llegó al workspace del owner, ni el del owner le llegó a él.
        memberReceived().ShouldBe(1);
    }

    [Fact]
    public async Task Una_lectura_no_genera_avisos()
    {
        var owner = await RegisterAsync();
        var project = await CreateProjectAsync(owner);
        var (connection, received) = await ConnectAsync(owner.Auth.AccessToken);
        await using var _ = connection;

        await owner.Client.GetAsync($"/api/v1/projects/{project.Id}/tasks", Ct);
        await owner.Client.GetAsync("/api/v1/tasks?search=algo", Ct);
        await Task.Delay(500, Ct);

        received().ShouldBe(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("token-inventado")]
    public async Task Sin_un_token_valido_no_se_puede_conectar(string? token)
    {
        var ex = await Should.ThrowAsync<HttpRequestException>(() => ConnectAsync(token));

        ex.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task El_token_en_la_query_solo_vale_para_el_hub_no_para_la_API()
    {
        var owner = await RegisterAsync();
        using var client = Factory.CreateApiClient(); // sin header Authorization

        var response = await client.GetAsync($"/api/v1/projects?access_token={owner.Auth.AccessToken}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
