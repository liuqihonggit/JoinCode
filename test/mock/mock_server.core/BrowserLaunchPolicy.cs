namespace MockServer.Core;

/// <summary>
/// 浏览器自动打开策略 — 只有交互式终端才打开 Dashboard；
/// 显式 <c>--no-open-browser</c> 或输出/输入重定向（E2E、CI、管道宿主）一律不打开，
/// 否则短命测试进程会让浏览器停在 "Failed to fetch"。
/// </summary>
public static class BrowserLaunchPolicy {
    /// <summary>
    /// 是否应自动打开浏览器。
    /// </summary>
    /// <param name="noOpenBrowser">命令行是否显式传入 <c>--no-open-browser</c>。</param>
    /// <param name="outputRedirected">当前进程 stdout 是否被重定向。</param>
    /// <param name="inputRedirected">当前进程 stdin 是否被重定向。</param>
    /// <returns>true 表示可以自动打开 Dashboard。</returns>
    public static bool ShouldOpen(bool noOpenBrowser, bool outputRedirected, bool inputRedirected) =>
        !noOpenBrowser && !outputRedirected && !inputRedirected;
}
