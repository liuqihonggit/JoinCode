namespace JoinCode.Gui.WebHost;

/// <summary>
/// DSH 一元 RPC 分发（ADR 0143）。M1 阶段返回前端启动所需的最小骨架数据，
/// 逐个方法在后续里程碑替换为自有 C# 引擎数据；未知方法显式失败（fail-closed + 引导）。
/// </summary>
internal static class DshRpcDispatcher {
    /// <summary>分发表：方法名 → 处理器（命名函数，便于替换与检索）</summary>
    private static readonly FrozenDictionary<string, Func<JsonElement, DshRpcResult>> Routes =
        new Dictionary<string, Func<JsonElement, DshRpcResult>>(StringComparer.Ordinal) {
            ["session/list"] = _ => Ok(new DshSessionList()),
            ["session/create"] = SessionCreate,
            ["session/modelCatalog"] = _ => Ok(new DshModelCatalog()),
            ["agentPresets/list"] = _ => Ok(new DshAgentPresetList {
                Presets = [new DshAgentPreset { Id = "standard", Order = 1, IsDefault = true }],
            }),
            ["permissionPresets/catalog"] = _ => Ok(DefaultPermissionPresets()),
            ["settings/describe"] = _ => Ok(new DshSettingsDescribe()),
            ["credentials/describe"] = Credentials,
            ["llm/listProviders"] = _ => Ok(new List<DshProviderInfo>()),
            ["llm/listConfigurableProviders"] = _ => OkEmptyArray(),
            ["commands/list"] = _ => OkEmptyArray(),
            ["skills/list"] = _ => Ok(new DshSkillList()),
            ["dynamicCordisRunner/inventory"] = _ => OkEmptyArray(),
            ["dynamicCordisRunner/syncInspectManifest"] = _ => OkNull(),
            ["workspace/list"] = _ => OkEmptyArray(),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>分发一次一元调用；未知方法返回稳定错误码（前端日志可见）</summary>
    internal static DshRpcResult Handle(string method, JsonElement args) =>
        Routes.TryGetValue(method, out var handler)
            ? handler(args)
            : Fail("unimplemented", $"宿主尚未实现方法 {method}；请在 DshRpcDispatcher.Routes 注册处理器");

    /// <summary>session/create：M1 回显前端请求的会话 ID（无则生成）</summary>
    private static DshRpcResult SessionCreate(JsonElement args) => Ok(new DshSessionCreateResult {
        SessionId = ReadString(args, "request", "sessionId") ?? $"session-{Guid.NewGuid():N}",
    });

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

    private static DshRpcResult Ok(DshSessionCreateResult value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSessionCreateResult));

    private static DshRpcResult Ok(DshModelCatalog value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshModelCatalog));

    private static DshRpcResult Ok(DshAgentPresetList value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshAgentPresetList));

    private static DshRpcResult Ok(DshPermissionPresetCatalog value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshPermissionPresetCatalog));

    private static DshRpcResult Ok(DshSettingsDescribe value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSettingsDescribe));

    private static DshRpcResult Ok(DshSkillList value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DshSkillList));

    private static DshRpcResult Ok(List<DshProviderInfo> value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.ListDshProviderInfo));

    private static DshRpcResult Ok(Dictionary<string, DshCredentialState> value) => Success(JsonSerializer.SerializeToElement(value, DshJsonContext.Default.DictionaryStringDshCredentialState));

    private static DshRpcResult OkEmptyArray() => Success(JsonSerializer.SerializeToElement(Array.Empty<JsonElement>(), DshJsonContext.Default.JsonElementArray));

    private static DshRpcResult OkNull() => Success(JsonSerializer.SerializeToElement<object?>(null, DshJsonContext.Default.Object));

    private static DshRpcResult Success(JsonElement value) => new() { Ok = true, Value = value };

    private static DshRpcResult Fail(string code, string message) => new() {
        Ok = false,
        Error = new DshRpcError {
            Code = code,
            Message = message,
            Details = JsonSerializer.SerializeToElement(new DshRpcErrorDetails(), DshJsonContext.Default.DshRpcErrorDetails),
        },
    };

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
