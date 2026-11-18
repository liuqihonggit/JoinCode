namespace JoinCode.Gui.Tests.WebView;

/// <summary>
/// 宿主入口 STA 契约（真机回归 2026-11-19）：<c>async Task&lt;int&gt; Main</c> 会让运行时给主线程套 MTA，
/// WebView2 环境创建以 RPC_E_CHANGED_MODE（0x80010106）失败，<c>--webui-smoke</c> 退出码 3。
/// 入口必须保持同步 <c>int Main</c> + <c>[STAThread]</c>，不得改回 async Main。
/// </summary>
public sealed class WebViewEntryPointTests {
    /// <summary>入口必须是同步 STA 方法（异步入口不套用 STAThread）。</summary>
    [Fact]
    public void Main_IsSynchronousStaEntryPoint() {
        var main = typeof(JoinCode.Gui.Program).GetMethod(
            nameof(JoinCode.Gui.Program.Main),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        main.Should().NotBeNull();
        main.ReturnType.Should().Be(typeof(int), "async Task<int> Main 不套用 STAThread，WebView2 初始化会因 MTA 失败");
        main.GetCustomAttribute<System.Runtime.CompilerServices.AsyncStateMachineAttribute>().Should().BeNull();
        main.GetCustomAttribute<System.STAThreadAttribute>().Should().NotBeNull();
    }
}
