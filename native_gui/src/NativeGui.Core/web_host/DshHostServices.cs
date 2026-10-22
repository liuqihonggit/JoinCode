namespace JoinCode.Gui.WebHost;

/// <summary>
/// DSH 宿主服务容器（ADR 0143）：聚合前端宿主协议背后的状态与能力，由 <see cref="DshWebHost"/> 创建、
/// 注入 <see cref="DshRpcDispatcher"/> 与流式基线。M2 起把数据来源逐个换成本项目自有引擎，
/// 前端只认这些宿主能力，不感知实现。
/// </summary>
internal sealed class DshHostServices {
    /// <summary>构造宿主服务</summary>
    /// <param name="fileSystem">文件系统抽象（工作区注册表等持久化）</param>
    /// <param name="workspaceFilePath">工作区注册表路径（默认 <c>~/.jcc/dsh-workspaces.json</c>）</param>
    /// <param name="journalDirectory">宿主事件日志目录（默认 <c>~/.jcc/dsh_journal</c>）</param>
    /// <param name="homeDirectory">目录选择器的起始主目录（默认取用户目录；测试可注入）</param>
    internal DshHostServices(IFileSystem fileSystem, string? workspaceFilePath = null, string? journalDirectory = null, string? homeDirectory = null) {
        FileSystem = fileSystem;
        Workspaces = new DshWorkspaceStore(fileSystem, workspaceFilePath);
        Settings = new DshSettingsStore(fileSystem);
        Journal = new DshSessionJournal(fileSystem, journalDirectory ?? Path.Combine(AppDataPaths.Default.JccDirectory, "dsh_journal"));
        Sessions = new DshSessionStore(Journal);
        Models = new DshModelCatalogSource();
        Streams = new DshSessionStreamHub(Journal);
        Approvals = new DshApprovalBroker {
            Audit = (sessionId, type, payload) => Streams.PublishElementAsync(sessionId, type, payload),
        };
        WorkspaceFiles = new DshWorkspaceFilesService(Sessions, fileSystem);
        Attachments = new DshAttachmentStore(fileSystem);
        Goals = new DshGoalStore(fileSystem);
        Feedback = new DshMessageFeedbackStore(fileSystem);
        Jobs = new DshJobService(this);
        Terminals = new DshTerminalService(this);
        DirectoryBrowser = new DshDirectoryBrowser(
            fileSystem,
            string.IsNullOrWhiteSpace(homeDirectory) ? null : () => homeDirectory);
        FileReferences = new DshFileReferenceService(Sessions, fileSystem);
    }

    /// <summary>宿主共享文件系统抽象（工作区路径存在性校验等）</summary>
    internal IFileSystem FileSystem { get; }

    /// <summary>自有引擎会话门面（组装完成后注入；未就绪时发送消息返回引导错误）</summary>
    internal Hosting.IJccChatSession? Engine { get; private set; }

    /// <summary>会话事件流中枢（session/follow 实时推送）</summary>
    internal DshSessionStreamHub Streams { get; }

    /// <summary>宿主事件日志（事件序号与内容的唯一持久化来源）</summary>
    internal DshSessionJournal Journal { get; }

    /// <summary>会话控制流中枢（session/control 的 plan / permissions 投影帧，TASK066 C7）</summary>
    internal DshSessionControlHub Control { get; } = new();

    /// <summary>注入已组装的引擎（DshWebUiWindow 完成后台初始化时调用）</summary>
    /// <param name="engine">引擎会话门面</param>
    internal void AttachEngine(Hosting.IJccChatSession engine) => Engine = engine;

    /// <summary>控制帧序号（会话 → 最后分配值；前端按 seq 单调丢弃过期帧）</summary>
    private readonly Lock _controlSeqGate = new();
    private readonly Dictionary<string, long> _controlSeq = new(StringComparer.Ordinal);

    /// <summary>
    /// 应用权限预设（TASK066 C7）：校验 → 写引擎 → 更新会话投影 → 落设置 → 广播
    /// <c>plan/mode</c> 事件与 <c>session/control</c> 投影帧。设置写入静默（不回环触发
    /// <see cref="DshSettingsStore.PermissionPresetChanged"/>，调用方已在此完成引擎应用）。
    /// </summary>
    /// <param name="preset">引擎 <see cref="PermissionMode"/> 枚举词汇（plan/auto/ask/bypass/unattended）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功标记与失败引导文案（成功时文案为空）</returns>
    internal async Task<(bool Ok, string Message)> ApplyPermissionPresetAsync(string preset, CancellationToken cancellationToken) {
        if (PermissionModeExtensions.FromValue(preset) is not { } mode) {
            return (false,
                $"权限预设必须是引擎枚举词汇（plan/auto/ask/bypass/unattended）之一；收到 {preset}。未改动引擎与设置");
        }
        if (Engine is not { } engine) {
            return (false, "引擎仍在组装中，权限预设暂不能应用；请稍后重试（已保存的预设会在引擎就绪后自动生效）");
        }
        await engine.SetPermissionModeAsync(mode, cancellationToken);
        Sessions.SetCurrentPermissionPreset(preset);
        Settings.SetPermissionPreset(preset);
        await Streams.PublishToOpenSessionsAsync("plan/mode", new DshPlanModeData { Active = mode is PermissionMode.Plan });
        foreach (var sessionId in Streams.OpenSessionIds()) {
            await PublishPermissionProjectionAsync(sessionId, preset, cancellationToken);
        }
        return (true, "");
    }

    /// <summary>
    /// 向一个会话推送 <c>plan</c> 与 <c>permissions</c> 两条控制帧（TASK066 C7）。
    /// 序号下限取该会话投影快照的 <c>AsOfSeq</c>：前端只接受 <c>seq &gt; 已有行 seq</c> 的帧，
    /// 否则会把刚算出的投影当过期帧丢弃。
    /// </summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="preset">当前权限预设（引擎枚举词汇）</param>
    /// <param name="cancellationToken">取消令牌</param>
    internal async Task PublishPermissionProjectionAsync(string sessionId, string preset, CancellationToken cancellationToken) {
        var snapshot = await Sessions.ProjectionsAsync(sessionId, cancellationToken);
        var floor = snapshot?.AsOfSeq ?? -1;
        var active = string.Equals(preset, PermissionMode.Plan.ToValue(), StringComparison.Ordinal);
        await Control.PublishAsync(new DshSessionControlProjection {
            SessionId = sessionId,
            Key = "plan",
            Value = JsonSerializer.SerializeToElement(
                new DshSessionPlanProjection { Active = active },
                DshJsonContext.Default.DshSessionPlanProjection),
            Seq = NextControlSeq(sessionId, floor),
        });
        await Control.PublishAsync(new DshSessionControlProjection {
            SessionId = sessionId,
            Key = "permissions",
            Value = JsonSerializer.SerializeToElement(
                new DshSessionPermissionProjection { CurrentValue = preset },
                DshJsonContext.Default.DshSessionPermissionProjection),
            Seq = NextControlSeq(sessionId, floor),
        });
    }

    /// <summary>分配控制帧序号（单调递增，且不低于投影快照水位）</summary>
    private long NextControlSeq(string sessionId, long floor) {
        lock (_controlSeqGate) {
            var next = Math.Max(floor, _controlSeq.TryGetValue(sessionId, out var last) ? last : -1) + 1;
            _controlSeq[sessionId] = next;
            return next;
        }
    }

    /// <summary>工作区注册表（workspace/* 与 workspace/follow 基线）</summary>
    internal DshWorkspaceStore Workspaces { get; }

    /// <summary>设置命名空间（settings/describe 与 settings/mutate）</summary>
    internal DshSettingsStore Settings { get; }

    /// <summary>会话注册表（session/* — 数据源是引擎会话存储，引擎就绪后注入）</summary>
    internal DshSessionStore Sessions { get; }

    /// <summary>模型目录（session/modelCatalog — 启动读 settings.json，引擎就绪后热切换）</summary>
    internal DshModelCatalogSource Models { get; }

    /// <summary>权限确认桥（引擎确认点 → $events waterfall → 前端回帖，TASK058 B1）</summary>
    internal DshApprovalBroker Approvals { get; }

    /// <summary>工作区文件树（workspaceFiles/list|read|readBytes|stat，TASK058 P2-B1）</summary>
    internal DshWorkspaceFilesService WorkspaceFiles { get; }

    /// <summary>附件存储（fileUploads/upload 与 session/attachment，TASK058 P2-B2）</summary>
    internal DshAttachmentStore Attachments { get; }

    /// <summary>目标存储（goals 七方法 + activation-changed 事件，TASK058 P2-B5）</summary>
    internal DshGoalStore Goals { get; }

    /// <summary>消息反馈存储（messageFeedback/list|put|delete，TASK066 C4）</summary>
    internal DshMessageFeedbackStore Feedback { get; }

    /// <summary>后台任务投影（job/list|follow|kill → 引擎后台代理，TASK066 C2）</summary>
    internal DshJobService Jobs { get; }

    /// <summary>侧栏终端（terminal/* 十方法 + follow/retain 流，TASK066 C3）</summary>
    internal DshTerminalService Terminals { get; }

    /// <summary>目录浏览（directoryPicker/list|createDirectory，TASK066 C6）</summary>
    internal DshDirectoryBrowser DirectoryBrowser { get; }

    /// <summary>@ 文件引用补全（fileReferences/list，TASK066 C6）</summary>
    internal DshFileReferenceService FileReferences { get; }
}
