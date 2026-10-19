namespace JoinCode.Gui.WebHost;

/// <summary>
/// DSH 一元 RPC 分发（ADR 0143）。启动期方法返回前端所需骨架数据，workspace/* 与 settings/* 已接宿主真实状态；
/// 未实现的方法显式失败（fail-closed + 引导），便于定位与逐方法替换为自有引擎数据。
/// </summary>
internal sealed class DshRpcDispatcher {
    /// <summary>一元处理器签名（同步方法用 <see cref="Sync"/> 适配）</summary>
    private delegate Task<DshRpcResult> Handler(JsonElement args, CancellationToken cancellationToken);

    private readonly DshHostServices _services;

    /// <summary>会话列表一次返回的最大条数（前端空 cursor 即从头拉取）</summary>
    private const int SessionListLimit = 200;

    /// <summary>分发表：方法名 → 处理器（命名函数，便于替换与检索）</summary>
    private readonly FrozenDictionary<string, Handler> _routes;

    /// <summary>构造分发器</summary>
    /// <param name="services">宿主服务容器</param>
    internal DshRpcDispatcher(DshHostServices services) {
        _services = services;
        _routes = new Dictionary<string, Handler>(StringComparer.Ordinal) {
            ["session/list"] = SessionList,
            ["session/create"] = SessionCreate,
            ["session/rename"] = SessionRename,
            ["session/cancel"] = Sync(_ => Ok(new DshSessionCancelResult())),
            ["session/modelCatalog"] = Sync(_ => Ok(new DshModelCatalog())),
            ["agentPresets/list"] = Sync(_ => Ok(new DshAgentPresetList {
                Presets = [new DshAgentPreset { Id = "standard", Order = 1, IsDefault = true }],
            })),
            ["permissionPresets/catalog"] = Sync(_ => Ok(DefaultPermissionPresets())),
            ["settings/describe"] = Sync(_ => Ok(services.Settings.Describe())),
            ["settings/mutate"] = Sync(args => services.Settings.Mutate(args)),
            ["credentials/describe"] = Sync(Credentials),
            ["llm/listProviders"] = Sync(_ => Ok(new List<DshProviderInfo>())),
            ["llm/listConfigurableProviders"] = Sync(_ => OkEmptyArray()),
            ["commands/list"] = Sync(_ => OkEmptyArray()),
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

    /// <summary>session/create：M1 回显前端请求的会话 ID（无则生成）</summary>
    private static Task<DshRpcResult> SessionCreate(JsonElement args, CancellationToken cancellationToken) => Task.FromResult(Ok(new DshSessionCreateResult {
        SessionId = ReadString(args, "request", "sessionId") ?? $"session-{Guid.NewGuid():N}",
    }));

    /// <summary>credentials/describe：按环境变量判断是否已配置</summary>
    private static DshRpcResult Credentials(JsonElement args) =>
        Ok(ReadStrings(args, "refs").ToDictionary(
            name => name,
            name => new DshCredentialState { Configured = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)) },
            StringComparer.Ordinal));

    /// <summary>权限预设目录：与 DSH 默认值一致（read-only / workspace-write / danger-full-access）</summary>
    private static DshPermissionPresetCatalog DefaultPermissionPresets() {
        List<DshPresetOption> options = [
            new() { Value = "read-only", Name = "read-only" },
            new() { Value = "workspace-write", Name = "workspace-write" },
            new() { Value = "danger-full-access", Name = "danger-full-access" },
        ];
        return new DshPermissionPresetCatalog {
            Options = options,
            DefaultOptions = options,
            DefaultPreset = "workspace-write",
        };
    }

    private static DshRpcResult Ok(DshSessionList value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSessionList));

    private static DshRpcResult Ok(DshSessionRenameResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSessionRenameResult));

    private static DshRpcResult Ok(DshSessionCancelResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSessionCancelResult));

    private static DshRpcResult Ok(DshSessionCreateResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSessionCreateResult));

    private static DshRpcResult Ok(DshModelCatalog value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshModelCatalog));

    private static DshRpcResult Ok(DshAgentPresetList value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshAgentPresetList));

    private static DshRpcResult Ok(DshPermissionPresetCatalog value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshPermissionPresetCatalog));

    private static DshRpcResult Ok(DshSettingsDescribe value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSettingsDescribe));

    private static DshRpcResult Ok(DshSkillList value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSkillList));

    private static DshRpcResult Ok(DshWorkspaceCreateResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshWorkspaceCreateResult));

    private static DshRpcResult Ok(DshWorkspaceResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshWorkspaceResult));

    private static DshRpcResult Ok(DshWorkspaceOrderResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshWorkspaceOrderResult));

    private static DshRpcResult Ok(DshWorkspaceDeletedResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshWorkspaceDeletedResult));

    private static DshRpcResult Ok(DshArchivedResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshArchivedResult));

    private static DshRpcResult Ok(DshPinnedResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshPinnedResult));

    private static DshRpcResult Ok(List<DshProviderInfo> value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.ListDshProviderInfo));

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
