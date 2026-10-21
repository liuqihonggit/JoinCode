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
    internal DshHostServices(IFileSystem fileSystem, string? workspaceFilePath = null, string? journalDirectory = null) {
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
    }

    /// <summary>自有引擎会话门面（组装完成后注入；未就绪时发送消息返回引导错误）</summary>
    internal Hosting.IJccChatSession? Engine { get; private set; }

    /// <summary>会话事件流中枢（session/follow 实时推送）</summary>
    internal DshSessionStreamHub Streams { get; }

    /// <summary>宿主事件日志（事件序号与内容的唯一持久化来源）</summary>
    internal DshSessionJournal Journal { get; }

    /// <summary>注入已组装的引擎（DshWebUiWindow 完成后台初始化时调用）</summary>
    /// <param name="engine">引擎会话门面</param>
    internal void AttachEngine(Hosting.IJccChatSession engine) => Engine = engine;

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
}
