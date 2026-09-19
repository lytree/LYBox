using LYBox.Plugin.Shared.Web;
using Microsoft.Extensions.Logging;
using TUnit.Assertions;
using TUnit.Core;

namespace LYBox.Tests.Web;

public class WebConsoleBridgeTests
{
    [Test]
    public async Task TryParse_解析合法信封()
    {
        var result = WebConsoleBridge.TryParse("""L{"l":"error","a":["boom","at line 3"]}""");

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value.Level).IsEqualTo("error");
        await Assert.That(result.Value.Message).IsEqualTo("boom at line 3");
    }

    [Test]
    public async Task TryParse_多参数以空格连接()
    {
        var result = WebConsoleBridge.TryParse("""L{"l":"warn","a":["a",1,true]}""");

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value.Message).IsEqualTo("a 1 true");
    }

    [Test]
    public async Task TryParse_非L前缀返回null()
    {
        await Assert.That(WebConsoleBridge.TryParse(null)).IsNull();
        await Assert.That(WebConsoleBridge.TryParse("")).IsNull();
        await Assert.That(WebConsoleBridge.TryParse("""C{"name":"cmd"}""")).IsNull();
        await Assert.That(WebConsoleBridge.TryParse("""E{"name":"evt"}""")).IsNull();
        await Assert.That(WebConsoleBridge.TryParse("""{"kind":"retry"}""")).IsNull();
    }

    [Test]
    public async Task TryParse_坏JSON或缺失字段返回null()
    {
        await Assert.That(WebConsoleBridge.TryParse("L{invalid")).IsNull();
        await Assert.That(WebConsoleBridge.TryParse("""L{}""")).IsNull();
        await Assert.That(WebConsoleBridge.TryParse("""L{"l":"log"}""")).IsNull();
        await Assert.That(WebConsoleBridge.TryParse("""L{"a":["x"]}""")).IsNull();
        await Assert.That(WebConsoleBridge.TryParse("""L[]""")).IsNull();
    }

    [Test]
    public async Task ToLogLevel_级别映射()
    {
        await Assert.That(WebConsoleBridge.ToLogLevel("error")).IsEqualTo(LogLevel.Error);
        await Assert.That(WebConsoleBridge.ToLogLevel("warn")).IsEqualTo(LogLevel.Warning);
        await Assert.That(WebConsoleBridge.ToLogLevel("info")).IsEqualTo(LogLevel.Information);
        await Assert.That(WebConsoleBridge.ToLogLevel("log")).IsEqualTo(LogLevel.Debug);
        await Assert.That(WebConsoleBridge.ToLogLevel(null)).IsEqualTo(LogLevel.Debug);
        await Assert.That(WebConsoleBridge.ToLogLevel("unknown")).IsEqualTo(LogLevel.Debug);
    }
}
