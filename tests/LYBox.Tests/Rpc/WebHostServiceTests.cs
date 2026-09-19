using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
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

    [Test]
    public async Task ParsePort_合法值原样返回_非法值回退零()
    {
        await Assert.That(WebHostService.ParsePort(null)).IsEqualTo(0);
        await Assert.That(WebHostService.ParsePort("")).IsEqualTo(0);
        await Assert.That(WebHostService.ParsePort("abc")).IsEqualTo(0);
        await Assert.That(WebHostService.ParsePort("0")).IsEqualTo(0);
        await Assert.That(WebHostService.ParsePort("-1")).IsEqualTo(0);
        await Assert.That(WebHostService.ParsePort("65536")).IsEqualTo(0);
        await Assert.That(WebHostService.ParsePort("1")).IsEqualTo(1);
        await Assert.That(WebHostService.ParsePort("54321")).IsEqualTo(54321);
        await Assert.That(WebHostService.ParsePort("65535")).IsEqualTo(65535);
    }

    [Test]
    public async Task StartAsync_固定端口生效()
    {
        // 探测一个当前空闲的端口（绑定后立即释放；探测与启动之间的竞争窗口极小）
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var freePort = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var root = Directory.CreateTempSubdirectory("lybox-webhost-fixedport-");
        await using var host = new WebHostService(requestedPort: freePort);
        try
        {
            host.MapPluginRoot("plugin-a", root.FullName);
            await host.StartAsync();

            await Assert.That(host.IsRunning).IsTrue();
            await Assert.That(host.Port).IsEqualTo(freePort);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Test]
    public async Task StartAsync_固定端口被占用时回退随机端口()
    {
        var root = Directory.CreateTempSubdirectory("lybox-webhost-portconflict-");
        await using var blocker = new WebHostService();
        blocker.MapPluginRoot("plugin-blocker", root.FullName);
        await blocker.StartAsync();
        await Assert.That(blocker.IsRunning).IsTrue();

        await using var host = new WebHostService(requestedPort: blocker.Port);
        try
        {
            host.MapPluginRoot("plugin-a", root.FullName);
            await host.StartAsync();

            await Assert.That(host.IsRunning).IsTrue();
            await Assert.That(host.Port).IsGreaterThan(0);
            await Assert.That(host.Port).IsNotEqualTo(blocker.Port);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Test]
    public async Task ParseOrigins_解析开发来源白名单()
    {
        // 未设置/空白 → 空数组（CORS 仅放行自身 BaseUrl）
        await Assert.That(WebHostService.ParseOrigins(null)).IsEmpty();
        await Assert.That(WebHostService.ParseOrigins("   ")).IsEmpty();

        // 单一有效 URI
        var single = WebHostService.ParseOrigins("http://localhost:5173");
        await Assert.That(single).HasSingleItem();
        await Assert.That(single[0].ToString()).IsEqualTo("http://localhost:5173/");

        // 分号/逗号混合 + 空白条目 + 无效条目跳过
        var multi = WebHostService.ParseOrigins(
            "http://localhost:5173; https://127.0.0.1:3000 , not-a-uri ,,");
        await Assert.That(multi.Length).IsEqualTo(2);
        await Assert.That(multi[0].ToString()).IsEqualTo("http://localhost:5173/");
        await Assert.That(multi[1].ToString()).IsEqualTo("https://127.0.0.1:3000/");

        // 无 Host 的绝对 URI（file:// 等）跳过
        await Assert.That(WebHostService.ParseOrigins("file:///C:/x")).IsEmpty();
    }

    [Test]
    public async Task StartDevWatcher_文件变化经SSE推送热刷新事件()
    {
        var root = Directory.CreateTempSubdirectory("lybox-webhost-hotreload-");
        await using var host = new WebHostService();
        using var readCts = new CancellationTokenSource();
        Task? readTask = null;
        try
        {
            host.MapPluginRoot("plugin-a", root.FullName);
            host.StartDevWatcher("plugin-a", root.FullName);
            await host.StartAsync();
            await Assert.That(host.IsRunning).IsTrue();

            // 订阅 SSE 事件流，等待 __lybox:reload 帧
            var session = host.CreateSession("plugin-a");
            using var client = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{host.BaseUrl}/sse/plugin-a");
            request.Headers.TryAddWithoutValidation("X-LYBox-Session", session);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, readCts.Token);
            await Assert.That((int)response.StatusCode).IsEqualTo(200);
            using var stream = await response.Content.ReadAsStreamAsync(readCts.Token);

            var reloadReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            readTask = Task.Run(async () =>
            {
                try
                {
                    using var reader = new StreamReader(stream);
                    string? line;
                    while ((line = await reader.ReadLineAsync(readCts.Token)) != null)
                    {
                        if (line.Contains("__lybox:reload"))
                        {
                            reloadReceived.TrySetResult();
                            break;
                        }
                    }
                }
                catch { /* 流关闭即退出 */ }
            });

            // 触发文件变化（文件系统通知 + 300ms 防抖 → SSE 推送）
            File.WriteAllText(Path.Combine(root.FullName, "index.html"), "<html>changed</html>");

            var completed = await Task.WhenAny(reloadReceived.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            await Assert.That(reloadReceived.Task.IsCompleted).IsTrue();
        }
        finally
        {
            readCts.Cancel();
            if (readTask is not null)
            {
                try { await readTask; } catch { /* 读循环随流关闭退出 */ }
            }
            readCts.Dispose();
            root.Delete(recursive: true);
        }
    }

    [Test]
    public async Task DebugPlugins_返回已注册插件列表()
    {
        var root = Directory.CreateTempSubdirectory("lybox-webhost-debugplugins-");
        await using var host = new WebHostService();
        try
        {
            host.MapPluginRoot("plugin-a", root.FullName);
            host.MapPluginRoot("plugin-b", root.FullName);
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };
            var plugins = await client.GetFromJsonAsync<string[]>("/__lybox/debug/plugins");

            await Assert.That(plugins).IsNotNull();
            await Assert.That(plugins!).Contains("plugin-a");
            await Assert.That(plugins).Contains("plugin-b");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Test]
    public async Task DebugSession_签发会话可用于生产RPC调用()
    {
        var root = Directory.CreateTempSubdirectory("lybox-webhost-debugsession-");
        await using var host = new WebHostService();
        try
        {
            host.MapPluginRoot("plugin-a", root.FullName);
            host.RegisterRpcHandler("plugin-a", "same.command", (_, _) => Task.FromResult<object?>("a"));
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };

            // 调试会话端点为外部浏览器签发 token
            var createResponse = await client.PostAsync("/__lybox/debug/session/plugin-a", content: null);
            await Assert.That((int)createResponse.StatusCode).IsEqualTo(200);
            var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
            var token = created.GetProperty("session").GetString();
            await Assert.That(token).IsNotNull().And.IsNotEmpty();

            // 用自助签发的会话调用生产 RPC 端点
            var rpc = await client.SendAsync(CreateRpcRequest("plugin-a", token!));
            await Assert.That((int)rpc.StatusCode).IsEqualTo(200);
            var result = await rpc.Content.ReadFromJsonAsync<JsonElement>();
            await Assert.That(result.GetProperty("payload").GetString()).IsEqualTo("a");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Test]
    public async Task DebugSession_未注册插件返回404()
    {
        var root = Directory.CreateTempSubdirectory("lybox-webhost-debugsession404-");
        await using var host = new WebHostService();
        try
        {
            host.MapPluginRoot("plugin-a", root.FullName);
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };
            var response = await client.PostAsync("/__lybox/debug/session/plugin-unknown", content: null);

            await Assert.That((int)response.StatusCode).IsEqualTo(404);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
