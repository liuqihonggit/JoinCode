namespace JoinCode.Gui.WebHost;

/// <summary>
/// DSH 一元 RPC 分发（ADR 0143）。启动期方法返回前端所需骨架数据，workspace/* 与 settings/* 已接宿主真实状态；
/// 未实现的方法显式失败（fail-closed + 引导），便于定位与逐方法替换为自有引擎数据。
/// </summary>
internal sealed class DshRpcDispatcher {
    /// <summary>一元处理器签名（同步方法用 <see cref="Sync"/> 适配）</summary>
    private delegate Task<DshRpcResult> Handler(JsonElement args, CancellationToken cancellationToken);

    private readonly DshHostServices _services;

    /// <summary>一轮对话执行器（session/prompt）</summary>
    private readonly DshTurnRunner _turns;

    /// <summary>会话列表一次返回的最大条数（前端空 cursor 即从头拉取）</summary>
    private const int SessionListLimit = DshSessionStore.MaxSessions;

    /// <summary>分发表：方法名 → 处理器（命名函数，便于替换与检索）</summary>
    private readonly FrozenDictionary<string, Handler> _routes;

    /// <summary>构造分发器</summary>
    /// <param name="services">宿主服务容器</param>
    internal DshRpcDispatcher(DshHostServices services) {
        _services = services;
        _turns = new DshTurnRunner(services);
        _routes = new Dictionary<string, Handler>(StringComparer.Ordinal) {
            ["session/list"] = SessionList,
            ["session/create"] = SessionCreate,
            ["session/rename"] = SessionRename,
            ["session/prompt"] = SessionPrompt,
            ["session/cancel"] = Sync(args => SessionCancel(args, CancellationToken.None)),
            ["session/modelCatalog"] = Sync(SessionModelCatalog),
            ["session/selectModel"] = SessionSelectModel,
            ["session/fork"] = SessionFork,
            ["session/projections"] = SessionProjections,
            ["session/page"] = SessionPage,
            ["session/search"] = SessionSearch,
            ["session/canOpenWorkspacePath"] = Sync(CanOpenWorkspacePath),
            ["session/initializeDefaultModel"] = Sync(InitializeDefaultModel),
            ["session/openWorkspacePath"] = SessionOpenWorkspacePath,
            ["session/workspacePathApplications"] = SessionWorkspacePathApplications,
            ["agentPresets/list"] = AgentPresetsList,
            ["agentPresets/read"] = Sync(AgentPresetsRead),
            ["agentPresets/select"] = Sync(AgentPresetsSelect),
            ["permissionPresets/catalog"] = PermissionPresetsCatalog,
            ["$events/result"] = EventsResult,
            ["userQuestions/answer"] = Sync(UserQuestionsAnswer),
            ["settings/describe"] = Sync(_ => Ok(services.Settings.Describe())),
            ["settings/mutate"] = Sync(args => services.Settings.Mutate(args)),
            ["settings/replace"] = SettingsReplace,
            ["settings/update"] = SettingsUpdate,
            ["credentials/describe"] = Sync(Credentials),
            ["llm/listProviders"] = Sync(LlmListProviders),
            ["llm/listConfigurableProviders"] = Sync(_ => OkEmptyArray()),
            ["commands/list"] = Sync(CommandsList),
            ["commands/execute"] = CommandsExecute,
            ["messageFeedback/list"] = MessageFeedbackList,
            ["messageFeedback/put"] = MessageFeedbackPut,
            ["messageFeedback/delete"] = MessageFeedbackDelete,
            ["skills/list"] = SkillsList,
            ["subagents/prompt"] = SubagentPrompt,
            ["subagents/interruptByParent"] = SubagentInterruptByParent,
            ["dynamicCordisRunner/inventory"] = Sync(_ => OkEmptyArray()),
            ["dynamicCordisRunner/syncInspectManifest"] = Sync(_ => OkNull()),
            // 插件页（TASK066 C7）：宿主没有可管理的 profile，诚实回 managementAvailable=false
            ["pluginInventory/list"] = Sync(_ => Ok(new DshPluginInventorySnapshot())),
            ["directoryPicker/pick"] = Pick,
            ["directoryPicker/list"] = Sync(DirectoryPickerList),
            ["directoryPicker/createDirectory"] = Sync(DirectoryPickerCreateDirectory),
            ["fileReferences/list"] = FileReferencesList,
            ["workspace/list"] = Sync(_ => OkEmptyArray()),
            ["workspace/create"] = WorkspaceCreate,
            ["workspace/rename"] = WorkspaceRename,
            ["workspace/delete"] = WorkspaceDelete,
            ["workspace/insertBefore"] = WorkspaceInsertBefore,
            ["workspace/insertSessionBefore"] = WorkspaceInsertSessionBefore,
            ["workspace/archiveSession"] = WorkspaceArchiveSession,
            ["workspace/unarchiveSession"] = WorkspaceUnarchiveSession,
            ["workspace/pinSession"] = WorkspacePinSession,
            ["workspace/unpinSession"] = WorkspaceUnpinSession,
            // 宿主暂不提供默认工作区（M2 起由自有引擎的会话/工作区目录决定）
            ["workspace/initializeDefault"] = Sync(_ => OkNull()),
            // 工作区文件树（TASK058 P2-B1；changes 流属深化项，前端会收到未注册引导错误）
            ["workspaceFiles/list"] = WorkspaceFilesList,
            ["workspaceFiles/read"] = WorkspaceFilesRead,
            ["workspaceFiles/readBytes"] = WorkspaceFilesReadBytes,
            ["workspaceFiles/stat"] = WorkspaceFilesStat,
            // 附件链（TASK058 P2-B2；uploadFileBinary 直传路由在 DshWebHost octet-stream 分支）
            ["fileUploads/upload"] = FileUploadsUpload,
            ["session/attachment"] = SessionAttachment,
            // 队列编辑（TASK058 P2-B3；steer=引擎无注入点诚实拒绝）
            ["session/updateQueue"] = SessionUpdateQueue,
            // 后台任务（TASK066 C2；list/follow 是 WS 流，路由在 DshWebHost）
            ["job/kill"] = JobKill,
            // 侧栏终端（TASK066 C3；follow/retain 是 WS 流）
            ["terminal/environment"] = TerminalEnvironment,
            ["terminal/shells"] = Sync(TerminalShells),
            ["terminal/list"] = Sync(TerminalList),
            ["terminal/create"] = TerminalCreate,
            ["terminal/write"] = Sync(TerminalWrite),
            ["terminal/resize"] = Sync(TerminalResize),
            ["terminal/rename"] = Sync(TerminalRename),
            ["terminal/close"] = TerminalClose,
            // 目标七方法（TASK058 P2-B5；写路径成功后推 goal 投影帧，前端 GoalDock 实时刷新）
            ["goals/get"] = Sync(GoalsGet),
            ["goals/create"] = GoalsCreate,
            ["goals/edit"] = GoalsEdit,
            ["goals/pause"] = GoalsPause,
            ["goals/resume"] = GoalsResume,
            ["goals/clear"] = GoalsClear,
            ["goals/complete"] = GoalsComplete,
        }.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>分发一次一元调用；未知方法返回稳定错误码（前端日志可见）</summary>
    internal Task<DshRpcResult> HandleAsync(string method, JsonElement args, CancellationToken cancellationToken = default) =>
        _routes.TryGetValue(method, out var handler)
            ? handler(args, cancellationToken)
            : Task.FromResult(Fail("unimplemented", $"宿主尚未实现方法 {method}；请在 DshRpcDispatcher 的路由表注册处理器"));

    /// <summary>把同步处理器适配为异步签名</summary>
    private static Handler Sync(Func<JsonElement, DshRpcResult> handler) => (args, _) => Task.FromResult(handler(args));

    /// <summary>directoryPicker/pick：原生目录选择（返回 null 表示用户取消）</summary>
    private static async Task<DshRpcResult> Pick(JsonElement args, CancellationToken cancellationToken) =>
        Success(JsonSerializer.SerializeToElement(
            await DshDirectoryPicker.PickAsync(cancellationToken),
            DshJsonContext.Default.String));

    /// <summary>directoryPicker/list：浏览宿主目录（path 省略时从主目录开始）</summary>
    private DshRpcResult DirectoryPickerList(JsonElement args) {
        var request = args.ValueKind == JsonValueKind.Object
            ? args.Deserialize(DshJsonContext.Default.DshDirectoryListArgs)
            : null;
        try {
            return Ok(_services.DirectoryBrowser.List(request?.Path));
        } catch (DshDirectoryBrowserException ex) {
            return Fail(ex.Code, ex.Message);
        }
    }

    /// <summary>directoryPicker/createDirectory：在父目录下创建单段子目录</summary>
    private DshRpcResult DirectoryPickerCreateDirectory(JsonElement args) {
        var request = args.ValueKind == JsonValueKind.Object
            ? args.Deserialize(DshJsonContext.Default.DshDirectoryCreateArgs)
            : null;
        if (request is null) {
            return Fail("directory-create-failed",
                $"directoryPicker/createDirectory 需要 {{path, name}} 参数（平铺字段）；收到 args={Describe(args)}。");
        }
        try {
            return Success(JsonSerializer.SerializeToElement(
                _services.DirectoryBrowser.CreateDirectory(request.Path, request.Name),
                DshJsonContext.Default.String));
        } catch (DshDirectoryBrowserException ex) {
            return Fail(ex.Code, ex.Message);
        }
    }

    /// <summary>fileReferences/list：会话工作目录内的 @ 引用候选（裸词模糊排序，最多 20 条）</summary>
    private async Task<DshRpcResult> FileReferencesList(JsonElement args, CancellationToken cancellationToken) {
        var request = args.ValueKind == JsonValueKind.Object
            ? args.Deserialize(DshJsonContext.Default.DshFileReferencesListArgs)
            : null;
        if (request is null || string.IsNullOrWhiteSpace(request.AgentId)) {
            return Fail("gateway/bad-request",
                $"fileReferences/list 需要 agentId（会话 ID）与 query 字符串；收到 args={Describe(args)}。query 为空表示列出工作目录根。");
        }
        try {
            return Ok(await _services.FileReferences.ListAsync(request.AgentId, request.Query ?? "", cancellationToken));
        } catch (DshFileReferenceException ex) {
            return Fail(ex.Code, ex.Message);
        }
    }

    /// <summary>session/canOpenWorkspacePath：宿主是否具备桌面文件管理器打开能力</summary>
    private static DshRpcResult CanOpenWorkspacePath(JsonElement args) => OkBool(DshWorkspacePathService.IsSupported);

    /// <summary>session/initializeDefaultModel：宿主使用本地供应商，没有账号默认模型可写，幂等无操作</summary>
    private static DshRpcResult InitializeDefaultModel(JsonElement args) {
        GuiDiag.Log("[DSH-WEB] initializeDefaultModel: 本地供应商由 settings 决定，无需初始化账号默认模型");
        return OkNull();
    }

    /// <summary>workspace/create：收养一个已存在目录（重复调用返回同一工作区）</summary>
    private async Task<DshRpcResult> WorkspaceCreate(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshWorkspacePathArgs)?.Request;
        if (request is null || string.IsNullOrWhiteSpace(request.Path)) {
            return Fail("workspace/rejected", $"workspace/create 缺少 path 参数（需要已存在的绝对目录路径）；收到 args={Describe(args)}");
        }
        if (!_services.Workspaces.DirectoryExists(request.Path)) {
            return Fail("workspace/rejected", $"workspace/create 路径不存在或不是目录：{request.Path}");
        }
        var (workspace, created) = await _services.Workspaces.CreateAsync(request.Path, cancellationToken);
        return Ok(new DshWorkspaceCreateResult { Workspace = workspace, Created = created });
    }

    /// <summary>workspace/rename：改显示名</summary>
    private async Task<DshRpcResult> WorkspaceRename(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshWorkspaceRenameArgs)?.Request;
        if (request is null || string.IsNullOrWhiteSpace(request.WorkspaceId)) {
            return Fail("workspace/rejected", "workspace/rename 缺少 workspaceId");
        }
        if (string.IsNullOrWhiteSpace(request.Title)) {
            return Fail("workspace/rejected", $"workspace/rename 缺少 title（workspaceId={request.WorkspaceId}）");
        }
        return await _services.Workspaces.RenameAsync(request.WorkspaceId, request.Title, cancellationToken) is { } workspace
            ? Ok(new DshWorkspaceResult { Workspace = workspace })
            : Fail("workspace/not-found", $"workspace/rename 找不到工作区：{request.WorkspaceId}");
    }

    /// <summary>workspace/delete：移除工作区行</summary>
    private async Task<DshRpcResult> WorkspaceDelete(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshWorkspaceIdArgs)?.Request;
        if (request is null || string.IsNullOrWhiteSpace(request.WorkspaceId)) {
            return Fail("workspace/rejected", "workspace/delete 缺少 workspaceId");
        }
        return await _services.Workspaces.DeleteAsync(request.WorkspaceId, cancellationToken)
            ? Ok(new DshWorkspaceDeletedResult())
            : Fail("workspace/not-found", $"workspace/delete 找不到工作区：{request.WorkspaceId}");
    }

    /// <summary>workspace/insertBefore：调整工作区顺序，返回完整顺序</summary>
    private async Task<DshRpcResult> WorkspaceInsertBefore(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshWorkspaceInsertBeforeArgs)?.Request;
        if (request is null || string.IsNullOrWhiteSpace(request.WorkspaceId)) {
            return Fail("workspace/rejected", "workspace/insertBefore 缺少 workspaceId");
        }
        return await _services.Workspaces.InsertBeforeAsync(request.WorkspaceId, request.BeforeWorkspaceId, cancellationToken) is { } order
            ? Ok(new DshWorkspaceOrderResult { WorkspaceIds = order })
            : Fail("workspace/not-found", $"workspace/insertBefore 找不到工作区：{request.WorkspaceId}");
    }

    /// <summary>workspace/insertSessionBefore：调整工作区内会话顺序</summary>
    private async Task<DshRpcResult> WorkspaceInsertSessionBefore(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshWorkspaceInsertSessionBeforeArgs)?.Request;
        if (request is null || string.IsNullOrWhiteSpace(request.WorkspaceId) || string.IsNullOrWhiteSpace(request.SessionId)) {
            return Fail("workspace/rejected", "workspace/insertSessionBefore 缺少 workspaceId 或 sessionId");
        }
        return await _services.Workspaces.InsertSessionBeforeAsync(request.WorkspaceId, request.SessionId, request.BeforeSessionId, cancellationToken) is { } workspace
            ? Ok(new DshWorkspaceResult { Workspace = workspace })
            : Fail("workspace/not-found", $"workspace/insertSessionBefore 找不到工作区：{request.WorkspaceId}");
    }

    /// <summary>workspace/archiveSession：归档会话，返回完整归档集合</summary>
    private async Task<DshRpcResult> WorkspaceArchiveSession(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshSessionIdArgs)?.Request;
        if (request is null || string.IsNullOrWhiteSpace(request.SessionId)) {
            return Fail("workspace/rejected", "workspace/archiveSession 缺少 sessionId");
        }
        return Ok(new DshArchivedResult { ArchivedSessionIds = await _services.Workspaces.ArchiveAsync(request.SessionId, cancellationToken) });
    }

    /// <summary>workspace/unarchiveSession：取消归档，返回完整归档集合</summary>
    private async Task<DshRpcResult> WorkspaceUnarchiveSession(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshSessionIdArgs)?.Request;
        if (request is null || string.IsNullOrWhiteSpace(request.SessionId)) {
            return Fail("workspace/rejected", "workspace/unarchiveSession 缺少 sessionId");
        }
        return Ok(new DshArchivedResult { ArchivedSessionIds = await _services.Workspaces.UnarchiveAsync(request.SessionId, cancellationToken) });
    }

    /// <summary>workspace/pinSession：置顶会话，返回完整置顶集合</summary>
    private async Task<DshRpcResult> WorkspacePinSession(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshSessionIdArgs)?.Request;
        if (request is null || string.IsNullOrWhiteSpace(request.SessionId)) {
            return Fail("workspace/rejected", "workspace/pinSession 缺少 sessionId");
        }
        return Ok(new DshPinnedResult { PinnedSessionIds = await _services.Workspaces.PinAsync(request.SessionId, cancellationToken) });
    }

    /// <summary>workspace/unpinSession：取消置顶，返回完整置顶集合</summary>
    private async Task<DshRpcResult> WorkspaceUnpinSession(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshSessionIdArgs)?.Request;
        if (request is null || string.IsNullOrWhiteSpace(request.SessionId)) {
            return Fail("workspace/rejected", "workspace/unpinSession 缺少 sessionId");
        }
        return Ok(new DshPinnedResult { PinnedSessionIds = await _services.Workspaces.UnpinAsync(request.SessionId, cancellationToken) });
    }

    /// <summary>session/list：列出引擎里的会话（引擎未就绪时为空列表）</summary>
    private async Task<DshRpcResult> SessionList(JsonElement args, CancellationToken cancellationToken) {
        var items = await _services.Sessions.ListAsync(SessionListLimit, cancellationToken);
        return Ok(new DshSessionList { Items = [.. items] });
    }

    /// <summary>session/prompt：把用户输入交给自有引擎执行（受理即返回，事件走 session/follow 流）</summary>
    private async Task<DshRpcResult> SessionPrompt(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshSessionPromptArgs)?.Request;
        if (request is null || string.IsNullOrWhiteSpace(request.SessionId)) {
            return Fail("session/rejected", $"session/prompt 缺少 sessionId；收到 args={Describe(args)}");
        }
        return await _turns.StartAsync(request, cancellationToken) is { } error
            ? Fail(error.Code, error.Message)
            : Ok(new DshSessionPromptValue());
    }

    /// <summary>session/cancel：停止该会话正在进行的生成（无轮次在跑也回 accepted，前端不区分）</summary>
    private DshRpcResult SessionCancel(JsonElement args, CancellationToken cancellationToken) {
        if (args.ValueKind != JsonValueKind.Object) {
            return Fail("session/rejected", $"session/cancel 需要 request.sessionId 参数；收到 args={Describe(args)}");
        }
        var request = args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshSessionCancelArgs)?.Request;
        if (request is null || string.IsNullOrWhiteSpace(request.SessionId)) {
            return Fail("session/rejected", $"session/cancel 缺少 sessionId；收到 args={Describe(args)}");
        }
        if (_services.Streams.Cancel(request.SessionId)) {
            GuiDiag.Log($"[DSH-WEB] cancel requested session={request.SessionId}");
        }
        return Ok(new DshSessionCancelResult());
    }

    /// <summary>session/rename：改会话标题（写引擎 custom-title）</summary>
    private async Task<DshRpcResult> SessionRename(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshSessionRenameArgs)?.Request;
        if (request is null || string.IsNullOrWhiteSpace(request.SessionId)) {
            return Fail("session/rejected", $"session/rename 缺少 sessionId；收到 args={Describe(args)}");
        }
        if (string.IsNullOrWhiteSpace(request.Title)) {
            return Fail("session/rejected", $"session/rename 缺少 title（sessionId={request.SessionId}）");
        }
        return await _services.Sessions.RenameAsync(request.SessionId, request.Title, cancellationToken) is { } renamed
            ? Ok(new DshSessionRenameResult { Title = renamed.Title, Seq = renamed.Seq })
            : Fail("session/not-found", $"session/rename 找不到会话：{request.SessionId}（引擎会话目录 ~/.jcc/sessions）");
    }

    /// <summary>
    /// session/create：在自有引擎里建会话实体（meta + 空 transcript），归入工作区后返回会话 ID。
    /// 参数按契约二选一：<c>workspaceId</c>（取工作区路径）或 <c>cwd</c>；都省略时用进程工作目录。
    /// </summary>
    private async Task<DshRpcResult> SessionCreate(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshSessionCreateArgs)?.Request;
        var projectPath = await ResolveSessionCwdAsync(request, cancellationToken);
        if (projectPath is null) {
            return Fail("session/rejected",
                $"session/create 的 workspaceId 不存在：{request?.WorkspaceId}；请先调用 workspace/create 或改用 cwd 参数；收到 args={Describe(args)}");
        }
        var catalog = _services.Models.Current;
        var created = await _services.Sessions.CreateAsync(
            request?.SessionId,
            projectPath,
            catalog.Default.Provider,
            catalog.Default.Model,
            cancellationToken);
        if (created is not { } session) {
            return Fail("session/not-ready", "session/create 需要引擎会话存储就绪；当前引擎尚未 attach，请稍后重试");
        }
        if (!string.IsNullOrWhiteSpace(request?.WorkspaceId)
            && await _services.Workspaces.AttachSessionAsync(request.WorkspaceId, session.SessionId, cancellationToken) is null) {
            GuiDiag.Log($"[DSH-WEB] session/create 会话 {session.SessionId} 未能登记到工作区 {request.WorkspaceId}");
        }
        var agentPreset = string.IsNullOrWhiteSpace(request?.AgentPreset) ? session.AgentPreset : request.AgentPreset;
        return Ok(new DshSessionCreateResult { SessionId = session.SessionId, AgentPreset = agentPreset });
    }

    /// <summary>解析会话所属工作目录：workspaceId 优先，其次 cwd，最后进程工作目录</summary>
    /// <returns>目录路径；workspaceId 未知时返回 null</returns>
    private async Task<string?> ResolveSessionCwdAsync(DshSessionCreateArgs? request, CancellationToken cancellationToken) {
        if (!string.IsNullOrWhiteSpace(request?.WorkspaceId)) {
            return (await _services.Workspaces.FindAsync(request.WorkspaceId, cancellationToken))?.Path;
        }
        return !string.IsNullOrWhiteSpace(request?.Cwd) ? request.Cwd : Environment.CurrentDirectory;
    }

    /// <summary>session/projections：单个会话的投影快照（会话不存在返回 null）</summary>
    private async Task<DshRpcResult> SessionProjections(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshSessionProjectionsArgs)?.Request;
        if (request is null || string.IsNullOrWhiteSpace(request.SessionId)) {
            return Fail("session/rejected", $"session/projections 缺少 sessionId；收到 args={Describe(args)}");
        }
        var snapshot = await _services.Sessions.ProjectionsAsync(request.SessionId, cancellationToken);
        return snapshot is null ? OkNull() : Ok(snapshot);
    }

    /// <summary>session/page：向上翻页取更早的历史记录</summary>
    private async Task<DshRpcResult> SessionPage(JsonElement args, CancellationToken cancellationToken) {
        if (args.ValueKind != JsonValueKind.Object) {
            return Fail("session/rejected", $"session/page 需要 request.address 参数；收到 args={Describe(args)}");
        }
        var request = args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshSessionPageArgs)?.Request;
        if (request?.Address is not { Kind: "session" } address || string.IsNullOrWhiteSpace(address.SessionId)) {
            return Fail("session/rejected", $"session/page 需要 request.address.sessionId（kind=session）；收到 args={Describe(args)}");
        }
        var page = await _services.Sessions.PageAsync(request, cancellationToken, _services.Streams);
        return page is null
            ? Fail("session/not-found", $"会话 {address.SessionId} 不在磁盘上；从侧栏重新选择会话后重试")
            : Ok(page);
    }

    /// <summary>session/modelCatalog：自有引擎的供应商/模型目录</summary>
    private DshRpcResult SessionModelCatalog(JsonElement args) => Ok(_services.Models.Current);

    /// <summary>
    /// session/selectModel：切换会话模型（TASK058 B2）。供应商变化时先 <c>SetVendorAsync</c>
    /// （引擎重置默认模型并持久化）再 <c>SetModelAsync</c> 覆盖；reasoningEffort 按引擎枚举值解析，
    /// 未知值忽略不报错。响应回显实际生效的供应商/模型。
    /// </summary>
    private async Task<DshRpcResult> SessionSelectModel(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshSessionSelectModelArgs)?.Request;
        if (request is null || string.IsNullOrWhiteSpace(request.SessionId)) {
            return Fail("session/rejected", $"session/selectModel 需要 {{sessionId, provider, model}}（reasoningEffort 可选）；收到 args={Describe(args)}");
        }
        if (string.IsNullOrWhiteSpace(request.Provider) || string.IsNullOrWhiteSpace(request.Model)) {
            return Fail("session/rejected", $"session/selectModel 的 provider 与 model 均为必填；收到 provider={request.Provider} model={request.Model}");
        }
        if (_services.Engine is not { } engine) {
            return Fail("session/not-ready", "引擎仍在组装中，稍后重试；若长时间未就绪查看 dumps/startup_timing.log");
        }
        if (!string.Equals(engine.CurrentVendor, request.Provider, StringComparison.OrdinalIgnoreCase)) {
            await engine.SetVendorAsync(request.Provider, cancellationToken);
        }
        await engine.SetModelAsync(request.Model, cancellationToken);
        if (request.ReasoningEffort is { } effort && EffortLevelExtensions.FromValue(effort) is { } level) {
            await engine.SetEffortLevelAsync(level, cancellationToken);
        }
        return Ok(new DshSessionSelectModelResult {
            Selected = new DshModelChoice {
                Provider = engine.CurrentVendor,
                Model = engine.CurrentModelId,
                ReasoningEffort = request.ReasoningEffort,
            },
        });
    }

    /// <summary>
    /// session/fork：分叉会话（TASK058 B7）。复制源会话 meta/转录/journal 到新 ID（v1 全量复制，
    /// atSeq 截断为深化项）；源会话不受影响。未知会话返回引导错误。
    /// </summary>
    private async Task<DshRpcResult> SessionFork(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshSessionForkArgs)?.Request;
        if (request is null || string.IsNullOrWhiteSpace(request.SessionId)) {
            return Fail("session/rejected", $"session/fork 需要 {{sessionId, atSeq?}}；收到 args={Describe(args)}");
        }
        if (!_services.Sessions.IsReady) {
            return Fail("session/not-ready", "会话存储尚未就绪（引擎仍在组装中），稍后重试即可");
        }
        var newId = await _services.Sessions.ForkAsync(request.SessionId, _services.Journal, cancellationToken);
        return newId is null
            ? Fail("session/unknown", $"会话 {request.SessionId} 不在磁盘上（~/.jcc/sessions/{request.SessionId}/transcript.json 不存在）；从侧栏选择一个已有会话再分叉")
            : Ok(new DshSessionForkResult { SessionId = newId });
    }

    /// <summary>session/search：按字面量搜索会话消息内容（最多 20 条，第 21 条置 hasMore）</summary>
    private async Task<DshRpcResult> SessionSearch(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshSessionSearchArgs)?.Request;
        if (request is null || string.IsNullOrWhiteSpace(request.Query)) {
            return Fail("gateway/bad-request",
                $"session/search 需要 request.query 且不能为空白；收到 args={Describe(args)}。请传用户输入的明文搜索词（例如 {{request:{{query:\"排序\"}}}}）。");
        }
        var (items, hasMore) = await _services.Sessions.SearchAsync(request.Query, cancellationToken);
        return Ok(new DshSessionSearchResult { Items = [.. items], HasMore = hasMore });
    }

    /// <summary>session/openWorkspacePath：资源管理器打开/选中路径（仅桌面可用处理器）</summary>
    private Task<DshRpcResult> SessionOpenWorkspacePath(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshOpenWorkspacePathArgs)?.Request;
        if (request is null || string.IsNullOrWhiteSpace(request.Path)) {
            return Task.FromResult(Fail("gateway/bad-request",
                $"session/openWorkspacePath 需要 request.path（绝对路径）；收到 args={Describe(args)}。可先调 session/workspacePathApplications 查询可用打开方式。"));
        }
        cancellationToken.ThrowIfCancellationRequested();
        var (opened, error) = DshWorkspacePathService.Open(_services.FileSystem, request.Path, request.Action, request.Application);
        return Task.FromResult(opened
            ? Ok(new DshOpenWorkspacePathResult())
            : Fail("gateway/bad-request", error ?? "session/openWorkspacePath 打开路径失败；请改用 workspaceFiles/read 预览。"));
    }

    /// <summary>session/workspacePathApplications：列出路径可用的打开方式（资源管理器）</summary>
    private Task<DshRpcResult> SessionWorkspacePathApplications(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshWorkspacePathAppsArgs)?.Request;
        if (request is null || string.IsNullOrWhiteSpace(request.Path)) {
            return Task.FromResult(Fail("gateway/bad-request",
                $"session/workspacePathApplications 需要 request.path（绝对路径）；收到 args={Describe(args)}。"));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Ok(DshWorkspacePathService.ListApplications(_services.FileSystem, request.Path)));
    }

    /// <summary>
    /// skills/list：技能清单来自引擎 ISkillService（名称/描述/模型可调用/源路径）。
    /// sessionId 仅用于协议对齐（技能集合全局共享），引擎未就绪时给出可操作引导。
    /// </summary>
    private async Task<DshRpcResult> SkillsList(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshSkillListArgs)?.Request;
        if (request is null || string.IsNullOrWhiteSpace(request.SessionId)) {
            return Fail("gateway/bad-request",
                $"skills/list 需要 request.sessionId（当前打开的会话 ID）；收到 args={Describe(args)}。示例：{{request:{{sessionId:\"<会话ID>\"}}}}");
        }
        if (_services.Engine is not { } engine) {
            return Fail("session/not-ready", "技能列表依赖引擎技能服务；引擎仍在组装中，稍后重试（进度见 dumps/startup_timing.log）。");
        }
        var skills = await engine.GetAvailableSkillsAsync(cancellationToken);
        return Ok(new DshSkillList {
            Skills = [.. skills.Select(skill => new DshSkillItem {
                Name = skill.Name,
                Description = skill.Description,
                ModelInvocable = skill.ModelInvocable,
                Path = skill.Path,
            })],
        });
    }

    /// <summary>
    /// subagents/prompt：向运行中的子代理投递一条用户消息（TASK066 C6）。
    /// 引擎侧对应 ForwardInputToSubAgentAsync（teammate 队列投递）；messageId 回显 requestId，
    /// 让前端把乐观条目与后续事件对齐。image 内容块与引擎纯文本通道不符，显式拒绝并给出替代做法。
    /// </summary>
    private async Task<DshRpcResult> SubagentPrompt(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshSubagentPromptRequest)?.Request;
        if (request is null || string.IsNullOrWhiteSpace(request.RequestId)) {
            return Fail("gateway/bad-request",
                $"subagents/prompt 需要 request.requestId（客户端消息 ID，宿主原样回显为 messageId）；收到 args={Describe(args)}");
        }
        if (string.IsNullOrWhiteSpace(request.ChildSessionId)) {
            return Fail("gateway/bad-request",
                $"subagents/prompt 需要 request.childSessionId（目标子代理会话 ID，可从 session/projections 的子代理列表获取）；收到 args={Describe(args)}");
        }
        if (request.Mode.Length > 0 && !string.Equals(request.Mode, "continuable", StringComparison.Ordinal)) {
            return Fail("gateway/bad-request",
                $"subagents/prompt 只支持 mode=continuable（可续对话子代理）；收到 mode={request.Mode}。one-shot 子代理无法接收轮外输入。");
        }
        if (request.Delivery.Length > 0
            && !string.Equals(request.Delivery, "queue", StringComparison.Ordinal)
            && !string.Equals(request.Delivery, "steer", StringComparison.Ordinal)) {
            return Fail("gateway/bad-request",
                $"subagents/prompt 的 delivery 只能是 queue 或 steer；收到 delivery={request.Delivery}");
        }
        if (request.Content.Any(part => string.Equals(part.Type, "image", StringComparison.OrdinalIgnoreCase))) {
            return Fail("session/rejected",
                "subagents/prompt 暂不支持 image 内容块：引擎子代理投递通道（ForwardUserInputToAgentAsync）只接受纯文本。"
                + "请先把图片保存到会话工作区，再用 text 块给出文件路径让子代理自行读取。");
        }
        var text = string.Join("\n", request.Content
            .Where(part => string.Equals(part.Type, "text", StringComparison.OrdinalIgnoreCase))
            .Select(part => part.Text)
            .Where(part => !string.IsNullOrWhiteSpace(part)));
        if (text.Length == 0) {
            return Fail("gateway/bad-request",
                "subagents/prompt 的 content 至少要有一个非空 text 块；收到空内容，子代理不会收到任何输入。");
        }
        if (_services.Engine is not { } engine) {
            return Fail("session/not-ready", "子代理投递依赖引擎；引擎仍在组装中，稍后重试（进度见 dumps/startup_timing.log）。");
        }
        var forwarded = await engine.ForwardInputToSubAgentAsync(request.ChildSessionId, text, cancellationToken);
        return forwarded
            ? Ok(new DshSubagentPromptReceipt { MessageId = request.RequestId })
            : Fail("subagent/not-found",
                $"子代理 {request.ChildSessionId} 不在运行中或已结束，消息未投递。请先在界面上刷新子代理列表，选择仍在运行的子代理（continuable）再发送。");
    }

    /// <summary>
    /// subagents/interruptByParent：父会话中断子代理当前工作（非终止）。引擎侧对应
    /// InterruptSubAgentAsync（teammate 进 idle，可再投递 prompt 恢复）。
    /// </summary>
    private async Task<DshRpcResult> SubagentInterruptByParent(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshSubagentInterruptArgs);
        if (request is null || string.IsNullOrWhiteSpace(request.ChildSessionId)) {
            return Fail("gateway/bad-request",
                $"subagents/interruptByParent 需要 childSessionId（目标子代理会话 ID）；收到 args={Describe(args)}");
        }
        if (_services.Engine is not { } engine) {
            return Fail("session/not-ready", "子代理中断依赖引擎；引擎仍在组装中，稍后重试（进度见 dumps/startup_timing.log）。");
        }
        var accepted = await engine.InterruptSubAgentAsync(request.ChildSessionId, cancellationToken);
        return accepted
            ? Ok(new DshSubagentInterruptReceipt())
            : Fail("subagent/not-found",
                $"子代理 {request.ChildSessionId} 不在运行中或已结束，未发生中断。请刷新子代理列表后重试。");
    }

    /// <summary>agentPresets/list：宿主内置标准代理（默认项来自 agent-preset-registry 设置）</summary>
    private async Task<DshRpcResult> AgentPresetsList(JsonElement args, CancellationToken cancellationToken) {
        await _services.Settings.InitializeAsync(cancellationToken);
        var selected = _services.Settings.ReadString("agent-preset-registry", "selectedDefault");
        var isDefault = string.IsNullOrEmpty(selected)
            || string.Equals(selected, DshSessionStore.DefaultAgentPreset, StringComparison.Ordinal);
        return Ok(new DshAgentPresetList {
            Presets = [
                new DshAgentPreset {
                    Id = DshSessionStore.DefaultAgentPreset,
                    IsDefault = isDefault,
                    Name = "标准代理",
                    Description = "宿主内置标准代理，与引擎 TUI 默认行为一致（无额外系统提示词注入）",
                },
            ],
        });
    }

    /// <summary>agentPresets/read：读取预设正文（参数是裸 JSON 字符串 agentPreset）</summary>
    private static DshRpcResult AgentPresetsRead(JsonElement args) {
        var presetId = args.ValueKind == JsonValueKind.String ? args.GetString() : null;
        if (string.IsNullOrWhiteSpace(presetId)) {
            return Fail("gateway/bad-request",
                $"agentPresets/read 的参数是预设 ID 字符串（例如 \"standard\"）；收到 args={Describe(args)}。");
        }
        if (!string.Equals(presetId, DshSessionStore.DefaultAgentPreset, StringComparison.Ordinal)) {
            return Fail("agentPreset/not-found",
                $"agentPresets/read 找不到预设 \"{presetId}\"；当前只公布 \"{DshSessionStore.DefaultAgentPreset}\"，请先调 agentPresets/list 取 ID。");
        }
        return Ok(new DshAgentPresetReadResult {
            AgentPreset = presetId,
            Content = "Standard preset: no additional instructions are injected. Tool policy, permission mode, and model selection follow the session settings.",
            Name = "标准代理",
            Description = "宿主内置标准代理，与引擎 TUI 默认行为一致",
        });
    }

    /// <summary>agentPresets/select：切换代理预设（当前仅 standard；返回实际生效值）</summary>
    private static DshRpcResult AgentPresetsSelect(JsonElement args) {
        var request = args.Deserialize(DshJsonContext.Default.DshAgentPresetSelectArgs);
        if (request is null || string.IsNullOrWhiteSpace(request.AgentId) || string.IsNullOrWhiteSpace(request.AgentPreset)) {
            return Fail("gateway/bad-request",
                $"agentPresets/select 需要 {{agentId, agentPreset}}；收到 args={Describe(args)}。");
        }
        if (!string.Equals(request.AgentPreset, DshSessionStore.DefaultAgentPreset, StringComparison.Ordinal)) {
            return Fail("agentPreset/not-found",
                $"agentPresets/select 找不到预设 \"{request.AgentPreset}\"；当前只公布 \"{DshSessionStore.DefaultAgentPreset}\"，请先调 agentPresets/list 取 ID。");
        }
        return Success(JsonSerializer.SerializeToElement(request.AgentPreset, DshJsonContext.Default.String));
    }

    /// <summary>settings/replace：整段替换命名空间（先确保存档已加载）</summary>
    private async Task<DshRpcResult> SettingsReplace(JsonElement args, CancellationToken cancellationToken) {
        await _services.Settings.InitializeAsync(cancellationToken);
        return _services.Settings.Replace(args);
    }

    /// <summary>settings/update：按顶层字段补丁合并命名空间（先确保存档已加载）</summary>
    private async Task<DshRpcResult> SettingsUpdate(JsonElement args, CancellationToken cancellationToken) {
        await _services.Settings.InitializeAsync(cancellationToken);
        return _services.Settings.Update(args);
    }

    /// <summary>messageFeedback/list：会话反馈列表（会话不存在返回契约错误联合体）</summary>
    private async Task<DshRpcResult> MessageFeedbackList(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshMessageFeedbackListRequest)?.Request;
        if (request is null || string.IsNullOrWhiteSpace(request.SessionId)) {
            return Fail("gateway/bad-request",
                $"messageFeedback/list 需要 request.sessionId；收到 args={Describe(args)}。");
        }
        if (!await _services.Sessions.ExistsAsync(request.SessionId, cancellationToken)) {
            return Ok(new DshMessageFeedbackListResult {
                Ok = false,
                Error = SerializeFeedbackError(new DshMessageFeedbackSessionNotFoundError { SessionId = request.SessionId }),
            });
        }
        await _services.Feedback.InitializeAsync(cancellationToken);
        return Ok(new DshMessageFeedbackListResult {
            Value = new DshMessageFeedbackListValue { Items = [.. _services.Feedback.List(request.SessionId)] },
        });
    }

    /// <summary>messageFeedback/put：写入/替换反馈（ifVersion 为乐观并发版本，null 期望新建）</summary>
    private async Task<DshRpcResult> MessageFeedbackPut(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshMessageFeedbackPutArgs)?.Request;
        if (request is null || string.IsNullOrWhiteSpace(request.SessionId) || string.IsNullOrWhiteSpace(request.MessageId)) {
            return Fail("gateway/bad-request",
                $"messageFeedback/put 需要 request.sessionId 与 request.messageId；收到 args={Describe(args)}。");
        }
        if (DshFeedbackRatingExtensions.FromValue(request.Rating) is null) {
            return Fail("gateway/bad-request",
                $"messageFeedback/put 的 rating 只接受 positive/negative；收到 \"{request.Rating}\"。");
        }
        if (request.Category is { } category && DshFeedbackCategoryExtensions.FromValue(category) is null) {
            return Fail("gateway/bad-request",
                $"messageFeedback/put 的 category 不是 DSH 分类词汇（other/task-result/instruction-following/product-interaction/service-stability/resource-cost/security-privacy-permission）；收到 \"{category}\"。");
        }
        if (request.Note is { } note) {
            if (string.IsNullOrWhiteSpace(note)) {
                return Ok(new DshMessageFeedbackPutResult {
                    Ok = false,
                    Error = SerializeFeedbackError(new DshMessageFeedbackNoteBlankError()),
                });
            }
            var bytes = Encoding.UTF8.GetByteCount(note);
            if (bytes > DshMessageFeedbackStore.MaxNoteBytes) {
                return Ok(new DshMessageFeedbackPutResult {
                    Ok = false,
                    Error = SerializeFeedbackError(new DshMessageFeedbackNoteTooLargeError {
                        MaxBytes = DshMessageFeedbackStore.MaxNoteBytes,
                        ActualBytes = bytes,
                    }),
                });
            }
        }
        if (!await _services.Sessions.ExistsAsync(request.SessionId, cancellationToken)) {
            return Ok(new DshMessageFeedbackPutResult {
                Ok = false,
                Error = SerializeFeedbackError(new DshMessageFeedbackSessionNotFoundError { SessionId = request.SessionId }),
            });
        }
        if (!await _services.Sessions.IsAssistantMessageAsync(request.SessionId, request.MessageId, cancellationToken)) {
            return Ok(new DshMessageFeedbackPutResult {
                Ok = false,
                Error = SerializeFeedbackError(new DshMessageFeedbackTargetNotFoundError {
                    SessionId = request.SessionId,
                    MessageId = request.MessageId,
                }),
            });
        }
        await _services.Feedback.InitializeAsync(cancellationToken);
        var outcome = _services.Feedback.Put(
            request.SessionId,
            request.MessageId,
            request.Rating,
            request.Note,
            request.Category,
            request.IfVersion);
        return outcome.Saved is null
            ? Ok(new DshMessageFeedbackPutResult {
                Ok = false,
                Error = SerializeFeedbackError(new DshMessageFeedbackVersionConflictError { Current = outcome.Current }),
            })
            : Ok(new DshMessageFeedbackPutResult { Value = outcome.Saved });
    }

    /// <summary>messageFeedback/delete：按版本删除反馈（目标已不存在返回 absent=true）</summary>
    private async Task<DshRpcResult> MessageFeedbackDelete(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshMessageFeedbackDeleteArgs)?.Request;
        if (request is null || string.IsNullOrWhiteSpace(request.SessionId) || string.IsNullOrWhiteSpace(request.MessageId)) {
            return Fail("gateway/bad-request",
                $"messageFeedback/delete 需要 request.sessionId、request.messageId 与 request.ifVersion；收到 args={Describe(args)}。");
        }
        if (!await _services.Sessions.ExistsAsync(request.SessionId, cancellationToken)) {
            return Ok(new DshMessageFeedbackDeleteResult {
                Ok = false,
                Error = SerializeFeedbackError(new DshMessageFeedbackSessionNotFoundError { SessionId = request.SessionId }),
            });
        }
        await _services.Feedback.InitializeAsync(cancellationToken);
        var outcome = _services.Feedback.Delete(request.SessionId, request.MessageId, request.IfVersion);
        return outcome.Removed
            ? Ok(new DshMessageFeedbackDeleteResult { Value = new DshMessageFeedbackDeleteValue() })
            : Ok(new DshMessageFeedbackDeleteResult {
                Ok = false,
                Error = SerializeFeedbackError(new DshMessageFeedbackVersionConflictError { Current = outcome.Current }),
            });
    }

    /// <summary>反馈错误体序列化（每类错误字段集严格对齐契约联合体）</summary>
    private static JsonElement SerializeFeedbackError(DshMessageFeedbackSessionNotFoundError error) =>
        JsonSerializer.SerializeToElement(error, DshJsonContext.Default.DshMessageFeedbackSessionNotFoundError);

    /// <summary>目标消息不存在错误体</summary>
    private static JsonElement SerializeFeedbackError(DshMessageFeedbackTargetNotFoundError error) =>
        JsonSerializer.SerializeToElement(error, DshJsonContext.Default.DshMessageFeedbackTargetNotFoundError);

    /// <summary>版本冲突错误体</summary>
    private static JsonElement SerializeFeedbackError(DshMessageFeedbackVersionConflictError error) =>
        JsonSerializer.SerializeToElement(error, DshJsonContext.Default.DshMessageFeedbackVersionConflictError);

    /// <summary>备注空白错误体</summary>
    private static JsonElement SerializeFeedbackError(DshMessageFeedbackNoteBlankError error) =>
        JsonSerializer.SerializeToElement(error, DshJsonContext.Default.DshMessageFeedbackNoteBlankError);

    /// <summary>备注超长错误体</summary>
    private static JsonElement SerializeFeedbackError(DshMessageFeedbackNoteTooLargeError error) =>
        JsonSerializer.SerializeToElement(error, DshJsonContext.Default.DshMessageFeedbackNoteTooLargeError);

    /// <summary>llm/listProviders：模型目录分组投影（与 session/modelCatalog 同源，TASK058 B2）</summary>
    private DshRpcResult LlmListProviders(JsonElement args) => Ok(_services.Models.Current.Groups
        .Select(group => new DshProviderInfo { Id = group.Id, Name = group.Name })
        .ToList());

    /// <summary>workspaceFiles 四方法共用参数解析（失败返回稳定引导错误）</summary>
    private DshRpcResult? ParseWorkspaceFilesArgs(JsonElement args, out string sessionId, out string path) {
        var request = args.Deserialize(DshJsonContext.Default.DshWorkspaceFilesArgs);
        sessionId = request?.WorkspaceFileScopeId ?? "";
        path = request?.Path ?? "";
        if (string.IsNullOrWhiteSpace(sessionId)) {
            return Fail("workspaceFiles/rejected",
                $"workspaceFiles 方法需要 workspaceFileScopeId（会话 ID）与 path；收到 args={Describe(args)}");
        }
        return null;
    }

    /// <summary>workspaceFiles/list：目录条目（目录在前按名排序）</summary>
    private async Task<DshRpcResult> WorkspaceFilesList(JsonElement args, CancellationToken cancellationToken) {
        var error = ParseWorkspaceFilesArgs(args, out var sessionId, out var path);
        if (error is not null) {
            return error;
        }
        return await _services.WorkspaceFiles.ListAsync(sessionId, path, cancellationToken);
    }

    /// <summary>workspaceFiles/read：读文本（全量 + 行数 + eof）</summary>
    private async Task<DshRpcResult> WorkspaceFilesRead(JsonElement args, CancellationToken cancellationToken) {
        var error = ParseWorkspaceFilesArgs(args, out var sessionId, out var path);
        if (error is not null) {
            return error;
        }
        return await _services.WorkspaceFiles.ReadAsync(sessionId, path, cancellationToken);
    }

    /// <summary>workspaceFiles/readBytes：读字节（base64）</summary>
    private async Task<DshRpcResult> WorkspaceFilesReadBytes(JsonElement args, CancellationToken cancellationToken) {
        var error = ParseWorkspaceFilesArgs(args, out var sessionId, out var path);
        if (error is not null) {
            return error;
        }
        return await _services.WorkspaceFiles.ReadBytesAsync(sessionId, path, cancellationToken);
    }

    /// <summary>workspaceFiles/stat：文件元数据（绝对路径 + version）</summary>
    private async Task<DshRpcResult> WorkspaceFilesStat(JsonElement args, CancellationToken cancellationToken) {
        var error = ParseWorkspaceFilesArgs(args, out var sessionId, out var path);
        if (error is not null) {
            return error;
        }
        return await _services.WorkspaceFiles.StatAsync(sessionId, path, cancellationToken);
    }

    /// <summary>fileUploads/upload：base64 附件 → 魔数校验 → receipt 落盘（TASK058 P2-B2）</summary>
    private async Task<DshRpcResult> FileUploadsUpload(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshFileUploadsUploadArgs);
        if (request is null || string.IsNullOrWhiteSpace(request.AgentId) || request.Request is null
            || string.IsNullOrEmpty(request.Request.Data)) {
            return Fail("fileUploads/rejected",
                $"fileUploads/upload 需要 {{agentId, request:{{data, name?}}}}；收到 args={Describe(args)}");
        }
        return await _services.Attachments.UploadAsync(request.AgentId, request.Request.Data, request.Request.Name, cancellationToken);
    }

    /// <summary>goals/get：会话目标（DSH agent 作用域；无目标返回 undefined，wire 上省略 value 键）</summary>
    private DshRpcResult GoalsGet(JsonElement args) {
        var request = args.Deserialize(DshJsonContext.Default.DshGoalsAgentArgs);
        if (request is null || string.IsNullOrWhiteSpace(request.AgentId)) {
            return Fail("goals/rejected", $"goals/get 需要 {{agentId}}；收到 args={Describe(args)}");
        }
        var goal = _services.Goals.Get(request.AgentId);
        return goal is null
            ? OkUndefined()
            : Success(JsonSerializer.SerializeToElement(goal, DshJsonContext.Default.DshGoal));
    }

    /// <summary>goals/create：创建目标（DSH wire {agentId, request:{objective, maxGoalRounds?}}；已存在 conflict 引导）</summary>
    private async Task<DshRpcResult> GoalsCreate(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshGoalsCreateArgs);
        if (request is null || string.IsNullOrWhiteSpace(request.AgentId) || request.Request is null) {
            return Fail("goals/rejected",
                $"goals/create 需要 {{agentId, request:{{objective, maxGoalRounds?}}}}；收到 args={Describe(args)}");
        }
        var (reference, error) = _services.Goals.Create(request.AgentId, request.Request.Objective, request.Request.MaxGoalRounds);
        if (error is not null || reference is null) {
            return error ?? Fail("goals/rejected", "创建失败");
        }
        await _services.PublishGoalProjectionAsync(request.AgentId, cancellationToken);
        return Success(JsonSerializer.SerializeToElement(new DshGoalsCreateResult { Ref = reference },
            DshJsonContext.Default.DshGoalsCreateResult));
    }

    /// <summary>goals/edit：CAS 编辑（objective/maxGoalRounds）</summary>
    private async Task<DshRpcResult> GoalsEdit(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshGoalsEditArgs);
        if (request is null || request.AgentId is not { Length: > 0 } || request.Ref is null || request.Request is null) {
            return Fail("goals/rejected", $"goals/edit 需要 {{agentId, ref, request:{{objective?, maxGoalRounds?}}}}；收到 args={Describe(args)}");
        }
        var (goal, error) = _services.Goals.Edit(request.AgentId, request.Ref, request.Request.Objective, request.Request.MaxGoalRounds);
        if (error is not null) {
            return error;
        }
        await _services.PublishGoalProjectionAsync(request.AgentId, cancellationToken);
        return Success(JsonSerializer.SerializeToElement(goal, DshJsonContext.Default.DshGoal));
    }

    /// <summary>状态迁移路由共用（ref 校验 + 委托 + 成功后推 goal 投影帧）</summary>
    private async Task<DshRpcResult> GoalsTransition(JsonElement args, Func<string, DshGoalRef, (DshGoal? Goal, DshRpcResult? Error)> action, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshGoalsRefArgs);
        if (request is null || request.AgentId is not { Length: > 0 } || request.Ref is null) {
            return Fail("goals/rejected", $"goals 方法需要 {{agentId, ref:{{id, revision}}}}；收到 args={Describe(args)}");
        }
        var (goal, error) = action(request.AgentId, request.Ref);
        if (error is not null) {
            return error;
        }
        await _services.PublishGoalProjectionAsync(request.AgentId, cancellationToken);
        return Success(JsonSerializer.SerializeToElement(goal, DshJsonContext.Default.DshGoal));
    }

    /// <summary>goals/pause</summary>
    private Task<DshRpcResult> GoalsPause(JsonElement args, CancellationToken cancellationToken) =>
        GoalsTransition(args, _services.Goals.Pause, cancellationToken);

    /// <summary>goals/resume</summary>
    private Task<DshRpcResult> GoalsResume(JsonElement args, CancellationToken cancellationToken) =>
        GoalsTransition(args, _services.Goals.Resume, cancellationToken);

    /// <summary>goals/complete</summary>
    private Task<DshRpcResult> GoalsComplete(JsonElement args, CancellationToken cancellationToken) =>
        GoalsTransition(args, _services.Goals.Complete, cancellationToken);

    /// <summary>goals/clear：清除（返回裸 GoalRef；推送 goal:null 投影帧隐藏目标条）</summary>
    private async Task<DshRpcResult> GoalsClear(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshGoalsRefArgs);
        if (request is null || request.AgentId is not { Length: > 0 } || request.Ref is null) {
            return Fail("goals/rejected", $"goals/clear 需要 {{agentId, ref:{{id, revision}}}}；收到 args={Describe(args)}");
        }
        var (reference, error) = _services.Goals.Clear(request.AgentId, request.Ref);
        if (error is not null || reference is null) {
            return error ?? Fail("goals/rejected", "清除失败");
        }
        await _services.PublishGoalProjectionAsync(request.AgentId, cancellationToken);
        return Success(JsonSerializer.SerializeToElement(reference, DshJsonContext.Default.DshGoalRef));
    }

    /// <summary>会话队列快照透传（updateQueue 响应与测试消费；internal 接缝）</summary>
    internal IReadOnlyList<DshTurnRunner.QueueItem> ListQueue(string sessionId) => _turns.ListQueue(sessionId);

    /// <summary>
    /// job/kill：停止一个后台任务（TASK066 C2）。任务已结束返回 <c>already-finished</c>；
    /// 运行中则委托引擎 <c>StopBackgroundAgentAsync</c>，受理后返回 <c>requested</c>。
    /// </summary>
    private async Task<DshRpcResult> JobKill(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshJobKillArgs)?.Request;
        if (request is null || string.IsNullOrWhiteSpace(request.SessionId) || string.IsNullOrWhiteSpace(request.JobId)) {
            return Fail("job/rejected", $"job/kill 需要 {{sessionId, jobId}}；收到 args={Describe(args)}");
        }
        var (outcome, error) = await _services.Jobs.KillAsync(request.SessionId, request.JobId, cancellationToken);
        if (error is not null) {
            return Fail(error.Code, error.Message);
        }
        return outcome is { } value
            ? Ok(new DshJobKillResult { Outcome = value })
            : Fail("job/unavailable", $"停止任务 {request.JobId} 未返回结果；请刷新任务列表确认任务状态");
    }

    /// <summary>terminal/environment：会话工作目录 + 终端限制</summary>
    private Task<DshRpcResult> TerminalEnvironment(JsonElement args, CancellationToken cancellationToken) =>
        TerminalAsync(async () => {
            var request = args.Deserialize(DshJsonContext.Default.DshTerminalAgentArgs);
            var sessionId = RequireSession(request?.AgentId, "terminal/environment");
            return JsonSerializer.SerializeToElement(
                await _services.Terminals.EnvironmentAsync(sessionId, cancellationToken),
                DshJsonContext.Default.DshTerminalEnvironmentResult);
        });

    /// <summary>terminal/shells：已安装 shell 清单（默认在最前）</summary>
    private DshRpcResult TerminalShells(JsonElement args) =>
        TerminalSync(() => {
            var request = args.Deserialize(DshJsonContext.Default.DshTerminalAgentArgs);
            RequireSession(request?.AgentId, "terminal/shells");
            return JsonSerializer.SerializeToElement(
                _services.Terminals.Shells().ToArray(), DshJsonContext.Default.DshTerminalShellProfileArray);
        });

    /// <summary>terminal/list：该会话保留的终端</summary>
    private DshRpcResult TerminalList(JsonElement args) =>
        TerminalSync(() => {
            var request = args.Deserialize(DshJsonContext.Default.DshTerminalListArgs);
            var sessionId = RequireSession(request?.SessionId, "terminal/list");
            return JsonSerializer.SerializeToElement(
                _services.Terminals.List(sessionId).ToArray(), DshJsonContext.Default.DshTerminalInfoArray);
        });

    /// <summary>terminal/create：创建或幂等返回终端</summary>
    private Task<DshRpcResult> TerminalCreate(JsonElement args, CancellationToken cancellationToken) =>
        TerminalAsync(async () => {
            var create = args.Deserialize(DshJsonContext.Default.DshTerminalCreateArgs);
            var sessionId = RequireSession(create?.AgentId, "terminal/create");
            var request = create?.Request;
            if (request is null || string.IsNullOrWhiteSpace(request.Id)) {
                throw new DshTerminalException("terminal/rejected",
                    "terminal/create 缺少 request.id（前端生成的终端标识）；请刷新页面重新打开终端面板");
            }
            return JsonSerializer.SerializeToElement(
                await _services.Terminals.CreateAsync(sessionId, request, cancellationToken),
                DshJsonContext.Default.DshTerminalInfo);
        });

    /// <summary>terminal/write：写入原始输入（仅当前 attachment）</summary>
    private DshRpcResult TerminalWrite(JsonElement args) =>
        TerminalSync(() => {
            var request = args.Deserialize(DshJsonContext.Default.DshTerminalWriteArgs);
            var sessionId = RequireSession(request?.AgentId, "terminal/write");
            var terminalId = RequireTerminalId(request?.Id, "terminal/write");
            var attachmentId = RequireAttachment(request?.AttachmentId, "terminal/write");
            _services.Terminals.Write(sessionId, terminalId, attachmentId, request?.Data ?? "");
            return JsonSerializer.SerializeToElement<object?>(null, DshJsonContext.Default.Object);
        });

    /// <summary>terminal/resize：调整 PTY 尺寸（仅当前 attachment）</summary>
    private DshRpcResult TerminalResize(JsonElement args) =>
        TerminalSync(() => {
            var request = args.Deserialize(DshJsonContext.Default.DshTerminalResizeArgs);
            var sessionId = RequireSession(request?.AgentId, "terminal/resize");
            var terminalId = RequireTerminalId(request?.Id, "terminal/resize");
            var attachmentId = RequireAttachment(request?.AttachmentId, "terminal/resize");
            _services.Terminals.Resize(sessionId, terminalId, attachmentId, request?.Cols ?? 0, request?.Rows ?? 0);
            return JsonSerializer.SerializeToElement<object?>(null, DshJsonContext.Default.Object);
        });

    /// <summary>terminal/rename：改显示标题</summary>
    private DshRpcResult TerminalRename(JsonElement args) =>
        TerminalSync(() => {
            var request = args.Deserialize(DshJsonContext.Default.DshTerminalIdArgs);
            var sessionId = RequireSession(request?.AgentId, "terminal/rename");
            var terminalId = RequireTerminalId(request?.Id, "terminal/rename");
            _services.Terminals.Rename(sessionId, terminalId, request?.Title ?? "");
            return JsonSerializer.SerializeToElement<object?>(null, DshJsonContext.Default.Object);
        });

    /// <summary>terminal/close：终止进程并移除终端（幂等）</summary>
    private Task<DshRpcResult> TerminalClose(JsonElement args, CancellationToken cancellationToken) =>
        TerminalAsync(async () => {
            var request = args.Deserialize(DshJsonContext.Default.DshTerminalIdArgs);
            var sessionId = RequireSession(request?.AgentId, "terminal/close");
            var terminalId = RequireTerminalId(request?.Id, "terminal/close");
            await _services.Terminals.CloseAsync(sessionId, terminalId).ConfigureAwait(false);
            return JsonSerializer.SerializeToElement<object?>(null, DshJsonContext.Default.Object);
        });

    /// <summary>终端处理器包装：把服务层异常映射成稳定错误码（单 try-catch）</summary>
    private async Task<DshRpcResult> TerminalAsync(Func<Task<JsonElement>> action) {
        try {
            return Success(await action());
        } catch (DshTerminalException ex) {
            return Fail(ex.Code, ex.Message);
        } catch (PlatformNotSupportedException ex) {
            return Fail("terminal/unavailable", ex.Message);
        } catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException) {
            GuiDiag.Log($"[DSH-TERMINAL] {ex.GetType().Name}: {ex.Message}");
            return Fail("terminal/unavailable", $"终端操作失败：{ex.Message}；请关闭该终端后重新打开");
        }
    }

    /// <summary>同步终端处理器包装</summary>
    private DshRpcResult TerminalSync(Func<JsonElement> action) => TerminalAsync(() => Task.FromResult(action())).GetAwaiter().GetResult();

    /// <summary>校验会话 ID（缺失 → 引导错误）</summary>
    private static string RequireSession(string? sessionId, string method) =>
        string.IsNullOrWhiteSpace(sessionId)
            ? throw new DshTerminalException("terminal/rejected", $"{method} 缺少 agentId/sessionId；请先选择一个会话再使用终端")
            : sessionId;

    /// <summary>校验终端 ID（返回非空值）</summary>
    private static string RequireTerminalId(string? id, string method) {
        if (string.IsNullOrWhiteSpace(id)) {
            throw new DshTerminalException("terminal/rejected", $"{method} 缺少 id（终端标识）；请刷新终端列表后重试");
        }
        return id;
    }

    /// <summary>校验 attachment ID（返回非空值）</summary>
    private static string RequireAttachment(string? attachmentId, string method) {
        if (string.IsNullOrWhiteSpace(attachmentId)) {
            throw new DshTerminalException("terminal/rejected", $"{method} 缺少 attachmentId；请重新打开终端面板以建立可写连接");
        }
        return attachmentId;
    }

    /// session/updateQueue：排队项编辑（TASK058 P2-B3 + B11）。edit=重写文本，remove=移除，
    /// steer=把排队项**升级为轮内注入**（出队 → 投递引擎邮箱，DSH `action.kind==='steer'` 语义）。
    /// 项不存在返回 session/queue-item-not-found；轮次未运行或引擎未装配邮箱返回
    /// session/steer-unavailable（此时该消息**仍留在队列**，不会丢）。
    /// </summary>
    private Task<DshRpcResult> SessionUpdateQueue(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshUpdateQueueArgs);
        if (request is null || string.IsNullOrWhiteSpace(request.SessionId)
            || string.IsNullOrWhiteSpace(request.ItemId) || request.Action is null) {
            return Task.FromResult(Fail("session/rejected",
                $"session/updateQueue 需要 {{sessionId, itemId, action:{{kind, content?}}}}；收到 args={Describe(args)}"));
        }
        if (string.Equals(request.Action.Kind, "steer", StringComparison.Ordinal)) {
            return Task.FromResult(SteerQueueItem(request.SessionId, request.ItemId));
        }
        if (string.Equals(request.Action.Kind, "remove", StringComparison.Ordinal)) {
            return Task.FromResult(_turns.TryRemoveQueueItem(request.SessionId, request.ItemId)
                ? Ok(new DshQueueAcceptedResult())
                : Fail("session/queue-item-not-found",
                    $"队列项 {request.ItemId} 不在队列中（可能已被下一轮 claim）；刷新队列后重试"));
        }
        if (string.Equals(request.Action.Kind, "edit", StringComparison.Ordinal)) {
            var text = string.Concat(request.Action.Content?.Select(part => part.Text ?? "") ?? []);
            if (string.IsNullOrWhiteSpace(text)) {
                return Task.FromResult(Fail("session/rejected", "edit 的 content 需要非空文本"));
            }
            return Task.FromResult(_turns.TryEditQueueItem(request.SessionId, request.ItemId, text)
                ? Ok(new DshQueueAcceptedResult())
                : Fail("session/queue-item-not-found",
                    $"队列项 {request.ItemId} 不在队列中（可能已被下一轮 claim）；刷新队列后重试"));
        }
        return Task.FromResult(Fail("session/rejected",
            $"updateQueue 的 action.kind 必须是 edit/remove/steer 之一；收到 {request.Action.Kind}"));
    }

    /// <summary>
    /// 把排队项升级为轮内注入：先确认项还在、轮次在跑、引擎能收，再出队并投递。
    /// 顺序保证失败路径**不动队列**（消息不丢，前端可继续 edit/remove/等下一轮）。
    /// </summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="itemId">排队项句柄</param>
    private DshRpcResult SteerQueueItem(string sessionId, string itemId) {
        if (_services.Engine is not { } engine) {
            return Fail("session/not-ready", "引擎仍在组装中，稍后重试；若长时间未就绪查看 dumps/startup_timing.log");
        }
        var item = _turns.ListQueue(sessionId).FirstOrDefault(entry => string.Equals(entry.ItemId, itemId, StringComparison.Ordinal));
        if (item is null) {
            return Fail("session/queue-item-not-found",
                $"队列项 {itemId} 不在队列中（可能已被下一轮 claim）；刷新队列后重试");
        }
        if (_services.Streams.RunningTurn(sessionId) == 0) {
            return Fail("session/steer-unavailable",
                $"会话 {sessionId} 当前没有运行中的轮次，轮内注入无处投放；该消息仍在队列，可用 remove 取消或等下一轮自动执行");
        }
        if (!engine.TrySteerInput(sessionId, item.Text)) {
            return Fail("session/steer-unavailable",
                $"会话 {sessionId} 的引擎未装配轮内注入邮箱（ISteerInputMailbox）；请重启宿主。该消息仍在队列，将在本轮结束后顺序执行");
        }
        _turns.TryRemoveQueueItem(sessionId, itemId);
        return Ok(new DshQueueAcceptedResult());
    }

    /// <summary>session/attachment：读附件（元数据 + base64 数据，前端聊天内图片渲染）</summary>
    private async Task<DshRpcResult> SessionAttachment(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshSessionAttachmentArgs);
        if (request is null || string.IsNullOrWhiteSpace(request.SessionId) || string.IsNullOrWhiteSpace(request.AttachmentId)) {
            return Fail("session/attachment-not-found",
                $"session/attachment 需要 {{sessionId, attachmentId}}；收到 args={Describe(args)}");
        }
        return await _services.Attachments.ReadAsync(request.SessionId, request.AttachmentId, cancellationToken);
    }

    /// <summary>commands/list：引擎斜杠命令目录投影（TASK058 B5 — 引擎 [ChatCommand] 特性为唯一数据源）</summary>
    private DshRpcResult CommandsList(JsonElement args) => Ok(
        (_services.Engine?.GetAvailableSlashCommands() ?? [])
        .Select(DshCommandsProjection.Project)
        .ToList());

    /// <summary>
    /// commands/execute：执行斜杠命令（TASK058 B5）。委托引擎 SlashCommandRunner（与 CLI/TUI 同一链路）；
    /// v1 不支持附件参数。成功与未知命令都带输出文本（前端即时反馈渲染）。
    /// <c>/plan on|off|status</c> 由宿主拦截：对齐 Claude Code 的计划模式语义，映射到引擎
    /// <see cref="PermissionMode.Plan"/> 并同步 plan/permissions 投影（TASK066 C7）。
    /// </summary>
    private async Task<DshRpcResult> CommandsExecute(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshCommandsExecuteArgs);
        if (request is null || string.IsNullOrWhiteSpace(request.AgentId) || string.IsNullOrWhiteSpace(request.Line)) {
            return Fail("commands/rejected", $"commands/execute 需要 {{agentId, line}}；收到 args={Describe(args)}（附件参数暂不支持）");
        }
        if (_services.Engine is not { } engine) {
            return Fail("commands/not-ready", "引擎仍在组装中，稍后重试；若长时间未就绪查看 dumps/startup_timing.log");
        }
        if (TryParsePlanCommand(request.Line, out var planAction)) {
            return await ExecutePlanCommandAsync(planAction, request.AgentId, engine, cancellationToken);
        }
        var output = await engine.ExecuteSlashCommandAsync(request.Line, cancellationToken);
        return Ok(new DshCommandsExecuteResult {
            CommandId = request.Line.Split(' ', 2)[0].TrimStart('/'),
            Result = new DshCommandOutcome { Kind = "success", Text = output },
        });
    }

    /// <summary>
    /// 解析宿主接管的 <c>/plan</c> 动作：裸命令或 on/enter 开启，off/exit 关闭，status 查询。
    /// 其它子参数返回 false，交还引擎 <c>PlanCommand</c>（保留 TUI 自己的计划任务能力）。
    /// </summary>
    /// <param name="line">前端命令行</param>
    /// <param name="action">on / off / status</param>
    private static bool TryParsePlanCommand(string line, out string action) {
        action = "";
        var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !string.Equals(parts[0], "/plan", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        if (parts.Length == 1) {
            action = "on";
            return true;
        }
        action = parts[1].ToLowerInvariant() switch {
            "on" or "enter" => "on",
            "off" or "exit" => "off",
            "status" => "status",
            _ => "",
        };
        return action.Length > 0;
    }

    /// <summary>执行宿主接管的一步 <c>/plan</c> 动作（引擎权限模式是应用级状态，作用于全部会话）</summary>
    private async Task<DshRpcResult> ExecutePlanCommandAsync(
        string action,
        string sessionId,
        Hosting.IJccChatSession engine,
        CancellationToken cancellationToken) {
        if (string.Equals(action, "status", StringComparison.Ordinal)) {
            var current = await engine.GetCurrentPermissionModeAsync(cancellationToken);
            var planOn = current is PermissionMode.Plan;
            return Ok(new DshCommandsExecuteResult {
                CommandId = "plan",
                Result = new DshCommandOutcome {
                    Kind = "success",
                    Text = $"计划模式：{(planOn ? "已开启" : "已关闭")}（当前权限预设 {current.ToValue()}）",
                },
            });
        }
        var preset = action == "on" ? PermissionMode.Plan.ToValue() : PermissionMode.Auto.ToValue();
        var (ok, message) = await _services.ApplyPermissionPresetAsync(preset, cancellationToken);
        if (!ok) {
            return Fail("commands/rejected", message);
        }
        return Ok(new DshCommandsExecuteResult {
            CommandId = "plan",
            Result = new DshCommandOutcome {
                Kind = "success",
                Text = action == "on"
                    ? $"计划模式已开启（会话 {sessionId}）：只读分析并给出计划，执行前需退出（/plan off）"
                    : $"计划模式已关闭（会话 {sessionId}，权限预设 {preset}）",
            },
        });
    }

    /// <summary>credentials/describe：按环境变量判断是否已配置</summary>
    private static DshRpcResult Credentials(JsonElement args) =>
        Ok(ReadStrings(args, "refs").ToDictionary(
            name => name,
            name => new DshCredentialState { Configured = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)) },
            StringComparer.Ordinal));

    /// <summary>
    /// $events/result：前端对 waterfall 帧的回帖（approval 决策等）。
    /// 载荷 {clientId, eventId, outcome}；结算结果对前端不可见（回帖 RPC 只需 ok），
    /// 未知 eventId / clientId 不符幂等返回 ok（bridge 内部记录）。
    /// </summary>
    private async Task<DshRpcResult> EventsResult(JsonElement args, CancellationToken cancellationToken) {
        var body = args.Deserialize(DshJsonContext.Default.DshEventsResultArgs);
        if (body is null || string.IsNullOrEmpty(body.EventId) || body.Outcome is null) {
            return Fail("events/result-rejected", $"$events/result 需要 {{clientId, eventId, outcome}} 三字段；收到 args={Describe(args)}");
        }
        await _services.Approvals.SettleAsync(body.ClientId, body.EventId, body.Outcome);
        await _services.Questions.SettleAsync(body.ClientId, body.EventId, body.Outcome);
        return Ok(new DshEventsResultValue());
    }

    /// <summary>
    /// userQuestions/answer：DSH 的「continued（计时等待）问题」续答入口。
    /// 宿主 ask_user_question 是阻塞式调用，答案随 <c>$events/result</c> 直接回给引擎，
    /// 不存在可续答的 continued 队列，因此诚实返回 false（该问题已不可续答）。
    /// 参数缺失时给出三字段引导，避免前端拿着空载荷反复重试。
    /// </summary>
    private static DshRpcResult UserQuestionsAnswer(JsonElement args) {
        var body = args.Deserialize(DshJsonContext.Default.DshUserQuestionsAnswerArgs);
        if (body is null || string.IsNullOrEmpty(body.CallId) || body.Answer is null) {
            return Fail("userQuestions/answer-rejected",
                $"userQuestions/answer 需要 {{agentId, callId, answer}} 三字段（answer={{answers:[{{id,selected,custom?}}]}}）；收到 args={Describe(args)}");
        }
        GuiDiag.Log($"[DSH-QUESTION] answer rpc ignored agent={body.AgentId} callId={body.CallId}: host questions settle via $events/result");
        return OkBool(false);
    }

    /// <summary>
    /// 权限预设目录 — 引擎 <see cref="PermissionMode"/> 枚举为唯一数据源（TASK058 DTO 统一原则）：
    /// 枚举 [EnumValue] 值即前端词汇（plan/auto/ask/bypass/unattended），默认取引擎当前模式，未就绪回退 auto。
    /// </summary>
    private async Task<DshRpcResult> PermissionPresetsCatalog(JsonElement args, CancellationToken cancellationToken) {
        var current = _services.Engine is { } engine
            ? await engine.GetCurrentPermissionModeAsync(cancellationToken)
            : PermissionMode.Auto;
        var options = Enum.GetValues<PermissionMode>()
            .Select(mode => new DshPresetOption { Value = mode.ToValue(), Name = mode.ToValue() })
            .ToList();
        var currentName = current.ToValue();
        return Ok(new DshPermissionPresetCatalog {
            Options = options,
            DefaultOptions = options,
            DefaultPreset = options.Any(option => option.Value == currentName) ? currentName : PermissionMode.Auto.ToValue(),
        });
    }

    private static DshRpcResult Ok(DshQueueAcceptedResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshQueueAcceptedResult));
    private static DshRpcResult Ok(DshSessionList value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSessionList));

    private static DshRpcResult Ok(DshJobKillResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshJobKillResult));

    private static DshRpcResult Ok(DshSessionRenameResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSessionRenameResult));

    private static DshRpcResult Ok(DshSessionCancelResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSessionCancelResult));

    private static DshRpcResult Ok(DshSessionPromptValue value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSessionPromptValue));

    private static DshRpcResult Ok(DshSessionCreateResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSessionCreateResult));

    private static DshRpcResult Ok(DshModelCatalog value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshModelCatalog));

    private static DshRpcResult Ok(DshSessionProjectionSnapshot value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSessionProjectionSnapshot));
    private static DshRpcResult Ok(DshSessionPageResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSessionPageResult));

    private static DshRpcResult Ok(DshAgentPresetList value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshAgentPresetList));

    private static DshRpcResult Ok(DshAgentPresetReadResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshAgentPresetReadResult));

    private static DshRpcResult Ok(DshPluginInventorySnapshot value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshPluginInventorySnapshot));

    private static DshRpcResult Ok(DshSessionSearchResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSessionSearchResult));

    private static DshRpcResult Ok(DshDirectoryListing value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshDirectoryListing));

    private static DshRpcResult Ok(IReadOnlyList<DshFileReferenceItem> value) => Success(JsonSerializer.SerializeToElement(value.ToList(), DshJsonContext.Default.ListDshFileReferenceItem));

    private static DshRpcResult OkBool(bool value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.Boolean));

    private static DshRpcResult Ok(DshOpenWorkspacePathResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshOpenWorkspacePathResult));

    private static DshRpcResult Ok(IReadOnlyList<DshWorkspacePathApp> value) => Success(JsonSerializer.SerializeToElement(value.ToList(), DshJsonContext.Default.ListDshWorkspacePathApp));

    private static DshRpcResult Ok(DshPermissionPresetCatalog value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshPermissionPresetCatalog));

    private static DshRpcResult Ok(DshEventsResultValue value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshEventsResultValue));

    private static DshRpcResult Ok(DshSettingsDescribe value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSettingsDescribe));

    private static DshRpcResult Ok(DshSkillList value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSkillList));

    private static DshRpcResult Ok(DshSubagentPromptReceipt value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSubagentPromptReceipt));

    private static DshRpcResult Ok(DshSubagentInterruptReceipt value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSubagentInterruptReceipt));

    private static DshRpcResult Ok(DshMessageFeedbackListResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshMessageFeedbackListResult));

    private static DshRpcResult Ok(DshMessageFeedbackPutResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshMessageFeedbackPutResult));

    private static DshRpcResult Ok(DshMessageFeedbackDeleteResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshMessageFeedbackDeleteResult));

    private static DshRpcResult Ok(DshWorkspaceCreateResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshWorkspaceCreateResult));

    private static DshRpcResult Ok(DshWorkspaceResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshWorkspaceResult));

    private static DshRpcResult Ok(DshWorkspaceOrderResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshWorkspaceOrderResult));

    private static DshRpcResult Ok(DshWorkspaceDeletedResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshWorkspaceDeletedResult));

    private static DshRpcResult Ok(DshArchivedResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshArchivedResult));

    private static DshRpcResult Ok(DshPinnedResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshPinnedResult));

    private static DshRpcResult Ok(DshSessionSelectModelResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSessionSelectModelResult));

    private static DshRpcResult Ok(DshSessionForkResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSessionForkResult));

    private static DshRpcResult Ok(List<DshProviderInfo> value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.ListDshProviderInfo));

    private static DshRpcResult Ok(List<DshCommandInfo> value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.ListDshCommandInfo));

    private static DshRpcResult Ok(DshCommandsExecuteResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshCommandsExecuteResult));

    private static DshRpcResult Ok(Dictionary<string, DshCredentialState> value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DictionaryStringDshCredentialState));

    private static DshRpcResult OkEmptyArray() => Success(JsonSerializer.SerializeToElement(Array.Empty<JsonElement>(), DshJsonContext.Default.JsonElementArray));

    private static DshRpcResult OkNull() => Success(JsonSerializer.SerializeToElement<object?>(null, DshJsonContext.Default.Object));

    /// <summary>成功但无返回值（DSH 的 undefined：wire 上省略 value 键，区别于显式 null 值）</summary>
    private static DshRpcResult OkUndefined() => new() { Ok = true };

    private static DshRpcResult Success(JsonElement value) => DshRpcResults.Ok(value);

    private static DshRpcResult Fail(string code, string message) => DshRpcResults.Fail(code, message);

    /// <summary>把收到的 args 原样摘出（截断），供拒绝提示里指认到底传了什么</summary>
    private static string Describe(JsonElement args) {
        if (args.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) {
            return "(空)";
        }
        var raw = args.GetRawText();
        return raw.Length <= 200 ? raw : raw[..200] + "…";
    }

    private static string? ReadString(JsonElement args, string parent, string name) =>
        args.ValueKind == JsonValueKind.Object
        && args.TryGetProperty(parent, out var node)
        && node.ValueKind == JsonValueKind.Object
        && node.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyList<string> ReadStrings(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object
        && args.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? "")
                .ToArray()
            : [];
}
