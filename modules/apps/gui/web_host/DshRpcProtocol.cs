namespace JoinCode.Gui.WebHost;

/// <summary>
/// DSH 前端宿主协议（ADR 0143）：从 dsh-client-connection 抓包确认的 JSON 信封。
/// 一元调用: POST /api/{method} 请求 <c>{type:"client-request", rpcId, method, payload:{args}}</c>
/// 响应: <c>{type:"server-response", rpcId, result:{ok:true, value}}</c> 或
/// <c>{..., result:{ok:false, error:{code, message, details}}}</c>。
/// </summary>
internal sealed class DshRpcRequest {
    /// <summary>固定为 client-request</summary>
    public string Type { get; set; } = "";
    /// <summary>请求关联 ID（响应原样回填）</summary>
    public string RpcId { get; set; } = "";
    /// <summary>方法名，形如 session/list</summary>
    public string Method { get; set; } = "";
    /// <summary>载荷（args 为方法参数）</summary>
    public DshRpcRequestPayload? Payload { get; set; }
}

/// <summary>请求载荷</summary>
internal sealed class DshRpcRequestPayload {
    /// <summary>方法参数（各方法自定义形状）</summary>
    public JsonElement Args { get; set; }
}

/// <summary>一元调用响应信封</summary>
internal sealed class DshRpcResponse {
    /// <summary>固定为 server-response</summary>
    public string Type { get; set; } = "server-response";
    /// <summary>与请求一致的 rpcId</summary>
    public string RpcId { get; set; } = "";
    /// <summary>结果分支</summary>
    public DshRpcResult Result { get; set; } = new();
}

/// <summary>响应结果（成功带 value，失败带 error）</summary>
internal sealed class DshRpcResult {
    /// <summary>是否成功</summary>
    public bool Ok { get; set; }
    /// <summary>成功值</summary>
    public JsonElement? Value { get; set; }
    /// <summary>失败详情</summary>
    public DshRpcError? Error { get; set; }
}

/// <summary>失败详情（code 为稳定错误码，details 必须为对象）</summary>
internal sealed class DshRpcError {
    /// <summary>稳定错误码</summary>
    public string Code { get; set; } = "";
    /// <summary>人类可读信息</summary>
    public string Message { get; set; } = "";
    /// <summary>附加细节</summary>
    public JsonElement Details { get; set; }
}

/// <summary>WebSocket 流打开请求（/api/remote.mux）</summary>
internal sealed class DshStreamOpen {
    /// <summary>固定为 open</summary>
    public string Type { get; set; } = "";
    /// <summary>流 ID（服务端在 item 中回填）</summary>
    public string StreamId { get; set; } = "";
    /// <summary>端点名，如 $events / workspace/follow / session/follow</summary>
    public string Endpoint { get; set; } = "";
    /// <summary>载荷</summary>
    public DshRpcRequestPayload? Payload { get; set; }
}

/// <summary>WebSocket 流数据项</summary>
internal sealed class DshStreamItem {
    /// <summary>固定为 item</summary>
    public string Type { get; set; } = "item";
    /// <summary>流 ID</summary>
    public string StreamId { get; set; } = "";
    /// <summary>项值（ready / baseline / snapshot / emit / error 等）</summary>
    public JsonElement Value { get; set; }
}

/// <summary>§events 就绪项</summary>
internal sealed class DshEventsReady {
    /// <summary>固定为 ready</summary>
    public string Type { get; set; } = "ready";
    /// <summary>客户端标识</summary>
    public string ClientId { get; set; } = "";
    /// <summary>宿主信息</summary>
    public DshHostInfo Host { get; set; } = new();
}

/// <summary>宿主信息</summary>
internal sealed class DshHostInfo {
    /// <summary>用户主目录</summary>
    public string Home { get; set; } = "";
}

/// <summary>workspace/follow 基线</summary>
internal sealed class DshWorkspaceBaseline {
    /// <summary>固定为 baseline</summary>
    public string Type { get; set; } = "baseline";
    /// <summary>基线值</summary>
    public DshWorkspaceBaselineValue Value { get; set; } = new();
}

/// <summary>workspace 基线值</summary>
internal sealed class DshWorkspaceBaselineValue {
    /// <summary>工作区列表</summary>
    public List<DshWorkspaceItem> Items { get; set; } = [];
    /// <summary>归档会话</summary>
    public List<string> ArchivedSessionIds { get; set; } = [];
    /// <summary>置顶会话</summary>
    public List<string> PinnedSessionIds { get; set; } = [];
}

/// <summary>工作区条目</summary>
internal sealed class DshWorkspaceItem {
    /// <summary>工作区 ID</summary>
    public string WorkspaceId { get; set; } = "";
    /// <summary>路径</summary>
    public string Path { get; set; } = "";
    /// <summary>标题</summary>
    public string Title { get; set; } = "";
    /// <summary>会话 ID 列表</summary>
    public List<string> SessionIds { get; set; } = [];
    /// <summary>创建时间</summary>
    public string CreatedAt { get; set; } = "";
    /// <summary>更新时间</summary>
    public string UpdatedAt { get; set; } = "";
}

/// <summary>session/control 基线项</summary>
internal sealed class DshSessionControlBaseline {
    /// <summary>固定为 baseline</summary>
    public string Type { get; set; } = "baseline";
    /// <summary>基线值</summary>
    public DshSessionControlValue Value { get; set; } = new();
}

/// <summary>session/control 基线值</summary>
internal sealed class DshSessionControlValue {
    /// <summary>会话投影（M1 为空）</summary>
    public Dictionary<string, JsonElement> Projections { get; set; } = [];
}

/// <summary>session/list 返回值</summary>
internal sealed class DshSessionList {
    /// <summary>会话条目</summary>
    public List<DshSessionListItem> Items { get; set; } = [];
}

/// <summary>会话列表条目（M1 只回填最小字段，后续接引擎）</summary>
internal sealed class DshSessionListItem {
    /// <summary>会话 ID</summary>
    public string SessionId { get; set; } = "";
    /// <summary>更新时间（毫秒时间戳）</summary>
    public long UpdatedAt { get; set; }
    /// <summary>代理可用</summary>
    public bool AgentAvailable { get; set; }
    /// <summary>运行中</summary>
    public bool Running { get; set; }
    /// <summary>空白会话</summary>
    public bool Blank { get; set; }
    /// <summary>工作目录</summary>
    public string Cwd { get; set; } = "";
}

/// <summary>session/create 结果</summary>
internal sealed class DshSessionCreateResult {
    /// <summary>会话 ID</summary>
    public string SessionId { get; set; } = "";
    /// <summary>代理预设</summary>
    public string AgentPreset { get; set; } = "standard";
}

/// <summary>模型目录</summary>
internal sealed class DshModelCatalog {
    /// <summary>默认选择</summary>
    public DshModelChoice Default { get; set; } = new();
    /// <summary>可路由供应商</summary>
    public List<string> RoutableProviders { get; set; } = [];
    /// <summary>供应商分组</summary>
    public List<DshModelGroup> Groups { get; set; } = [];
}

/// <summary>模型选择</summary>
internal sealed class DshModelChoice {
    /// <summary>供应商</summary>
    public string Provider { get; set; } = "";
    /// <summary>模型</summary>
    public string Model { get; set; } = "";
}

/// <summary>供应商模型分组</summary>
internal sealed class DshModelGroup {
    /// <summary>供应商 ID</summary>
    public string Id { get; set; } = "";
    /// <summary>显示名</summary>
    public string Name { get; set; } = "";
    /// <summary>模型清单</summary>
    public List<DshModelItem> Models { get; set; } = [];
}

/// <summary>模型条目</summary>
internal sealed class DshModelItem {
    /// <summary>模型 ID</summary>
    public string Id { get; set; } = "";
    /// <summary>显示名</summary>
    public string Name { get; set; } = "";
}

/// <summary>代理预设列表</summary>
internal sealed class DshAgentPresetList {
    /// <summary>预设</summary>
    public List<DshAgentPreset> Presets { get; set; } = [];
}

/// <summary>代理预设</summary>
internal sealed class DshAgentPreset {
    /// <summary>ID</summary>
    public string Id { get; set; } = "";
    /// <summary>顺序</summary>
    public int Order { get; set; }
    /// <summary>是否默认</summary>
    public bool IsDefault { get; set; }
}

/// <summary>权限预设目录</summary>
internal sealed class DshPermissionPresetCatalog {
    /// <summary>可选值</summary>
    public List<DshPresetOption> Options { get; set; } = [];
    /// <summary>默认可选值</summary>
    public List<DshPresetOption> DefaultOptions { get; set; } = [];
    /// <summary>默认预设</summary>
    public string DefaultPreset { get; set; } = "workspace-write";
}

/// <summary>预设选项</summary>
internal sealed class DshPresetOption {
    /// <summary>值</summary>
    public string Value { get; set; } = "";
    /// <summary>显示名</summary>
    public string Name { get; set; } = "";
}

/// <summary>设置描述</summary>
internal sealed class DshSettingsDescribe {
    /// <summary>是否可写</summary>
    public bool Writable { get; set; }
    /// <summary>是否有文档</summary>
    public bool HasDocument { get; set; }
    /// <summary>命名空间</summary>
    public List<JsonElement> Namespaces { get; set; } = [];
}

/// <summary>凭据状态</summary>
internal sealed class DshCredentialState {
    /// <summary>是否已配置</summary>
    public bool Configured { get; set; }
    /// <summary>来源（env 等）</summary>
    public string? Source { get; set; }
    /// <summary>是否可写</summary>
    public bool Writable { get; set; }
}

/// <summary>供应商条目</summary>
internal sealed class DshProviderInfo {
    /// <summary>ID</summary>
    public string Id { get; set; } = "";
    /// <summary>显示名</summary>
    public string Name { get; set; } = "";
}

/// <summary>技能列表</summary>
internal sealed class DshSkillList {
    /// <summary>技能</summary>
    public List<DshSkillItem> Skills { get; set; } = [];
}

/// <summary>技能条目</summary>
internal sealed class DshSkillItem {
    /// <summary>名称</summary>
    public string Name { get; set; } = "";
    /// <summary>描述</summary>
    public string Description { get; set; } = "";
    /// <summary>模型可调用</summary>
    public bool ModelInvocable { get; set; }
}

/// <summary>失败详情占位对象（协议要求 details 为对象）</summary>
internal sealed class DshRpcErrorDetails;

/// <summary>DSH 宿主协议 JSON 上下文（AOT 源生成）</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DshRpcRequest))]
[JsonSerializable(typeof(DshRpcResponse))]
[JsonSerializable(typeof(DshStreamOpen))]
[JsonSerializable(typeof(DshStreamItem))]
[JsonSerializable(typeof(DshEventsReady))]
[JsonSerializable(typeof(DshWorkspaceBaseline))]
[JsonSerializable(typeof(DshSessionControlBaseline))]
[JsonSerializable(typeof(DshSessionList))]
[JsonSerializable(typeof(DshSessionCreateResult))]
[JsonSerializable(typeof(DshModelCatalog))]
[JsonSerializable(typeof(DshAgentPresetList))]
[JsonSerializable(typeof(DshPermissionPresetCatalog))]
[JsonSerializable(typeof(DshSettingsDescribe))]
[JsonSerializable(typeof(DshSkillList))]
[JsonSerializable(typeof(DshProviderInfo))]
[JsonSerializable(typeof(List<DshProviderInfo>))]
[JsonSerializable(typeof(JsonElement[]))]
[JsonSerializable(typeof(Dictionary<string, DshCredentialState>))]
[JsonSerializable(typeof(DshRpcErrorDetails))]
[JsonSerializable(typeof(object))]
internal sealed partial class DshJsonContext : JsonSerializerContext;
