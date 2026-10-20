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

/// <summary>
/// $events 流上的 waterfall 帧（宿主→前端）。前端 dsh-api-gateway parseRemoteEventFrame 硬校验：
/// 必须且只能含 type/event/eventId/agentId/request 五键；request 禁含 agent/signal（前端 dispatchWaterfall 自行注入）。
/// </summary>
internal sealed class DshWaterfallFrame {
    /// <summary>固定为 waterfall</summary>
    public string Type { get; set; } = "waterfall";
    /// <summary>事件名（如 approval/request）</summary>
    public string Event { get; set; } = "";
    /// <summary>本次请求的关联 ID（回帖 $events/result 用）</summary>
    public string EventId { get; set; } = "";
    /// <summary>目标 Agent/会话身份（前端按此路由到对应会话 scope 的监听器）</summary>
    public string AgentId { get; set; } = "";
    /// <summary>请求载荷（事件专属结构；approval 为 toolName/reason/callId?）</summary>
    public JsonElement Request { get; set; }
}

/// <summary>$events/result 回帖参数（前端→宿主，POST /api/$events/result）</summary>
internal sealed class DshEventsResultArgs {
    /// <summary>$events 就绪帧下发的 clientId</summary>
    public string ClientId { get; set; } = "";
    /// <summary>waterfall 帧的 eventId</summary>
    public string EventId { get; set; } = "";
    /// <summary>监听器处理结果</summary>
    public DshEventOutcome? Outcome { get; set; }
}

/// <summary>waterfall 监听器处理结果（kind=next 表示无人处理；result 携带决策值；rejected 表示监听器抛错）</summary>
internal sealed class DshEventOutcome {
    /// <summary>next / result / rejected</summary>
    public string Kind { get; set; } = "";
    /// <summary>决策值（approval 域为 allowed-once / rejected）</summary>
    public JsonElement? Value { get; set; }
    /// <summary>rejected 时的错误信息</summary>
    public DshEventErrorBody? Error { get; set; }
}

/// <summary>rejected 错误体（前端 projectRemoteEventRejection 投影）</summary>
internal sealed class DshEventErrorBody {
    /// <summary>错误名</summary>
    public string Name { get; set; } = "Error";
    /// <summary>错误消息</summary>
    public string Message { get; set; } = "";
    /// <summary>稳定错误码（可选）</summary>
    public string? Code { get; set; }
}

/// <summary>$events/result 回帖响应（前端只看 ok，无业务载荷）</summary>
internal sealed class DshEventsResultValue;

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

/// <summary>workspace/create 参数</summary>
internal sealed class DshWorkspacePathArgs {
    /// <summary>要纳入工作区的绝对路径</summary>
    public string Path { get; set; } = "";
}

/// <summary>workspace/create 结果</summary>
internal sealed class DshWorkspaceCreateResult {
    /// <summary>工作区行</summary>
    public DshWorkspaceItem Workspace { get; set; } = new();
    /// <summary>是否新建（false 表示该路径已存在工作区）</summary>
    public bool Created { get; set; }
}

/// <summary>按 workspaceId 定位的参数</summary>
internal sealed class DshWorkspaceIdArgs {
    /// <summary>目标工作区</summary>
    public string WorkspaceId { get; set; } = "";
}

/// <summary>workspace/rename 参数</summary>
internal sealed class DshWorkspaceRenameArgs {
    /// <summary>目标工作区</summary>
    public string WorkspaceId { get; set; } = "";
    /// <summary>新显示名</summary>
    public string Title { get; set; } = "";
}

/// <summary>workspace/insertBefore 参数（beforeWorkspaceId 省略表示追加到末尾）</summary>
internal sealed class DshWorkspaceInsertBeforeArgs {
    /// <summary>要移动的工作区</summary>
    public string WorkspaceId { get; set; } = "";
    /// <summary>锚点工作区</summary>
    public string? BeforeWorkspaceId { get; set; }
}

/// <summary>workspace/insertSessionBefore 参数</summary>
internal sealed class DshWorkspaceInsertSessionBeforeArgs {
    /// <summary>所属工作区</summary>
    public string WorkspaceId { get; set; } = "";
    /// <summary>要移动的会话</summary>
    public string SessionId { get; set; } = "";
    /// <summary>锚点会话</summary>
    public string? BeforeSessionId { get; set; }
}

/// <summary>单工作区结果（rename / insertSessionBefore）</summary>
internal sealed class DshWorkspaceResult {
    /// <summary>工作区行</summary>
    public DshWorkspaceItem Workspace { get; set; } = new();
}

/// <summary>工作区顺序结果</summary>
internal sealed class DshWorkspaceOrderResult {
    /// <summary>完整顺序</summary>
    public List<string> WorkspaceIds { get; set; } = [];
}

/// <summary>workspace/delete 结果</summary>
internal sealed class DshWorkspaceDeletedResult {
    /// <summary>固定成功标记</summary>
    public bool Deleted { get; set; } = true;
}

/// <summary>按 sessionId 定位的参数（archive / unarchive / pin / unpin 共用）</summary>
internal sealed class DshSessionIdArgs {
    /// <summary>目标会话</summary>
    public string SessionId { get; set; } = "";
    /// <summary>归档时是否中断该会话正在运行的工作（archive 专用）</summary>
    public bool? StopActivity { get; set; }
}

/// <summary>归档集合结果</summary>
internal sealed class DshArchivedResult {
    /// <summary>完整归档集合</summary>
    public List<string> ArchivedSessionIds { get; set; } = [];
}

/// <summary>置顶集合结果</summary>
internal sealed class DshPinnedResult {
    /// <summary>完整置顶集合</summary>
    public List<string> PinnedSessionIds { get; set; } = [];
}

/// <summary>session/control 基线值</summary>
internal sealed class DshSessionControlValue {
    /// <summary>会话投影（会话 ID → 投影快照）</summary>
    public Dictionary<string, DshSessionProjectionSnapshot> Projections { get; set; } = [];
}

/// <summary>session/list 返回值</summary>
internal sealed class DshSessionList {
    /// <summary>会话条目</summary>
    public List<DshSessionListItem> Items { get; set; } = [];
}

/// <summary>会话列表条目（引擎会话投影成 DSH 形状）</summary>
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
    /// <summary>工作目录（缺失时为空串 — 该字段在协议里是可选字符串，不能为 null）</summary>
    public string Cwd { get; set; } = "";
    /// <summary>会话投影（前端据此取标题/模型选择；必须整体存在，缺失会让前端停在加载态）</summary>
    public DshSessionProjections Projections { get; set; } = new();
}

/// <summary>会话投影块（<c>kind=sequenced</c> 表示投影值挂在真实事件序号上）</summary>
internal sealed class DshSessionProjections {
    /// <summary>投影种类（sequenced / cached）</summary>
    public string Kind { get; set; } = "sequenced";
    /// <summary>投影对应的会话事件序号</summary>
    public long AsOfSeq { get; set; }
    /// <summary>投影键值</summary>
    public DshSessionProjectionValues Values { get; set; } = new();
}

/// <summary>会话投影键值（前端认得的键；未知键原样透传）</summary>
internal sealed class DshSessionProjectionValues {
    /// <summary>会话标题（null = 未命名，前端显示「未命名会话」）</summary>
    public string? Title { get; set; }
    /// <summary>代理预设</summary>
    public string? AgentPreset { get; set; }
    /// <summary>模型选择投影（缺失会让「模型选择」停在加载态）</summary>
    public DshSessionModelSelectionProjection? ModelSelection { get; set; }
    /// <summary>会话列表元数据</summary>
    public DshSessionListMetadata? SessionListMetadata { get; set; }
}

/// <summary>模型选择投影（上次使用 / 下次待用，null 表示无）</summary>
internal sealed class DshSessionModelSelectionProjection {
    /// <summary>上次使用的模型</summary>
    public DshModelSelectionRef? LastUsed { get; set; }
    /// <summary>下次将使用的模型</summary>
    public DshModelSelectionRef? Next { get; set; }
}

/// <summary>模型选择的精简形状（协议里 reasoningEffort 可选，缺失时不能输出 null）</summary>
internal sealed class DshModelSelectionRef {
    /// <summary>供应商</summary>
    public string Provider { get; set; } = "";
    /// <summary>模型</summary>
    public string Model { get; set; } = "";
}

/// <summary>会话列表元数据投影</summary>
internal sealed class DshSessionListMetadata {
    /// <summary>是否空白会话</summary>
    public bool Blank { get; set; }
    /// <summary>最后一次用户提问时间（毫秒时间戳，null = 未知）</summary>
    public long? LastPromptAt { get; set; }
}

/// <summary>session/rename 参数</summary>
internal sealed class DshSessionRenameArgs {
    /// <summary>目标会话</summary>
    public string SessionId { get; set; } = "";
    /// <summary>新标题</summary>
    public string Title { get; set; } = "";
}

/// <summary>session/rename 结果</summary>
internal sealed class DshSessionRenameResult {
    /// <summary>生效标题</summary>
    public string Title { get; set; } = "";
    /// <summary>对应的会话事件序号</summary>
    public long Seq { get; set; }
}

/// <summary>session/cancel 参数（停止该会话正在进行的生成）</summary>
internal sealed class DshSessionCancelArgs {
    /// <summary>目标会话</summary>
    public string SessionId { get; set; } = "";
}

/// <summary>session/cancel 结果</summary>
internal sealed class DshSessionCancelResult {
    /// <summary>固定接受标记</summary>
    public bool Accepted { get; set; } = true;
}

/// <summary>session/prompt 参数（前端发送一条用户输入）</summary>
internal sealed class DshSessionPromptArgs {
    /// <summary>本次提交身份（用户消息事件的 source.rpcId 必须回填它，前端据此撤掉本地待发气泡）</summary>
    public string RequestId { get; set; } = "";
    /// <summary>目标会话</summary>
    public string SessionId { get; set; } = "";
    /// <summary>提交方式（queue = 排队，steer = 打断当前轮）</summary>
    public string Mode { get; set; } = "queue";
    /// <summary>输入内容块</summary>
    public List<DshPromptContent> Content { get; set; } = [];
    /// <summary>客户端时区（可选，宿主当前不消费）</summary>
    public string? ClientTimeZone { get; set; }
}

/// <summary>session/prompt 内容块（当前只消费 text）</summary>
internal sealed class DshPromptContent {
    /// <summary>块类型（text / image / file）</summary>
    public string Type { get; set; } = "text";
    /// <summary>文本内容</summary>
    public string? Text { get; set; }
    /// <summary>图片媒体类型</summary>
    public string? MediaType { get; set; }
    /// <summary>图片数据（base64）</summary>
    public string? Data { get; set; }
    /// <summary>文件/图片名</summary>
    public string? Name { get; set; }
    /// <summary>已上传文件的回执标识</summary>
    public string? ReceiptId { get; set; }
}

/// <summary>session/prompt 结果</summary>
internal sealed class DshSessionPromptValue {
    /// <summary>固定接受标记</summary>
    public bool Accepted { get; set; } = true;
}

/// <summary>session/create 结果</summary>
internal sealed class DshSessionCreateResult {
    /// <summary>会话 ID</summary>
    public string SessionId { get; set; } = "";
    /// <summary>代理预设</summary>
    public string AgentPreset { get; set; } = "standard";
}

/// <summary>session/create 参数（workspaceId 与 cwd 二选一，都省略时用进程工作目录）</summary>
internal sealed class DshSessionCreateArgs {
    /// <summary>目标工作区（优先于 cwd）</summary>
    public string? WorkspaceId { get; set; }
    /// <summary>工作目录</summary>
    public string? Cwd { get; set; }
    /// <summary>指定会话 ID（省略则生成）</summary>
    public string? SessionId { get; set; }
    /// <summary>代理预设（省略则 standard）</summary>
    public string? AgentPreset { get; set; }
}

/// <summary>session/projections 参数</summary>
internal sealed class DshSessionProjectionsArgs {
    /// <summary>目标会话</summary>
    public string SessionId { get; set; } = "";
}

/// <summary>单个会话的投影快照（session/projections 返回；会话不存在时为 null）</summary>
internal sealed class DshSessionProjectionSnapshot {
    /// <summary>投影对应的会话事件序号</summary>
    public long AsOfSeq { get; set; }
    /// <summary>投影键值</summary>
    public DshSessionProjectionValues Values { get; set; } = new();
}

/// <summary>session/follow 参数（流式端点：打开会话事件流）</summary>
internal sealed class DshSessionFollowArgs {
    /// <summary>订阅地址（普通会话或子代理）</summary>
    public DshSessionAddress? Address { get; set; }
    /// <summary>是否订阅助手流式增量基线（前端固定传 true）</summary>
    public bool? AssistantStream { get; set; }
    /// <summary>快照最多携带的历史条数（省略表示默认窗口）</summary>
    public int? MaxMessages { get; set; }
}

/// <summary>session/page 参数：取 throughSeq 之前（不含 beforeSeq 及之后）的较早历史</summary>
internal sealed class DshSessionPageArgs {
    /// <summary>订阅地址</summary>
    public DshSessionAddress? Address { get; set; }
    /// <summary>窗口上界（含）</summary>
    public long ThroughSeq { get; set; }
    /// <summary>分页游标（不含），省略表示从最新往下取</summary>
    public long? BeforeSeq { get; set; }
    /// <summary>最多返回的记录条数</summary>
    public int? MaxMessages { get; set; }
    /// <summary>按轮次补齐的窗口（暂忽略）</summary>
    public DshTurnWindow? TurnWindow { get; set; }
}

/// <summary>按轮次补齐历史的窗口参数</summary>
internal sealed class DshTurnWindow {
    /// <summary>最少消息条数</summary>
    public int MinMessages { get; set; }
    /// <summary>最少轮次数</summary>
    public int MinTurns { get; set; }
}

/// <summary>session/page 结果</summary>
internal sealed class DshSessionPageResult {
    /// <summary>历史记录（按 seq 升序）</summary>
    public List<DshSessionRecord> Records { get; set; } = [];
    /// <summary>是否还有更早的记录</summary>
    public bool HasMore { get; set; }
}

/// <summary>会话订阅地址（kind=session 时为普通会话；子代理地址暂不支持）</summary>
internal sealed class DshSessionAddress {
    /// <summary>地址种类</summary>
    public string Kind { get; set; } = "session";
    /// <summary>持久化会话身份</summary>
    public string SessionId { get; set; } = "";
}

/// <summary>session/follow 打开快照（前端据 <c>cursor=-1</c> + 空 <c>records</c> 判定空日志）</summary>
internal sealed class DshSessionFollowSnapshot {
    /// <summary>空日志页尾光标（与前端 emptyCursor 对齐；页尾光标必须等于请求光标）</summary>
    internal const long EmptyCursor = -1;
    /// <summary>固定为 snapshot</summary>
    public string Type { get; set; } = "snapshot";
    /// <summary>会话头</summary>
    public DshSessionHeader Header { get; set; } = new();
    /// <summary>页尾光标（空日志固定 -1，与前端 emptyCursor 对齐）</summary>
    public long Cursor { get; set; } = EmptyCursor;
    /// <summary>历史事件页（由引擎转录投影：turn/step 脚手架 + 消息）</summary>
    public List<DshSessionRecord> Records { get; set; } = [];
    /// <summary>是否还有更早的历史页（空日志为 false，前端据此不再请求 session/page）</summary>
    public bool HasMore { get; set; }
    /// <summary>会话投影（协议必需；前端用来渲染标题与模型选择）</summary>
    public DshSessionProjectionSnapshot Projections { get; set; } = new();
    /// <summary>助手流基线（前端传 assistantStream:true 时必需，revision=0 表示无进行中的尝试）</summary>
    public DshAssistantStreamBaseline AssistantStream { get; set; } = new();
}

/// <summary>会话头（快照携带）</summary>
internal sealed class DshSessionHeader {
    /// <summary>头版本</summary>
    public int Version { get; set; } = 1;
    /// <summary>会话身份</summary>
    public string Id { get; set; } = "";
    /// <summary>创建时间（毫秒时间戳）</summary>
    public long CreatedAt { get; set; }
    /// <summary>工作目录</summary>
    public string? Cwd { get; set; }
    /// <summary>是否由持久化记录播种（磁盘读回的会话恒为 true）</summary>
    public bool IsSeeded { get; set; } = true;
    /// <summary>代理预设</summary>
    public string? AgentPreset { get; set; }
}

/// <summary>会话日志条目（type 固定 event）</summary>
internal sealed class DshSessionRecord {
    /// <summary>固定为 event</summary>
    public string Type { get; set; } = "event";
    /// <summary>会话事件</summary>
    public DshSessionEvent Event { get; set; } = new();
}

/// <summary>会话事件（seq 单调递增；data 为事件载荷，保持 JSON 原样透传）</summary>
internal sealed class DshSessionEvent {
    /// <summary>事件类型（如 user/message）</summary>
    public string Type { get; set; } = "";
    /// <summary>事件序号</summary>
    public long Seq { get; set; }
    /// <summary>事件时间（毫秒时间戳）</summary>
    public long Time { get; set; }
    /// <summary>事件载荷</summary>
    public JsonElement Data { get; set; }
    /// <summary>会话表面操作（消息类事件为 append，其余不输出）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SurfaceOp { get; set; }
}

/// <summary>消息内容块（当前仅文本）</summary>
internal sealed class DshWireContentBlock {
    /// <summary>块类型</summary>
    public string Type { get; set; } = "text";
    /// <summary>文本内容</summary>
    public string Text { get; set; } = "";
}

/// <summary>消息来源（用户输入或模型产出）</summary>
internal sealed class DshWireMessageSource {
    /// <summary>来源类型（user / model）</summary>
    public string Kind { get; set; } = "user";
    /// <summary>触发该消息的请求标识（用户来源）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RpcId { get; set; }
    /// <summary>供应商（模型来源）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Provider { get; set; }
    /// <summary>模型标识（模型来源）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; set; }
    /// <summary>工具调用标识（工具结果来源）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CallId { get; set; }
}

/// <summary>会话消息（user/message、assistant/message 共用形状）</summary>
internal sealed class DshWireMessage {
    /// <summary>消息身份</summary>
    public string Id { get; set; } = "";
    /// <summary>角色（user / assistant）</summary>
    public string Role { get; set; } = "";
    /// <summary>内容块</summary>
    public List<DshWireContentBlock> Content { get; set; } = [];
    /// <summary>消息来源</summary>
    public DshWireMessageSource Source { get; set; } = new();
    /// <summary>工具结果对应的调用标识（仅 tool 角色）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; set; }
    /// <summary>是否工具错误（仅 tool 角色）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsError { get; set; }
}

/// <summary>turn/start 载荷</summary>
internal sealed class DshTurnStartData {
    /// <summary>轮次</summary>
    public int Turn { get; set; }
}

/// <summary>step/start、step/end 载荷</summary>
internal sealed class DshStepData {
    /// <summary>轮次</summary>
    public int Turn { get; set; }
    /// <summary>步骤</summary>
    public int Step { get; set; }
}

/// <summary>tool/call 载荷（模型请求调用工具）</summary>
internal sealed class DshToolCallData {
    /// <summary>轮次</summary>
    public int Turn { get; set; }
    /// <summary>步骤</summary>
    public int Step { get; set; }
    /// <summary>工具调用标识</summary>
    public string CallId { get; set; } = "";
    /// <summary>工具名</summary>
    public string Name { get; set; } = "";
    /// <summary>调用参数（原始 JSON 文本）</summary>
    public string Arguments { get; set; } = "";
}

/// <summary>tool/result 载荷（工具执行结果）</summary>
internal sealed class DshToolResultData {
    /// <summary>轮次</summary>
    public int Turn { get; set; }
    /// <summary>步骤</summary>
    public int Step { get; set; }
    /// <summary>工具结果消息</summary>
    public DshWireMessage Message { get; set; } = new();
}

/// <summary>turn/end 载荷</summary>
internal sealed class DshTurnEndData {
    /// <summary>轮次</summary>
    public int Turn { get; set; }
    /// <summary>结束原因</summary>
    public DshTurnEndReason Reason { get; set; } = new();
}

/// <summary>轮次结束原因</summary>
internal sealed class DshTurnEndReason {
    /// <summary>原因类型（completed / error / aborted）</summary>
    public string Kind { get; set; } = "completed";
    /// <summary>错误详情（仅 kind=error）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DshTurnEndError? Error { get; set; }
    /// <summary>取消原因（仅 kind=aborted）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DshTurnEndCancelCause? Reason { get; set; }
}

/// <summary>轮次取消原因（`{kind:'user'}` 表示用户点了停止生成）</summary>
internal sealed class DshTurnEndCancelCause {
    /// <summary>取消来源（user / parent / hook / disposed / legacy）</summary>
    public string Kind { get; set; } = "user";
}

/// <summary>轮次失败原因</summary>
internal sealed class DshTurnEndError {
    /// <summary>错误码</summary>
    public string Code { get; set; } = "UNKNOWN";
    /// <summary>错误信息</summary>
    public string Message { get; set; } = "";
}

/// <summary>assistant/message 载荷</summary>
internal sealed class DshAssistantMessageData {
    /// <summary>轮次</summary>
    public int Turn { get; set; }
    /// <summary>步骤</summary>
    public int Step { get; set; }
    /// <summary>消息体</summary>
    public DshWireMessage Message { get; set; } = new();
    /// <summary>流式块（历史消息为空数组）</summary>
    public List<JsonElement> Stream { get; set; } = [];
}

/// <summary>助手流基线（revision=0 表示无进行中的尝试）</summary>
internal sealed class DshAssistantStreamBaseline {
    /// <summary>流版本</summary>
    public int Revision { get; set; }
}

/// <summary>模型目录</summary>
internal sealed class DshModelCatalog {
    /// <summary>默认选择</summary>
    public DshModelChoice Default { get; set; } = new();
    /// <summary>可路由供应商</summary>
    public List<string> RoutableProviders { get; set; } = [];
    /// <summary>供应商分组</summary>
    public List<DshModelGroup> Groups { get; set; } = [];
    /// <summary>读取失败的供应商（协议必需，缺失会让前端解码失败）</summary>
    public List<DshModelFailure> Failures { get; set; } = [];
}

/// <summary>模型目录读取失败项</summary>
internal sealed class DshModelFailure {
    /// <summary>供应商 ID</summary>
    public string Id { get; set; } = "";
    /// <summary>显示名</summary>
    public string Name { get; set; } = "";
    /// <summary>失败原因</summary>
    public string Message { get; set; } = "";
}

/// <summary>模型选择</summary>
internal sealed class DshModelChoice {
    /// <summary>供应商</summary>
    public string Provider { get; set; } = "";
    /// <summary>模型</summary>
    public string Model { get; set; } = "";
    /// <summary>推理强度（协议里可选 — 缺失时必须整键省略，不能输出 null）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasoningEffort { get; set; }
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
    /// <summary>命名空间视图列表</summary>
    public List<DshSettingsNamespaceView> Namespaces { get; set; } = [];
}

/// <summary>settings/mutate 参数</summary>
internal sealed class DshSettingsMutateArgs {
    /// <summary>命名空间键</summary>
    public string Ns { get; set; } = "";
    /// <summary>按序应用的编辑操作</summary>
    public List<DshSettingsOp> Ops { get; set; } = [];
    /// <summary>调用方读到的 revision；提供则做乐观并发校验</summary>
    public int? ExpectedRevision { get; set; }
}

/// <summary>单条设置编辑操作</summary>
internal sealed class DshSettingsOp {
    /// <summary>set 或 unset</summary>
    public string Op { get; set; } = "";
    /// <summary>路径段</summary>
    public List<string> Path { get; set; } = [];
    /// <summary>set 的值（unset 操作不携带；缺失即拒绝该操作）</summary>
    public JsonElement? Value { get; set; }
}

/// <summary>命名空间视图（settings/describe / settings/mutate 返回项）</summary>
internal sealed class DshSettingsNamespaceView {
    /// <summary>是否自动生成</summary>
    public bool AutoGenerate { get; set; }
    /// <summary>命名空间键</summary>
    public string Ns { get; set; } = "";
    /// <summary>值 schema（未知为 null）</summary>
    public JsonElement Schema { get; set; }
    /// <summary>生效值</summary>
    public JsonElement Value { get; set; }
    /// <summary>默认值层</summary>
    public JsonElement Base { get; set; }
    /// <summary>用户覆盖层</summary>
    public JsonElement User { get; set; }
    /// <summary>生效时机（DSH 目前仅 live）</summary>
    public string Applies { get; set; } = "live";
    /// <summary>密钥字段标记</summary>
    public List<DshSettingsSecretFlag> Secrets { get; set; } = [];
    /// <summary>写入代数</summary>
    public int Revision { get; set; }
}

/// <summary>密钥字段标记</summary>
internal sealed class DshSettingsSecretFlag {
    /// <summary>字段路径段</summary>
    public List<string> Path { get; set; } = [];
    /// <summary>是否已设置</summary>
    public bool Set { get; set; }
}

/// <summary>
/// schemastery 序列化信封（宿主 <c>schema.toJSON()</c> 的原样形状）：
/// 根节点 uid 指向 <see cref="Refs"/> 中的实际节点，节点之间同样以 uid 引用。
/// 前端 <c>configForms</c> 用 <c>new Schema(serialized)</c> 还原它校验 <c>value</c>；
/// 信封非法时命名空间停在 loading，界面（如「预览版说明」）不会渲染。
/// </summary>
internal sealed class DshSchemaEnvelope {
    /// <summary>根节点 uid</summary>
    public int Uid { get; set; }
    /// <summary>uid → 节点</summary>
    public Dictionary<string, DshSchemaNode> Refs { get; set; } = [];
}

/// <summary>schemastery 节点（type + meta + 子节点 uid 表）</summary>
internal sealed class DshSchemaNode {
    /// <summary>类型名（string / object / number / array ...）</summary>
    public string Type { get; set; } = "";
    /// <summary>元信息（default 等）</summary>
    public DshSchemaMeta Meta { get; set; } = new();
    /// <summary>对象字段名 → 子节点 uid（非对象节点为空表，schemastery 容忍）</summary>
    public Dictionary<string, int> Dict { get; set; } = [];
}

/// <summary>schemastery 元信息（只保留需回传给前端的键，未设置的键省略）</summary>
internal sealed class DshSchemaMeta {
    /// <summary>默认值（对象默认值此处只需空对象占位；volatile 已在投影阶段剔除，不回传）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DshSchemaEmptyObject? Default { get; set; }
}

/// <summary>空对象占位（序列化为 <c>{}</c>）</summary>
internal sealed class DshSchemaEmptyObject;

/// <summary>一元调用结果工厂（成功/失败信封的唯一构造点）</summary>
internal static class DshRpcResults {
    /// <summary>构造成功信封</summary>
    internal static DshRpcResult Ok(JsonElement value) => new() { Ok = true, Value = value };

    /// <summary>构造失败信封（details 恒为对象，前端按对象解析）</summary>
    internal static DshRpcResult Fail(string code, string message) => new() {
        Ok = false,
        Error = new DshRpcError {
            Code = code,
            Message = message,
            Details = JsonSerializer.SerializeToElement(new DshRpcErrorDetails(), DshJsonContext.Default.DshRpcErrorDetails),
        },
    };
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

/// <summary>消息反馈列表请求</summary>
internal sealed class DshMessageFeedbackListRequest {
    /// <summary>会话 ID</summary>
    public string SessionId { get; set; } = "";
}

/// <summary>消息反馈列表结果（Remote 契约自带成功包装：{ok:true,value:{items}}）</summary>
internal sealed class DshMessageFeedbackListResult {
    /// <summary>是否成功</summary>
    public bool Ok { get; set; } = true;
    /// <summary>成功值</summary>
    public DshMessageFeedbackListValue? Value { get; set; }
}

/// <summary>消息反馈列表值（自有引擎暂不落反馈，恒为空）</summary>
internal sealed class DshMessageFeedbackListValue {
    /// <summary>反馈条目</summary>
    public List<DshMessageFeedbackItem> Items { get; set; } = [];
}

/// <summary>单条消息反馈</summary>
internal sealed class DshMessageFeedbackItem {
    /// <summary>消息 ID</summary>
    public string MessageId { get; set; } = "";
    /// <summary>评价（positive / negative）</summary>
    public string Rating { get; set; } = "positive";
    /// <summary>版本号</summary>
    public long Version { get; set; }
    /// <summary>创建时间（Unix 毫秒）</summary>
    public long CreatedAt { get; set; }
    /// <summary>更新时间（Unix 毫秒）</summary>
    public long UpdatedAt { get; set; }
}

/// <summary>DSH 生成的远程契约：具名参数统一收在 request 下（生成器里 wire="request"）</summary>
internal sealed class DshRequestEnvelope<TRequest> where TRequest : new() {
    /// <summary>参数体</summary>
    public TRequest Request { get; set; } = new();
}

/// <summary>失败详情占位对象（协议要求 details 为对象）</summary>
internal sealed class DshRpcErrorDetails;

/// <summary>流式端点失败项（如会话不存在）</summary>
internal sealed class DshStreamError {
    /// <summary>固定为 error</summary>
    public string Type { get; set; } = "error";
    /// <summary>失败体</summary>
    public DshStreamErrorBody Error { get; set; } = new();
}

/// <summary>流式端点失败体</summary>
internal sealed class DshStreamErrorBody {
    /// <summary>稳定错误码</summary>
    public string Code { get; set; } = "";
    /// <summary>面向人（和 AI）的说明</summary>
    public string Message { get; set; } = "";
    /// <summary>附加细节</summary>
    public JsonElement Details { get; set; }
}

/// <summary>DSH 宿主协议 JSON 上下文（AOT 源生成）</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DshRpcRequest))]
[JsonSerializable(typeof(DshRpcResponse))]
[JsonSerializable(typeof(DshStreamOpen))]
[JsonSerializable(typeof(DshStreamItem))]
[JsonSerializable(typeof(DshStreamError))]
[JsonSerializable(typeof(DshStreamErrorBody))]
[JsonSerializable(typeof(DshEventsReady))]
[JsonSerializable(typeof(DshRequestEnvelope<DshWorkspacePathArgs>))]
[JsonSerializable(typeof(DshRequestEnvelope<DshSessionRenameArgs>))]
[JsonSerializable(typeof(DshRequestEnvelope<DshWorkspaceIdArgs>))]
[JsonSerializable(typeof(DshRequestEnvelope<DshWorkspaceRenameArgs>))]
[JsonSerializable(typeof(DshRequestEnvelope<DshWorkspaceInsertBeforeArgs>))]
[JsonSerializable(typeof(DshRequestEnvelope<DshWorkspaceInsertSessionBeforeArgs>))]
[JsonSerializable(typeof(DshRequestEnvelope<DshSessionIdArgs>))]
[JsonSerializable(typeof(DshWorkspaceBaseline))]
[JsonSerializable(typeof(DshWorkspacePathArgs))]
[JsonSerializable(typeof(DshWorkspaceCreateResult))]
[JsonSerializable(typeof(DshWorkspaceIdArgs))]
[JsonSerializable(typeof(DshWorkspaceRenameArgs))]
[JsonSerializable(typeof(DshWorkspaceInsertBeforeArgs))]
[JsonSerializable(typeof(DshWorkspaceInsertSessionBeforeArgs))]
[JsonSerializable(typeof(DshWorkspaceResult))]
[JsonSerializable(typeof(DshWorkspaceOrderResult))]
[JsonSerializable(typeof(DshWorkspaceDeletedResult))]
[JsonSerializable(typeof(DshSessionIdArgs))]
[JsonSerializable(typeof(DshArchivedResult))]
[JsonSerializable(typeof(DshPinnedResult))]
[JsonSerializable(typeof(DshSessionControlBaseline))]
[JsonSerializable(typeof(DshSessionList))]
[JsonSerializable(typeof(DshSessionProjections))]
[JsonSerializable(typeof(DshSessionProjectionValues))]
[JsonSerializable(typeof(DshSessionModelSelectionProjection))]
[JsonSerializable(typeof(DshModelSelectionRef))]
[JsonSerializable(typeof(DshSessionListMetadata))]
[JsonSerializable(typeof(DshSessionRenameArgs))]
[JsonSerializable(typeof(DshSessionRenameResult))]
[JsonSerializable(typeof(DshSessionCancelResult))]
[JsonSerializable(typeof(DshSessionPromptArgs))]
[JsonSerializable(typeof(DshPromptContent))]
[JsonSerializable(typeof(DshSessionPromptValue))]
[JsonSerializable(typeof(DshRequestEnvelope<DshSessionPromptArgs>))]
[JsonSerializable(typeof(DshTurnEndError))]
[JsonSerializable(typeof(DshTurnEndCancelCause))]
[JsonSerializable(typeof(DshSessionCancelArgs))]
[JsonSerializable(typeof(DshRequestEnvelope<DshSessionCancelArgs>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(DshSessionCreateResult))]
[JsonSerializable(typeof(DshRequestEnvelope<DshSessionCreateArgs>))]
[JsonSerializable(typeof(DshSessionCreateArgs))]
[JsonSerializable(typeof(DshRequestEnvelope<DshSessionProjectionsArgs>))]
[JsonSerializable(typeof(DshSessionProjectionsArgs))]
[JsonSerializable(typeof(DshSessionProjectionSnapshot))]
[JsonSerializable(typeof(DshRequestEnvelope<DshSessionFollowArgs>))]
[JsonSerializable(typeof(DshSessionFollowArgs))]
[JsonSerializable(typeof(DshRequestEnvelope<DshSessionPageArgs>))]
[JsonSerializable(typeof(DshRequestEnvelope<DshMessageFeedbackListRequest>))]
[JsonSerializable(typeof(DshSessionPageArgs))]
[JsonSerializable(typeof(DshTurnWindow))]
[JsonSerializable(typeof(DshSessionPageResult))]
[JsonSerializable(typeof(DshSessionAddress))]
[JsonSerializable(typeof(DshSessionFollowSnapshot))]
[JsonSerializable(typeof(DshSessionHeader))]
[JsonSerializable(typeof(DshSessionRecord))]
[JsonSerializable(typeof(DshSessionEvent))]
[JsonSerializable(typeof(DshWireContentBlock))]
[JsonSerializable(typeof(DshWireMessageSource))]
[JsonSerializable(typeof(DshWireMessage))]
[JsonSerializable(typeof(List<DshWireContentBlock>))]
[JsonSerializable(typeof(DshTurnStartData))]
[JsonSerializable(typeof(DshStepData))]
[JsonSerializable(typeof(DshToolCallData))]
[JsonSerializable(typeof(DshToolResultData))]
[JsonSerializable(typeof(DshTurnEndData))]
[JsonSerializable(typeof(DshTurnEndReason))]
[JsonSerializable(typeof(DshAssistantMessageData))]
[JsonSerializable(typeof(List<JsonElement>))]
[JsonSerializable(typeof(DshAssistantStreamBaseline))]
[JsonSerializable(typeof(Dictionary<string, DshSessionProjectionSnapshot>))]
[JsonSerializable(typeof(DshModelCatalog))]
[JsonSerializable(typeof(DshAgentPresetList))]
[JsonSerializable(typeof(DshPermissionPresetCatalog))]
[JsonSerializable(typeof(DshWaterfallFrame))]
[JsonSerializable(typeof(DshApprovalRequestBody))]
[JsonSerializable(typeof(DshEventsResultArgs))]
[JsonSerializable(typeof(DshEventOutcome))]
[JsonSerializable(typeof(DshEventsResultValue))]
[JsonSerializable(typeof(DshEventErrorBody))]
[JsonSerializable(typeof(DshSettingsDescribe))]
[JsonSerializable(typeof(DshSettingsNamespaceView))]
[JsonSerializable(typeof(DshSettingsMutateArgs))]
[JsonSerializable(typeof(DshSchemaEnvelope))]
[JsonSerializable(typeof(DshSkillList))]
[JsonSerializable(typeof(DshMessageFeedbackListResult))]
[JsonSerializable(typeof(DshProviderInfo))]
[JsonSerializable(typeof(List<DshProviderInfo>))]
[JsonSerializable(typeof(JsonElement[]))]
[JsonSerializable(typeof(Dictionary<string, DshCredentialState>))]
[JsonSerializable(typeof(DshRpcErrorDetails))]
[JsonSerializable(typeof(object))]
internal sealed partial class DshJsonContext : JsonSerializerContext;
