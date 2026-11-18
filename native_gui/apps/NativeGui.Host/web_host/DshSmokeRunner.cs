namespace JoinCode.Gui.WebHost;

/// <summary>
/// DSH 宿主无窗口冒烟 — 启动真实 HttpListener/WS 宿主，HTTP GET 首页并校验资产注入，
/// 不创建 WebView2 窗口，供 CI/无桌面环境验证 web_host 装配链路。
/// </summary>
internal static class DshSmokeRunner {
    /// <summary>执行冒烟；0=首页可访问且返回 HTML，1=失败。</summary>
    internal static async Task<int> RunAsync() {
        var services = new DshHostServices(new PhysicalFileSystem());
        await using var host = DshWebHost.Start(services);
        using var http = new HttpClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        try {
            var html = await http.GetStringAsync(host.BaseUrl, cts.Token);
            var hasHtml = html.Contains("<html", StringComparison.OrdinalIgnoreCase);
            var hasHostInjection = html.Contains("dsh", StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"[dsh-smoke] GET {host.BaseUrl} len={html.Length} html={hasHtml} injection={hasHostInjection}");
            return hasHtml && hasHostInjection ? 0 : 1;
        } catch (Exception ex) {
            Console.WriteLine($"[dsh-smoke] FAIL {host.BaseUrl} {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }
}
