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
            ["agentPresets/list"] = Sync(_ => Ok(new DshAgentPresetList {
                Presets = [new DshAgentPreset { Id = "standard", Order = 1, IsDefault = true }],
            })),
            ["permissionPresets/catalog"] = PermissionPresetsCatalog,
            ["$events/result"] = EventsResult,
            ["settings/describe"] = Sync(_ => Ok(services.Settings.Describe())),
            ["settings/mutate"] = Sync(args => services.Settings.Mutate(args)),
            ["credentials/describe"] = Sync(Credentials),
            ["llm/listProviders"] = Sync(LlmListProviders),
            ["llm/listConfigurableProviders"] = Sync(_ => OkEmptyArray()),
            ["commands/list"] = Sync(CommandsList),
            ["commands/execute"] = CommandsExecute,
            ["messageFeedback/list"] = Sync(_ => Ok(new DshMessageFeedbackListResult { Value = new DshMessageFeedbackListValue() })),
            ["skills/list"] = Sync(_ => Ok(new DshSkillList())),
            ["dynamicCordisRunner/inventory"] = Sync(_ => OkEmptyArray()),
            ["dynamicCordisRunner/syncInspectManifest"] = Sync(_ => OkNull()),
            ["directoryPicker/pick"] = Pick,
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

    /// <summary>会话队列快照透传（updateQueue 响应与测试消费；internal 接缝）</summary>
    internal IReadOnlyList<DshTurnRunner.QueueItem> ListQueue(string sessionId) => _turns.ListQueue(sessionId);

    /// <summary>
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
    /// </summary>
    private async Task<DshRpcResult> CommandsExecute(JsonElement args, CancellationToken cancellationToken) {
        var request = args.Deserialize(DshJsonContext.Default.DshCommandsExecuteArgs);
        if (request is null || string.IsNullOrWhiteSpace(request.AgentId) || string.IsNullOrWhiteSpace(request.Line)) {
            return Fail("commands/rejected", $"commands/execute 需要 {{agentId, line}}；收到 args={Describe(args)}（附件参数暂不支持）");
        }
        if (_services.Engine is not { } engine) {
            return Fail("commands/not-ready", "引擎仍在组装中，稍后重试；若长时间未就绪查看 dumps/startup_timing.log");
        }
        var output = await engine.ExecuteSlashCommandAsync(request.Line, cancellationToken);
        return Ok(new DshCommandsExecuteResult {
            CommandId = request.Line.Split(' ', 2)[0].TrimStart('/'),
            Result = new DshCommandOutcome { Kind = "success", Text = output },
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
        return Ok(new DshEventsResultValue());
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

    private static DshRpcResult Ok(DshSessionRenameResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSessionRenameResult));

    private static DshRpcResult Ok(DshSessionCancelResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSessionCancelResult));

    private static DshRpcResult Ok(DshSessionPromptValue value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSessionPromptValue));

    private static DshRpcResult Ok(DshSessionCreateResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSessionCreateResult));

    private static DshRpcResult Ok(DshModelCatalog value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshModelCatalog));

    private static DshRpcResult Ok(DshSessionProjectionSnapshot value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSessionProjectionSnapshot));
    private static DshRpcResult Ok(DshSessionPageResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSessionPageResult));

    private static DshRpcResult Ok(DshAgentPresetList value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshAgentPresetList));

    private static DshRpcResult Ok(DshPermissionPresetCatalog value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshPermissionPresetCatalog));

    private static DshRpcResult Ok(DshEventsResultValue value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshEventsResultValue));

    private static DshRpcResult Ok(DshSettingsDescribe value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSettingsDescribe));

    private static DshRpcResult Ok(DshSkillList value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSkillList));

    private static DshRpcResult Ok(DshMessageFeedbackListResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshMessageFeedbackListResult));

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
