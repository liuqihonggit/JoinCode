// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace JoinCode.Plugins.Tests.Unit;

/// <summary>
/// NativePluginHost.Invoke null 守卫单元测试 — 验证 requestJson 为 null/空/空白时抛参数异常
/// <para>纯单元测试,不依赖 native DLL(守卫在加载检查之前触发)</para>
/// </summary>
public sealed class NativePluginHostInvokeGuardTests {
    private static NativePluginHost CreateHost() {
        var fs = new PhysicalFileSystem();
        return new NativePluginHost("/nonexistent/plugin.dll", "guard-test", fs);
    }

    [Fact]
    public async Task Invoke_NullRequestJson_ThrowsArgumentNullException() {
        await using var host = CreateHost();
        var act = () => host.Invoke(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("requestJson");
    }

    [Fact]
    public async Task Invoke_EmptyRequestJson_ThrowsArgumentException() {
        await using var host = CreateHost();
        var act = () => host.Invoke(string.Empty);
        act.Should().Throw<ArgumentException>()
            .WithParameterName("requestJson");
    }

    [Fact]
    public async Task Invoke_WhitespaceRequestJson_ThrowsArgumentException() {
        await using var host = CreateHost();
        var act = () => host.Invoke("   ");
        act.Should().Throw<ArgumentException>()
            .WithParameterName("requestJson");
    }
}
