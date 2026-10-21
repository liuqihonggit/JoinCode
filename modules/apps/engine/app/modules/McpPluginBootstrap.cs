namespace JoinCode.App.Modules;

/// <summary>
/// MCP 插件后台加载协调器（启动开销优化 — TASK037 后续）：
/// 基准问题：McpInitModule.ConfigureAsync 阻塞等待八插件并行加载墙钟（实测 ~1.6s — mcpInit 插件
/// MCP 客户端 stdio 子进程启动+握手为关键路径），拖慢 CLI/GUI/TUI 全宿主启动。
/// 协调语义：StartAsync 立即返回（调用方 fire-and-forget — 启动不等插件），
/// 后台链 WhenAll 全成功后调用就绪钩子（toolsBridge 同步 + kernel.Plugins 刷新 — 工具秒级自动补齐，
/// 与 RemoteClientManager.ToolsListChanged 事件增量刷新双保险）；
/// 加载链/钩子异常全部吞掉（SafeAsync — 返回 Task 恒成功完成，异常不逃逸 ThreadPool；
/// 单插件失败仅丢失该插件工具，不阻断其余就绪补齐 — 对齐 LoadXxxSafeAsync 语义）。
/// 消费方：McpInitModule.ConfigureAsync（委托注入加载清单与就绪钩子 — 零 DI 耦合可测）。
/// </summary>
internal static class McpPluginBootstrap
{
    /// <summary>启动后台加载链 — 立即返回后台 Task（调用方 fire-and-forget；全异常吞 — 恒成功完成语义）。
    /// pluginLoads 返回并行加载任务清单（WhenAll 墙钟语义保持）；onAllLoaded 为全成功后的就绪钩子。</summary>
    public static Task StartAsync(
        Func<CancellationToken, Task[]> pluginLoads,
        Func<CancellationToken, Task> onAllLoaded,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pluginLoads);
        ArgumentNullException.ThrowIfNull(onAllLoaded);
        return RunAsync(pluginLoads, onAllLoaded, cancellationToken);
    }

    /// <summary>后台链主体 — WhenAll → 全成功钩子；任何阶段异常吞掉（返回 Task 恒成功完成）。</summary>
    private static async Task RunAsync(
        Func<CancellationToken, Task[]> pluginLoads,
        Func<CancellationToken, Task> onAllLoaded,
        CancellationToken cancellationToken)
    {
        try
        {
            var loads = pluginLoads(cancellationToken);
            await Task.WhenAll(loads).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                // 宿主已请求关闭 — 跳过就绪钩子（Dispose 竞态防护：LoadXxxSafeAsync 会把取消吞成"完成"，
                // WhenAll 误判全成功；钩子前取消短路防碰已释放 IServiceProvider）
                return;
            }

            await onAllLoaded(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 宿主关闭取消 — 静默收尾（后台链随进程结束）
        }
        catch (Exception ex)
        {
            // SafeAsync — 单插件失败/钩子失败吞掉不逃逸（fire-and-forget 未观察异常崩溃风险）；
            // 诊断走 stderr 旁路（协调器零依赖不持 logger — 对齐 AsyncStderrWriter 诊断先例）
            Console.Error.WriteLine($"[McpPluginBootstrap] 后台加载链终止: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

/// <summary>
/// MCP 插件后台加载生命周期句柄（DI 单例）— 后台链与宿主 Dispose 的退出竞态防护：
/// Run 记录链 Task + 持取消源（单链语义 — 二次 Run no-op）；DisposeAsync 先取消（加速断链 —
/// 链内 OCE 静默收尾）再无超时等待链完成（对齐 AGENTS 资源规范：释放函数禁止超时，直接 await）。
/// 场景：真实交互会话链早已完成（Dispose 瞬回零等待）；启动即退出（selftest/快速退出）等链收尾
/// （秒级 — 消除 Dispose 后访问已释放 IServiceProvider 的 ObjectDisposedException 竞态噪音）。
/// 注册消费：McpInitModule.ConfigureServices 注册 → ConfigureAsync 经 Run 启动 →
/// Host DisposeAsync 释放容器单例时等待收尾。
/// </summary>
internal sealed class McpPluginBootstrapHandle : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task _background = Task.CompletedTask;
    private bool _started;
    private bool _disposed;

    /// <summary>启动后台链（单链语义 — 首次调用生效，后到 no-op；token 归句柄持有供 Dispose 取消）。</summary>
    public void Run(Func<CancellationToken, Task> background)
    {
        ArgumentNullException.ThrowIfNull(background);
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        _background = background(_cts.Token);
    }

    /// <summary>插件就绪完成信号（邮箱/通知语义）— <b>Run 前为占位 Task.CompletedTask（无可等之物 ≠ 就绪！）</b>；
    /// Run 后为后台链 Task（八插件加载 + 就绪钩子全部完成）。
    /// ⚠️ 契约（TASK063 P8 文档化）：Run 前 await 此属性立即通过，但此时插件链尚未启动 —
    /// 误用形态：DI 解析后立刻 await Completion（Run 由 ConfigureAsync 稍后调用）→ 假就绪 →
    /// 查工具表只剩同步注册子集（gh_pr_* 缺失）→ "Tool not found" 假象。
    /// ✅ 正确写法：等待方必须确保 Run 已发生（宿主启动序列 ConfigureAsync 先于消费方查询，
    /// 或消费方经宿主协调拿到"已 Run"保证）；E2E 等就绪在宿主启动完成后 await。
    /// 替代时间性等待（Task.Delay 固定延时在 CI 快慢机上均不可靠）。两类消费方：
    /// E2E/宿主等就绪；CLI 单次命令（gh/mcp_call/mcp_list）在查询工具前等待 —
    /// 后台化后单次进程不等插件加载完成即查 registry，工具表只剩同步注册的 15 个（gh_pr_* 全缺失）；
    /// 长驻宿主（GUI/TUI/交互会话）不等待 — 保留启动开销优化收益。</summary>
    public Task Completion => _background;

    /// <summary>收尾 — 取消（尽力加速断链）→ 无超时等待链完成（链恒成功语义由 Bootstrap 保证，兜底吞）；幂等。</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        try
        {
            await _background.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 防御兜底 — 链异常应已在 Bootstrap 内吞；此处防 Run 装入未包装的原始 Task
            Console.Error.WriteLine($"[McpPluginBootstrapHandle] 后台链终止: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _cts.Dispose();
        }
    }
}
