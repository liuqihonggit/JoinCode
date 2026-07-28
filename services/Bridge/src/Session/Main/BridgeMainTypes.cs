
namespace Core.Bridge;

/// <summary>
/// Headless 模式永久性错误 — 对齐 TS 端 BridgeHeadlessPermanentError
/// 表示配置性问题，supervisor 不应重试，应停放（park）该 worker
/// </summary>
public sealed class BridgeHeadlessPermanentError : Exception
{
    /// <summary>初始化永久性错误</summary>
    public BridgeHeadlessPermanentError(string message) : base(message) { }
}

/// <summary>
/// Headless 模式选项 — 对齐 TS 端 HeadlessBridgeOpts
/// 用于守护进程（daemon worker）场景，无 TUI、无交互、无 readline
/// </summary>
public sealed class BridgeHeadlessOpts
{
    /// <summary>工作目录 — 对齐 TS 端 dir</summary>
    public required string Dir { get; init; }

    /// <summary>会话标题 — 对齐 TS 端 name</summary>
    public string? Name { get; init; }

    /// <summary>子进程生成模式 — 对齐 TS 端 spawnMode（不支持 single-session）</summary>
    public required BridgeSpawnMode SpawnMode { get; init; }

    /// <summary>最大并发会话数 — 对齐 TS 端 capacity</summary>
    public required int Capacity { get; init; }

    /// <summary>权限模式 — 对齐 TS 端 permissionMode</summary>
    public string? PermissionMode { get; init; }

    /// <summary>沙箱模式 — 对齐 TS 端 sandbox</summary>
    public bool Sandbox { get; init; }

    /// <summary>会话超时（毫秒）— 对齐 TS 端 sessionTimeoutMs</summary>
    public int SessionTimeoutMs { get; init; }

    /// <summary>启动时是否预创建会话 — 对齐 TS 端 createSessionOnStart</summary>
    public bool CreateSessionOnStart { get; init; }

    /// <summary>获取访问令牌 — 对齐 TS 端 getAccessToken（IPC 来源）</summary>
    public required Func<string?> GetAccessToken { get; init; }

    /// <summary>401 刷新回调 — 对齐 TS 端 onAuth401（IPC 来源）</summary>
    public Func<string, Task<bool>>? OnAuth401 { get; init; }

    /// <summary>日志输出回调 — 对齐 TS 端 log（写入 worker 的 stdout 管道）</summary>
    public required Action<string> Log { get; init; }

    /// <summary>获取 API 基础 URL</summary>
    public required Func<string> GetBaseUrl { get; init; }

    /// <summary>检查工作区是否已信任 — 对齐 TS 端 checkHasTrustDialogAccepted</summary>
    public Func<bool>? CheckWorkspaceTrusted { get; init; }

    /// <summary>检查 git 仓库是否存在 — 对齐 TS 端 worktree 可用性检查</summary>
    public Func<string, bool>? CheckGitRepoExists { get; init; }

    /// <summary>检查 WorktreeCreate hooks 是否可用 — 对齐 TS 端 WorktreeCreate hooks</summary>
    public Func<bool>? CheckWorktreeCreateHooks { get; init; }
}

/// <summary>
/// Bridge 独立进程依赖注入 — 对齐 TS 端 bridgeMain 的外部依赖
/// </summary>
public sealed class BridgeMainDeps
{
    /// <summary>Bridge API 客户端</summary>
    public required BridgeApiClient ApiClient { get; init; }

    /// <summary>Bridge 日志接口 — 对齐 TS 端 BridgeLogger（交互式/TUI/Headless 适配）</summary>
    public IBridgeLogger? BridgeLogger { get; set; }

    /// <summary>子进程生成器</summary>
    public required BridgeSubprocessSpawner Spawner { get; init; }

    /// <summary>文件系统抽象</summary>
    public required IFileSystem FileSystem { get; init; }

    /// <summary>崩溃恢复指针服务</summary>
    public required BridgePointerService PointerService { get; init; }

    /// <summary>工作目录</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>获取访问令牌</summary>
    public required Func<string?> GetAccessToken { get; init; }

    /// <summary>获取 API 基础 URL</summary>
    public required Func<string> GetBaseUrl { get; init; }

    /// <summary>检查远程控制是否已被用户接受 — 对齐 TS 端 remoteDialogSeen</summary>
    public Func<bool>? CheckRemoteDialogAccepted { get; set; }

    /// <summary>
    /// 远程控制首次确认对话框 — 对齐 TS 端 readline y/n 对话框
    /// 显示说明文本，等待用户输入 y/n，返回 true 表示接受
    /// 调用方负责保存 remoteDialogSeen 配置
    /// </summary>
    public Func<CancellationToken, Task<bool>>? RemoteControlDialog { get; set; }

    /// <summary>
    /// 保存远程控制已确认标志 — 对齐 TS 端 saveGlobalConfig({remoteDialogSeen: true})
    /// 无论用户回答什么，都应保存 remoteDialogSeen=true 防止下次再问
    /// </summary>
    public Action? MarkRemoteDialogSeen { get; set; }

    /// <summary>
    /// Spawn 模式选择对话框 — 对齐 TS 端 readline [1/2] 对话框
    /// 显示选项文本，等待用户输入 1 或 2，返回选择的 spawnMode
    /// 调用方负责保存 remoteControlSpawnMode 配置
    /// </summary>
    public Func<CancellationToken, Task<BridgeSpawnMode>>? SpawnModeDialog { get; set; }

    /// <summary>
    /// 保存 spawn 模式偏好 — 对齐 TS 端 saveCurrentProjectConfig({remoteControlSpawnMode: chosen})
    /// </summary>
    public Action<BridgeSpawnMode>? SaveSpawnModePreference { get; set; }

    /// <summary>
    /// 获取已保存的 spawn 模式偏好 — 对齐 TS 端 getCurrentProjectConfig().remoteControlSpawnMode
    /// </summary>
    public Func<BridgeSpawnMode?>? GetSavedSpawnMode { get; set; }

    /// <summary>
    /// 检查 worktree 是否可用 — 对齐 TS 端 worktreeAvailable
    /// 当前目录是 git 仓库时返回 true
    /// </summary>
    public Func<bool>? IsWorktreeAvailable { get; set; }

    /// <summary>
    /// 多会话 spawn 模式是否启用 — 对齐 TS 端 isMultiSessionSpawnEnabled
    /// GrowthBook gate: tengu_ccr_bridge_multi_session
    /// Gate 关闭时: 默认 single-session，不加载保存偏好，不弹对话框
    /// Gate 开启时: 默认 same-dir，加载偏好，首次弹对话框
    /// </summary>
    public Func<bool>? IsMultiSessionSpawnEnabled { get; set; }

    /// <summary>
    /// 注册键盘监听 — 对齐 TS 端 process.stdin.setRawMode(true) + on('data', onStdinData)
    /// 回调参数: 按键处理器，接收原始字节
    /// Space(0x20)=切换QR, w(0x77)=切换spawnMode, Ctrl+C(0x03)/Ctrl+D(0x04)=优雅关闭
    /// </summary>
    public Action<Func<byte, Task>>? RegisterKeyboardListener { get; set; }

    /// <summary>
    /// 注销键盘监听 — 对齐 TS 端 process.stdin.setRawMode(false) + removeListener
    /// </summary>
    public Action? UnregisterKeyboardListener { get; set; }

    /// <summary>
    /// 获取服务端会话标题 — 对齐 TS 端 fetchSessionTitle
    /// GET /v1/sessions/{id} → 提取 title
    /// 如果为 null，则使用 ApiClient.GetSessionTitleAsync 作为后备
    /// </summary>
    public Func<string, CancellationToken, Task<string?>>? FetchSessionTitle { get; set; }

    /// <summary>
    /// 更新服务端会话标题 — 对齐 TS 端 updateBridgeSessionTitle
    /// PATCH /v1/sessions/{id} body={title}
    /// 错误静默处理（best-effort）
    /// </summary>
    public Func<string, string, CancellationToken, Task>? UpdateSessionTitle { get; set; }

    /// <summary>
    /// 权限请求回调 — 对齐 TS 端 deps.onPermissionRequest
    /// 当子进程 stdout 检测到 control_request/can_use_tool 时触发
    /// 参数: sessionId, permissionRequest, accessToken
    /// </summary>
    public Action<string, BridgePermissionRequest, string?>? OnPermissionRequest { get; set; }

    /// <summary>
    /// 活动回调 — 对齐 TS 端 deps.onActivity
    /// 当子进程 stdout 检测到 assistant/result 活动时触发
    /// 参数: sessionId, activity
    /// </summary>
    public Action<string, BridgeNdjsonActivity>? OnActivity { get; set; }

    /// <summary>默认生成模式 — 对齐 TS 端门控默认值</summary>
    public BridgeSpawnMode? DefaultSpawnMode { get; init; }

    /// <summary>Git 分支名</summary>
    public string? GitBranch { get; init; }

    /// <summary>Git 仓库 URL</summary>
    public string? GitRepoUrl { get; init; }

    /// <summary>Worktree 目录 — worktree 模式下子进程的工作目录</summary>
    public string? WorktreeDir { get; init; }

    /// <summary>Worktree 服务 — 对齐 TS 端 worktree 隔离创建/清理</summary>
    public IAgentWorktreeService? WorktreeService { get; init; }

    /// <summary>权限模式 — 传递给子进程</summary>
    public string? PermissionMode { get; init; }

    /// <summary>轮询配置</summary>
    public BridgeMainPollConfig? PollConfig { get; init; }

    /// <summary>容量唤醒服务 — 对齐 TS 端 capacityWake</summary>
    public CapacityWakeService? CapacityWake { get; init; }

    /// <summary>Token 刷新调度器 — 对齐 TS 端 tokenRefresh（优先使用 BridgeMain 内部创建的实例）</summary>
    public BridgeTokenRefreshScheduler? TokenRefreshScheduler { get; init; }

    /// <summary>
    /// 重连会话回调 — 对齐 TS 端 api.reconnectSession(environmentId, sessionId)
    /// v2 会话 token 刷新时使用 reconnectSession 而非直接 updateAccessToken
    /// POST /v1/environments/{envId}/bridge/reconnect body={session_id}
    /// </summary>
    public Func<string, string, CancellationToken, Task>? ReconnectSession { get; set; }

    /// <summary>归档会话回调 — 对齐 TS 端 archiveSession</summary>
    public Func<string, CancellationToken, Task>? ArchiveSession { get; init; }

    /// <summary>
    /// 创建初始会话回调 — 对齐 TS 端 createBridgeSession
    /// 在环境注册后、主循环前调用，预创建一个会话
    /// 创建失败是非致命的（non-fatal），bridge 会继续运行
    /// </summary>
    public Func<BridgeCreateSessionRequest, CancellationToken, Task<string?>>? CreateBridgeSession { get; init; }

    /// <summary>BridgeConfig — 用于关闭时判断 spawnMode</summary>
    public BridgeConfig Config { get; init; } = new();

    /// <summary>
    /// 遥测服务 — 对齐 TS 端 logEvent/logEventAsync
    /// 记录 tengu_bridge_* 遥测事件，用于产品分析和监控
    /// </summary>
    public ITelemetryService? TelemetryService { get; init; }
}

/// <summary>
/// Bridge 主循环轮询配置 — 对齐 TS 端 getPollIntervalConfig
/// </summary>
public sealed class BridgeMainPollConfig
{
    /// <summary>轮询间隔（毫秒）— 空闲时</summary>
    public int PollIntervalMs { get; init; } = 5000;

    /// <summary>心跳间隔（毫秒）— at-capacity 时</summary>
    public int HeartbeatIntervalMs { get; init; } = 30000;

    /// <summary>关闭等待超时（毫秒）</summary>
    public int ShutdownGraceMs { get; init; } = 30000;

    /// <summary>
    /// 非独占心跳间隔（毫秒）— 对齐 TS 端 non_exclusive_heartbeat_interval_ms
    /// 当 > 0 时启用 at-capacity 心跳循环模式，在此间隔内循环发送心跳
    /// 默认 0 = 禁用心跳循环（仅做一次心跳+等待）
    /// </summary>
    public int NonExclusiveHeartbeatIntervalMs { get; init; }

    /// <summary>
    /// at-capacity 轮询间隔（毫秒）— 对齐 TS 端 pollDeadline
    /// 心跳循环期间定期轮询刷新 token
    /// 默认 0 = 不在心跳循环中轮询
    /// </summary>
    public int AtCapacityPollIntervalMs { get; init; }

    /// <summary>
    /// 回收超时未确认工作项的阈值（毫秒）— 对齐 TS 端 reclaim_older_than_ms
    /// 告诉服务端"如果有超过 N 毫秒仍未被确认的工作项，请回收并重新分配给我"
    /// 默认 5000ms，与服务端 DEFAULT_RECLAIM_OLDER_THAN_MS 匹配
    /// </summary>
    public int ReclaimOlderThanMs { get; init; } = 5000;
}

/// <summary>
/// Bridge 独立进程运行结果
/// </summary>
public sealed class BridgeMainResult
{
    /// <summary>是否正常完成</summary>
    public bool Completed { get; init; }

    /// <summary>帮助文本</summary>
    public string? HelpText { get; init; }

    /// <summary>错误信息</summary>
    public string? Error { get; init; }

    /// <summary>是否有错误</summary>
    public bool HasError => !string.IsNullOrEmpty(Error);
}

/// <summary>
/// 创建初始会话请求 — 对齐 TS 端 createBridgeSession 参数
/// </summary>
public sealed class BridgeCreateSessionRequest
{
    /// <summary>环境 ID</summary>
    public required string EnvironmentId { get; init; }

    /// <summary>会话标题 — 对齐 TS 端 name 参数</summary>
    public string? Title { get; init; }

    /// <summary>Git 仓库 URL</summary>
    public string? GitRepoUrl { get; init; }

    /// <summary>Git 分支</summary>
    public string? Branch { get; init; }

    /// <summary>权限模式</summary>
    public string? PermissionMode { get; init; }
}
