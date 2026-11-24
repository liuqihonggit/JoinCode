namespace NativeTui.Core.Bridge;

/// <summary>
/// bridge 引擎适配器 — BridgeClient（子进程 stdio 协议）→ IReplEngine（ReplShell 消费面）：
/// 提交/打断/权限模式直通请求写 stdin，事件/诊断排空直通事件泵，存活检测看子进程。
/// 诊断旁路由 shell 经 DrainDiagnostics 写 stderr；引擎退出由 shell 经 IsAlive 检测追加退出消息。
/// </summary>
public sealed class BridgeEngine : IReplEngine, IDisposable
{
    /// <summary>bridge 子进程客户端（持有事件泵与 stdin 写面）。</summary>
    private readonly BridgeClient _client;

    /// <summary>创建适配器 — client=已启动的 bridge 子进程客户端（Dispose 归本类管）。</summary>
    public BridgeEngine(BridgeClient client) => _client = client;

    /// <summary>子进程存活（退出后 shell 空闲时追加退出消息并结束）。</summary>
    public bool IsAlive => _client.IsEngineAlive;

    /// <summary>提交查询 — 写子进程 stdin（submit 请求），返回 queryId（ADR 0165）。</summary>
    public string Submit(string text) => _client.Submit(text);

    /// <summary>提交查询（带图片附件 + queryId — ADR 0165 / TASK066 板块 G G4）。</summary>
    /// <param name="text">提交文本（图片占位 [Image #N] 已由 shell 剥离）。</param>
    /// <param name="images">图片附件快照（bridge 协议 DTO — null/空 = 无附件）。</param>
    /// <param name="queryId">显式查询身份；null/空时由 BridgeClient 生成。</param>
    public string Submit(string text, IReadOnlyList<BridgeImageAttachment>? images, string? queryId = null) =>
        _client.Submit(text, images, queryId);

    /// <summary>打断指定查询 — 写子进程 stdin（interrupt 请求，ADR 0165）。</summary>
    public void Interrupt(string? queryId = null) => _client.Interrupt(queryId);

    /// <summary>请求权限模式切换 — 写子进程 stdin（回执事件为准）。</summary>
    public void SetPermissionMode(string mode) => _client.SetPermissionMode(mode);

    /// <summary>停止后台任务（M8-8 — 写子进程 stdin stop_task 请求；cancelled 终态 background_task
    /// 事件由泵回推面板，即发即返不等回执）。</summary>
    public void StopBackgroundTask(string kind, string taskId) => _client.StopBackgroundTask(kind, taskId);

    /// <summary>排空事件泵待应用事件。</summary>
    public IReadOnlyList<BridgeEvent> DrainEvents() => _client.DrainEvents();

    /// <summary>排空事件泵诊断旁路（协议解析警告等 — shell 写 stderr）。</summary>
    public IReadOnlyList<string> DrainDiagnostics() => _client.DrainDiagnostics();

    /// <summary>释放 — 关闭 stdin 发 quit 并回收子进程（归 BridgeClient 语义）。</summary>
    public void Dispose() => _client.Dispose();
}
