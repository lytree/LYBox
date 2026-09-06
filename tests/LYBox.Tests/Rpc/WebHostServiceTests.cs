using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LYBox.Plugin.Shared.Web;
using TUnit.Assertions;
using TUnit.Core;

namespace LYBox.Tests.Rpc;

public class WebHostServiceTests
{
    [Test]
    public async Task HttpRpc_要求有效会话_并按插件隔离命令()
    {
        var root = Directory.CreateTempSubdirectory("lybox-webhost-test-");
        await using var host = new WebHostService();
        try
        {
            host.MapPluginRoot("plugin-a", root.FullName);
            host.MapPluginRoot("plugin-b", root.FullName);
            host.RegisterRpcHandler("plugin-a", "same.command", (_, _) => Task.FromResult<object?>("a"));
            host.RegisterRpcHandler("plugin-b", "same.command", (_, _) => Task.FromResult<object?>("b"));
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };
            var noSession = await client.PostAsJsonAsync("/__bridge/plugin-a/rpc", new { name = "same.command", args = Array.Empty<object>() });
            await Assert.That(noSession.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);

            var sessionA = host.CreateSession("plugin-a");
            using var requestA = CreateRpcRequest("plugin-a", sessionA);
            var responseA = await client.SendAsync(requestA);
            await Assert.That(responseA.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var jsonA = JsonDocument.Parse(await responseA.Content.ReadAsStringAsync());
            await Assert.That(jsonA.RootElement.GetProperty("payload").GetString()).IsEqualTo("a");

            using var crossPlugin = CreateRpcRequest("plugin-b", sessionA);
            var crossResponse = await client.SendAsync(crossPlugin);
            await Assert.That(crossResponse.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);

            host.RevokeSession(sessionA);
            using var revoked = CreateRpcRequest("plugin-a", sessionA);
            var revokedResponse = await client.SendAsync(revoked);
            await Assert.That(revokedResponse.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Test]
    public async Task HttpRpc_拒绝非宿主Origin()
    {
        var root = Directory.CreateTempSubdirectory("lybox-webhost-origin-");
        await using var host = new WebHostService();
        try
        {
            host.MapPluginRoot("plugin-a", root.FullName);
            host.RegisterRpcHandler("plugin-a", "ping", (_, _) => Task.FromResult<object?>("pong"));
            await host.StartAsync();

            var session = host.CreateSession("plugin-a");
            using var client = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };
            using var request = CreateRpcRequest("plugin-a", session);
            request.Headers.TryAddWithoutValidation("Origin", "https://untrusted.example");

            var response = await client.SendAsync(request);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static HttpRequestMessage CreateRpcRequest(string pluginId, string session)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/__bridge/{pluginId}/rpc")
        {
            Content = JsonContent.Create(new { name = "same.command", args = Array.Empty<object>() })
        };
        request.Headers.TryAddWithoutValidation("X-LYBox-Session", session);
        return request;
    }
}
