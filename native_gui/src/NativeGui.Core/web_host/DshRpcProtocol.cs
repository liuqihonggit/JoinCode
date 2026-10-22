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
    /// <summary>
    /// 二进制附件（不参与 JSON 序列化）：宿主转 multipart/form-data 时作为 Blob 部件，
    /// value 对应路径写 null 占位，前端 parseBinaryResponse 回填 Uint8Array。
    /// </summary>
    [JsonIgnore]
    public DshRpcBinaryAttachment? Binary { get; set; }
}

/// <summary>一元响应中的二进制附件（path 指向 value 内 null 占位字段）</summary>
internal sealed class DshRpcBinaryAttachment(string[] path, byte[] bytes) {
    /// <summary>在 value 中的字段路径（如 ["data"]）</summary>
    public string[] Path { get; } = path;
    /// <summary>原始字节</summary>
    public byte[] Bytes { get; } = bytes;
}

/// <summary>multipart 响应元数据（JSON 信封 + 附件清单；字段名与 DSH gateway 一致）</summary>
internal sealed class DshRpcMultipartMetadata {
    /// <summary>固定为 server-response</summary>
    public string Type { get; set; } = "server-response";
    /// <summary>与请求一致的 rpcId</summary>
    public string RpcId { get; set; } = "";
    /// <summary>结果分支（Value 内保留 null 占位）</summary>
    public DshRpcResult Result { get; set; } = new();
    /// <summary>附件清单</summary>
    public List<DshRpcAttachmentRef> Attachments { get; set; } = [];
}

/// <summary>multipart 附件引用（part 为 Blob 字段名）</summary>
internal sealed class DshRpcAttachmentRef {
    /// <summary>value 内字段路径</summary>
    public string[] Path { get; set; } = [];
    /// <summary>编码方式，恒为 bytes</summary>
    public string Codec { get; set; } = "bytes";
    /// <summary>Blob 部件名（如 bytes-0）</summary>
    public string Part { get; set; } = "";
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

/// <summary>
/// 会话日志的审批审计事件 <c>approval/asked</c>（对齐 DSH dsh-user-approval：审批请求必须先落审计，
/// 且与 <see cref="DshApprovalDecidedData"/> 成对、被 turn/start…turn/end 包住，否则回放时是崩溃残尾）。
/// </summary>
internal sealed class DshApprovalAskedData {
    /// <summary>审批请求 ID（与 waterfall 帧的 eventId 无关，审计对内部自洽）</summary>
    public string Id { get; set; } = "";
    /// <summary>请求审批的工具名（不可为空）</summary>
    public string ToolName { get; set; } = "";
    /// <summary>关联的工具调用 ID（可选）</summary>
    public string? CallId { get; set; }
    /// <summary>人类可读原因（可选）</summary>
    public string? Reason { get; set; }
}

/// <summary>会话日志的审批审计事件 <c>approval/decided</c>（词汇表 allowed-once/rejected/cancelled/unavailable）</summary>
internal sealed class DshApprovalDecidedData {
    /// <summary>与 approval/asked 相同的审批请求 ID</summary>
    public string Id { get; set; } = "";
    /// <summary>判定结果（allowed-once 是唯一放行值）</summary>
    public string Outcome { get; set; } = "";
}

/// <summary>fileUploads/upload 参数（前端 lookup 字段 agentId 即会话/代理 ID；request 包裹 data+name）</summary>
internal sealed class DshFileUploadsUploadArgs {
    /// <summary>目标会话/代理</summary>
    public string AgentId { get; set; } = "";
    /// <summary>上传请求体</summary>
    public DshFileUploadRequest? Request { get; set; }
}

/// <summary>上传请求体</summary>
internal sealed class DshFileUploadRequest {
    /// <summary>base64 数据</summary>
    public string Data { get; set; } = "";
    /// <summary>文件名（可选）</summary>
    public string? Name { get; set; }
}

/// <summary>fileUploads/upload 响应（receiptId 进 prompt 的 file part）</summary>
internal sealed class DshFileUploadResult {
    /// <summary>回执 ID（同时是 attachmentId）</summary>
    public string ReceiptId { get; set; } = "";
    /// <summary>文件元数据</summary>
    public DshUploadedFileInfo File { get; set; } = new();
}

/// <summary>上传文件元数据</summary>
internal sealed class DshUploadedFileInfo {
    /// <summary>附件 ID</summary>
    public string AttachmentId { get; set; } = "";
    /// <summary>文件名</summary>
    public string Name { get; set; } = "";
    /// <summary>字节数</summary>
    public int Bytes { get; set; }
}

/// <summary>附件落盘记录（{receiptId}.json）</summary>
internal sealed class DshAttachmentRecord {
    /// <summary>附件 ID</summary>
    public string AttachmentId { get; set; } = "";
    /// <summary>文件名</summary>
    public string Name { get; set; } = "";
    /// <summary>媒体类型（四类图片）</summary>
    public string MediaType { get; set; } = "";
    /// <summary>字节数</summary>
    public int Bytes { get; set; }
    /// <summary>宽</summary>
    public int Width { get; set; }
    /// <summary>高</summary>
    public int Height { get; set; }
}

/// <summary>session/attachment 参数</summary>
internal sealed class DshSessionAttachmentArgs {
    /// <summary>会话/代理（上传时的 owner）</summary>
    public string SessionId { get; set; } = "";
    /// <summary>附件 ID</summary>
    public string AttachmentId { get; set; } = "";
}

/// <summary>session/attachment 响应（前端聊天内图片渲染）</summary>
internal sealed class DshSessionAttachmentResult {
    /// <summary>附件元数据</summary>
    public DshAttachmentInfo Attachment { get; set; } = new();
    /// <summary>base64 数据</summary>
    public string Data { get; set; } = "";
}

/// <summary>附件元数据</summary>
internal sealed class DshAttachmentInfo {
    /// <summary>附件 ID</summary>
    public string AttachmentId { get; set; } = "";
    /// <summary>媒体类型</summary>
    public string MediaType { get; set; } = "";
    /// <summary>字节数</summary>
    public int Bytes { get; set; }
    /// <summary>宽</summary>
    public int Width { get; set; }
    /// <summary>高</summary>
    public int Height { get; set; }
    /// <summary>文件名</summary>
    public string? Name { get; set; }
}

/// <summary>session/updateQueue 参数（QueueDock 行操作）</summary>
internal sealed class DshUpdateQueueArgs {
    /// <summary>会话 ID</summary>
    public string SessionId { get; set; } = "";
    /// <summary>队列项 ID（入队时分配的 GUID）</summary>
    public string ItemId { get; set; } = "";
    /// <summary>操作（edit/remove/steer）</summary>
    public DshUpdateQueueAction? Action { get; set; }
}

/// <summary>队列操作</summary>
internal sealed class DshUpdateQueueAction {
    /// <summary>操作类型</summary>
    public string Kind { get; set; } = "";
    /// <summary>edit 的新内容（text part 数组；其他 kind 恒空）</summary>
    public List<DshPromptContent> Content { get; set; } = [];
}

/// <summary>updateQueue 受理响应</summary>
internal sealed class DshQueueAcceptedResult {
    /// <summary>受理标志</summary>
    public bool Accepted => true;
}

/// <summary>workspaceFiles 四方法共用参数（前端 lookup 字段 workspaceFileScopeId 即 sessionId）</summary>
internal sealed class DshWorkspaceFilesArgs {
    /// <summary>会话 ID（文件树根 = 会话 cwd）</summary>
    public string WorkspaceFileScopeId { get; set; } = "";
    /// <summary>相对路径（空 = 根目录）</summary>
    public string Path { get; set; } = "";
}

/// <summary>workspaceFiles/list 响应</summary>
internal sealed class DshWorkspaceFilesListResult {
    /// <summary>请求的相对路径（回显）</summary>
    public string Path { get; set; } = "";
    /// <summary>目录条目（目录在前按名排序）</summary>
    public List<DshWorkspaceFileEntry> Entries { get; set; } = [];
    /// <summary>是否超过截断阈值</summary>
    public bool Truncated { get; set; }
}

/// <summary>文件树条目</summary>
internal sealed class DshWorkspaceFileEntry {
    /// <summary>名称（不含路径）</summary>
    public string Name { get; set; } = "";
    /// <summary>类型（file/directory/other）</summary>
    public string Type { get; set; } = "file";
    /// <summary>字节数（文件可选）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Size { get; set; }
}

/// <summary>workspaceFiles/read 响应</summary>
internal sealed class DshWorkspaceFilesReadResult {
    /// <summary>文本起始偏移（v1 恒 0）</summary>
    public int Offset { get; set; }
    /// <summary>文本内容</summary>
    public string Text { get; set; } = "";
    /// <summary>行数</summary>
    public int Lines { get; set; }
    /// <summary>是否读到末尾</summary>
    public bool Eof { get; set; }
    /// <summary>绝对路径</summary>
    public string AbsolutePath { get; set; } = "";
    /// <summary>版本（size-mtime 合成，前端透传比较失效）</summary>
    public string Version { get; set; } = "";
}

/// <summary>workspaceFiles/readBytes 响应（data 走 multipart 二进制附件，JSON 中恒为 null 占位）</summary>
internal sealed class DshWorkspaceFilesReadBytesResult {
    /// <summary>字节起始偏移（v1 恒 0）</summary>
    public int Offset { get; set; }
    /// <summary>null 占位：真实字节由 result.attachments[path=["data"]] 的 Blob 部件回填</summary>
    public JsonElement? Data { get; set; }
    /// <summary>是否读到末尾</summary>
    public bool Eof { get; set; }
    /// <summary>绝对路径</summary>
    public string AbsolutePath { get; set; } = "";
    /// <summary>版本</summary>
    public string Version { get; set; } = "";
}

/// <summary>goals/get 响应（无目标时 null）</summary>
internal sealed class DshGoal {
    /// <summary>目标 ID（GUID）</summary>
    public string Id { get; set; } = "";
    /// <summary>版本（每次变更 +1，CAS 用）</summary>
    public int Revision { get; set; }
    /// <summary>目标描述</summary>
    public string Objective { get; set; } = "";
    /// <summary>阶段（active/paused/complete）</summary>
    public string Phase { get; set; } = "active";
    /// <summary>武装状态（armed=驱动轮次；disarmed=暂停驱动）</summary>
    public string Activation { get; set; } = "armed";
    /// <summary>已启动轮次（prompt 受理递增）</summary>
    public int RoundsStarted { get; set; }
    /// <summary>轮次上限</summary>
    public int MaxGoalRounds { get; set; }
    /// <summary>创建时间（Unix 毫秒）</summary>
    public long CreatedAt { get; set; }
    /// <summary>更新时间（Unix 毫秒）</summary>
    public long UpdatedAt { get; set; }
}

/// <summary>目标引用（CAS 句柄：id + 期望 revision）</summary>
internal sealed class DshGoalRef {
    /// <summary>目标 ID</summary>
    public string Id { get; set; } = "";
    /// <summary>期望版本（须与当前一致否则 conflict）</summary>
    public int Revision { get; set; }
}

/// <summary>goals/get|pause|resume|complete 共用参数（ref 由各方法携带）</summary>
internal sealed class DshGoalsRefArgs {
    /// <summary>会话 ID</summary>
    public string SessionId { get; set; } = "";
    /// <summary>目标引用</summary>
    public DshGoalRef? Ref { get; set; }
}

/// <summary>goals/create 参数</summary>
internal sealed class DshGoalsCreateArgs {
    /// <summary>会话 ID</summary>
    public string SessionId { get; set; } = "";
    /// <summary>目标描述</summary>
    public string Objective { get; set; } = "";
    /// <summary>轮次上限（可选，默认 10）</summary>
    public int? MaxGoalRounds { get; set; }
}

/// <summary>goals/create 响应</summary>
internal sealed class DshGoalsCreateResult {
    /// <summary>新目标引用</summary>
    public DshGoalRef Ref { get; set; } = new();
}

/// <summary>goals/edit 参数</summary>
internal sealed class DshGoalsEditArgs {
    /// <summary>会话 ID</summary>
    public string SessionId { get; set; } = "";
    /// <summary>目标引用（CAS）</summary>
    public DshGoalRef? Ref { get; set; }
    /// <summary>变更内容</summary>
    public DshGoalChanges? Changes { get; set; }
}

/// <summary>目标变更内容（均可选）</summary>
internal sealed class DshGoalChanges {
    /// <summary>新目标描述</summary>
    public string? Objective { get; set; }
    /// <summary>新轮次上限</summary>
    public int? MaxGoalRounds { get; set; }
}

/// <summary>goals/clear 响应</summary>
internal sealed class DshGoalsClearResult {
    /// <summary>被清目标引用（revision 为清算后值）</summary>
    public DshGoalRef Ref { get; set; } = new();
}

/// <summary>目标落盘文档（~/.jcc/dsh_goals.json 条目）</summary>
internal sealed record DshGoalDocument {
    /// <summary>目标 ID</summary>
    public string Id { get; init; } = "";
    /// <summary>版本</summary>
    public int Revision { get; init; }
    /// <summary>目标描述</summary>
    public string Objective { get; init; } = "";
    /// <summary>阶段</summary>
    public string Phase { get; init; } = "active";
    /// <summary>武装状态</summary>
    public string Activation { get; init; } = "armed";
    /// <summary>已启动轮次</summary>
    public int RoundsStarted { get; init; }
    /// <summary>轮次上限</summary>
    public int MaxGoalRounds { get; init; }
    /// <summary>创建时间（Unix 毫秒）</summary>
    public long CreatedAt { get; init; }
    /// <summary>更新时间（Unix 毫秒）</summary>
    public long UpdatedAt { get; init; }
}

/// <summary>goal/activation-changed 事件载荷（前端 $on 监听）</summary>
internal sealed class DshGoalActivationChanged {
    /// <summary>会话 ID</summary>
    public string SessionId { get; set; } = "";
    /// <summary>目标状态摘要</summary>
    public DshGoalActivation Goal { get; set; } = new();
}

/// <summary>目标激活状态摘要</summary>
internal sealed class DshGoalActivation {
    /// <summary>目标 ID</summary>
    public string Id { get; set; } = "";
    /// <summary>版本</summary>
    public int Revision { get; set; }
    /// <summary>武装状态（armed/disarmed）</summary>
    public string Activation { get; set; } = "";
}

/// <summary>dsh_goals.json 根文档</summary>
internal sealed class DshGoalsDocument {
    /// <summary>会话 → 目标</summary>
    public Dictionary<string, DshGoalDocument> Goals { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>workspaceFiles/changes 就绪帧（kind=ready — 无附加载荷）</summary>
internal sealed class DshChangeReady {
    /// <summary>帧类型（ready）</summary>
    public string Kind => "ready";
}

/// <summary>workspaceFiles/changes 变更帧（kind=change；ready 帧由宿主直接发）</summary>
internal sealed class DshFileChangeFrame {
    /// <summary>帧类型（change）</summary>
    public string Kind => "change";
    /// <summary>变更详情</summary>
    public DshFileChange Change { get; set; } = new();
}

/// <summary>单条文件变更（version 与 absent 互斥 — 删除无版本）</summary>
internal sealed class DshFileChange {
    /// <summary>绝对路径</summary>
    public string AbsolutePath { get; set; } = "";
    /// <summary>新版本（size-mtime 合成；删除时省略）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; set; }
    /// <summary>是否已删除（删除事件为 true）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Absent { get; set; }
}

/// <summary>workspaceFiles/stat 响应</summary>
internal sealed class DshWorkspaceFilesStatResult {
    /// <summary>绝对路径</summary>
    public string AbsolutePath { get; set; } = "";
    /// <summary>版本</summary>
    public string Version { get; set; } = "";
}

/// <summary>commands/list 条目（前端以 /{name} 组装命令行）</summary>
internal sealed class DshCommandInfo {
    /// <summary>命令名（无 / 前缀）</summary>
    public string Name { get; set; } = "";
    /// <summary>描述</summary>
    public string Description { get; set; } = "";
    /// <summary>输入提示（可选）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DshCommandInputInfo? Input { get; set; }
}

/// <summary>命令输入提示</summary>
internal sealed class DshCommandInputInfo {
    /// <summary>参数提示（如 model-id）</summary>
    public string Hint { get; set; } = "";
}

/// <summary>commands/execute 参数（位置参数 wire 平铺：agentId + line；attachments v1 不支持）</summary>
internal sealed class DshCommandsExecuteArgs {
    /// <summary>目标会话（scope agent）</summary>
    public string AgentId { get; set; } = "";
    /// <summary>命令行（如 /model deepseek-v4-pro）</summary>
    public string? Line { get; set; }
}

/// <summary>commands/execute 响应 — commandId + 成败结果</summary>
internal sealed class DshCommandsExecuteResult {
    /// <summary>命令标识</summary>
    public string CommandId { get; set; } = "";
    /// <summary>执行结果</summary>
    public DshCommandOutcome Result { get; set; } = new();
}

/// <summary>命令执行结果体（success 带输出文本，error 带提示文本）</summary>
internal sealed class DshCommandOutcome {
    /// <summary>success / error</summary>
    public string Kind { get; set; } = "success";
    /// <summary>输出或错误文本（可选）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }
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
    /// <summary>子代理实例目录（undefined 会让前端子代理面板永久 loading，必须输出数组；TASK058 B8）</summary>
    public List<DshSubagentCatalogEntry> SubagentCatalog { get; set; } = [];
}

/// <summary>子代理实例目录条目（实例目录含已完成条目；运行态由前端另查会话状态）</summary>
internal sealed class DshSubagentCatalogEntry {
    /// <summary>子代理实例 ID</summary>
    public string Id { get; set; } = "";
    /// <summary>创建时间（Unix 毫秒）</summary>
    public long CreatedAt { get; set; }
    /// <summary>实例模式（one-shot / continuable / unknown — 引擎后台代理为一次性）</summary>
    public string Mode { get; set; } = "one-shot";
    /// <summary>显示名（可选）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label { get; set; }
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
    /// <summary>本轮用量（引擎 Complete.Usage 投影，未报告时整键省略，TASK058 B4）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DshTurnUsage? Usage { get; set; }
    /// <summary>流式块（历史消息为空数组）</summary>
    public List<JsonElement> Stream { get; set; } = [];
}

/// <summary>本轮用量 — 前端 TurnUsagePanel 四桶（totalTokens = 四桶之和，对齐前端 tokenTotal 语义）</summary>
internal sealed class DshTurnUsage {
    /// <summary>未缓存输入 token（prompt − cacheRead）</summary>
    public int UncachedInputTokens { get; set; }
    /// <summary>输出 token</summary>
    public int OutputTokens { get; set; }
    /// <summary>缓存读取 token</summary>
    public int CacheReadTokens { get; set; }
    /// <summary>缓存写入 token</summary>
    public int CacheWriteTokens { get; set; }
    /// <summary>总 token（四桶之和）</summary>
    public int TotalTokens { get; set; }
    /// <summary>推理 token（可选，未报告时整键省略）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ReasoningTokens { get; set; }
}

/// <summary>tool-workflow/run-start 载荷（子代理卡片创建，TASK058 B6）</summary>
internal sealed class DshWorkflowRunStartData {
    /// <summary>运行标识（宿主按轮合成 {sessionId}:{turn}:agents）</summary>
    public string RunId { get; set; } = "";
    /// <summary>运行名</summary>
    public string Name { get; set; } = "";
}

/// <summary>tool-workflow/agent-start 载荷（成员加入）</summary>
internal sealed class DshWorkflowAgentStartData {
    /// <summary>运行标识</summary>
    public string RunId { get; set; } = "";
    /// <summary>轮内成员序号（start/end 配对键）</summary>
    public int Seq { get; set; }
    /// <summary>显示名</summary>
    public string Label { get; set; } = "";
    /// <summary>子代理会话 ID</summary>
    public string ChildId { get; set; } = "";
}

/// <summary>tool-workflow/agent-end 载荷（成员定格；outcome 值域 completed/cancelled/failed）</summary>
internal sealed class DshWorkflowAgentEndData {
    /// <summary>运行标识</summary>
    public string RunId { get; set; } = "";
    /// <summary>轮内成员序号</summary>
    public int Seq { get; set; }
    /// <summary>结果</summary>
    public string Outcome { get; set; } = "";
}

/// <summary>tool-workflow/run-end 载荷（stopReason 值域 completed/cancelled/error）</summary>
internal sealed class DshWorkflowRunEndData {
    /// <summary>运行标识</summary>
    public string RunId { get; set; } = "";
    /// <summary>停止原因</summary>
    public string StopReason { get; set; } = "";
}

/// <summary>
/// 助手流基线（ADR 0147）：重连时前端据此重建「进行中尝试」已收到的文本，
/// <c>revision=0</c> 表示从未有过帧；有 <see cref="ActiveAttempt"/> 时后续帧必须从 <c>revision+1</c> 续上。
/// </summary>
internal sealed class DshAssistantStreamBaseline {
    /// <summary>流版本（单调递增，跨轮次不重置）</summary>
    public int Revision { get; set; }
    /// <summary>进行中的尝试（空闲时不下发该键）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DshAssistantStreamAttempt? ActiveAttempt { get; set; }
}

/// <summary>进行中的助手尝试（基线里的可重建状态）</summary>
internal sealed class DshAssistantStreamAttempt {
    /// <summary>尝试身份（start/end 帧共用）</summary>
    public string AttemptId { get; set; } = "";
    /// <summary>该尝试开始时的持久游标（之后的帧都属于这条消息）</summary>
    public long StartedAfterSeq { get; set; }
    /// <summary>轮次号</summary>
    public int Turn { get; set; }
    /// <summary>步骤号</summary>
    public int Step { get; set; }
    /// <summary>下一条 chunk 帧的序号（前端据此校验不跳号）</summary>
    public int NextIndex { get; set; }
    /// <summary>已收到的 chunk 记录（供前端展开重建，不含 revision）</summary>
    public List<DshAssistantStreamRecord> Stream { get; set; } = [];
}

/// <summary>尝试内的一条 chunk 记录（基线里只允许 chunk 形状）</summary>
internal sealed class DshAssistantStreamRecord {
    /// <summary>固定为 chunk</summary>
    public string Type { get; set; } = "chunk";
    /// <summary>产出时间（毫秒时间戳）</summary>
    public long Time { get; set; }
    /// <summary>delta 载荷（text-delta / reasoning-delta）</summary>
    public JsonElement Chunk { get; set; }
}

/// <summary>逐字流信封（推给 <c>session/follow</c> 流的瞬态项，<c>type</c> 用于前端分流）</summary>
internal sealed class DshAssistantStreamEnvelope {
    /// <summary>固定为 assistant-stream</summary>
    public string Type { get; set; } = "assistant-stream";
    /// <summary>帧内容</summary>
    public DshAssistantStreamFrame Frame { get; set; } = new();
}

/// <summary>逐字流的一帧：start → chunk* → end（瞬态，不落日志、不占序号）</summary>
internal sealed class DshAssistantStreamFrame {
    /// <summary>帧类型（start / chunk / end）</summary>
    public string Type { get; set; } = "start";
    /// <summary>尝试身份</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AttemptId { get; set; }
    /// <summary>流版本（每帧 +1，前端强校验）</summary>
    public int Revision { get; set; }
    /// <summary>尝试开始时的持久游标（仅 start）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? StartedAfterSeq { get; set; }
    /// <summary>轮次号（仅 start）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Turn { get; set; }
    /// <summary>步骤号（仅 start）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Step { get; set; }
    /// <summary>chunk 序号（仅 chunk，从 0 起）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Index { get; set; }
    /// <summary>产出时间（仅 chunk，毫秒时间戳）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Time { get; set; }
    /// <summary>delta 载荷（仅 chunk）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Chunk { get; set; }
    /// <summary>结算（仅 end）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DshAssistantStreamOutcome? Outcome { get; set; }
}

/// <summary>流帧的结算：committed 指向已落盘的助手消息事件，abandoned 表示本轮作废</summary>
internal sealed class DshAssistantStreamOutcome {
    /// <summary>结算类型（committed / abandoned）</summary>
    public string Kind { get; set; } = "abandoned";
    /// <summary>落盘事件类型（committed 时固定 assistant/message）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EventType { get; set; }
    /// <summary>落盘事件序号（committed 时必须等于 assistant/message 的 seq）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Seq { get; set; }
}

/// <summary>文本/思考 delta（组装器容忍纯 delta 协议，无需 block-start/block-end）</summary>
internal sealed class DshDeltaChunk {
    /// <summary>delta 类型（text-delta / reasoning-delta）</summary>
    public string Type { get; set; } = "text-delta";
    /// <summary>内容块下标（按块首次出现顺序从 0 分配：思考先到则 0、正文 1；纯正文轮正文 0）</summary>
    public int Index { get; set; }
    /// <summary>本次追加的文本</summary>
    public string Text { get; set; } = "";
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

/// <summary>session/selectModel 参数</summary>
internal sealed class DshSessionSelectModelArgs {
    /// <summary>会话 ID</summary>
    public string SessionId { get; set; } = "";
    /// <summary>供应商</summary>
    public string Provider { get; set; } = "";
    /// <summary>模型 ID</summary>
    public string Model { get; set; } = "";
    /// <summary>推理强度（low/medium/high/max，可选）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasoningEffort { get; set; }
}

/// <summary>session/selectModel 响应 — 回显实际选中的模型</summary>
internal sealed class DshSessionSelectModelResult {
    /// <summary>选中的模型</summary>
    public DshModelChoice Selected { get; set; } = new();
}

/// <summary>session/fork 参数</summary>
internal sealed class DshSessionForkArgs {
    /// <summary>源会话 ID</summary>
    public string SessionId { get; set; } = "";
    /// <summary>分叉点事件序号（可选；v1 全量复制，深化项）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? AtSeq { get; set; }
}

/// <summary>session/fork 响应 — 新会话 ID</summary>
internal sealed class DshSessionForkResult {
    /// <summary>新会话 ID</summary>
    public string SessionId { get; set; } = "";
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

    /// <summary>构造带二进制附件的成功信封（宿主以 multipart 返回）</summary>
    internal static DshRpcResult Ok(JsonElement value, DshRpcBinaryAttachment binary) =>
        new() { Ok = true, Value = value, Binary = binary };

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
[JsonSerializable(typeof(DshTerminalAgentArgs))]
[JsonSerializable(typeof(DshTerminalEnvironmentResult))]
[JsonSerializable(typeof(DshTerminalShellProfile))]
[JsonSerializable(typeof(DshTerminalShellProfile[]))]
[JsonSerializable(typeof(DshTerminalInfo))]
[JsonSerializable(typeof(DshTerminalInfo[]))]
[JsonSerializable(typeof(DshTerminalCreateRequest))]
[JsonSerializable(typeof(DshTerminalCreateArgs))]
[JsonSerializable(typeof(DshTerminalIdArgs))]
[JsonSerializable(typeof(DshTerminalWriteArgs))]
[JsonSerializable(typeof(DshTerminalResizeArgs))]
[JsonSerializable(typeof(DshTerminalListArgs))]
[JsonSerializable(typeof(DshTerminalFollowArgs))]
[JsonSerializable(typeof(DshTerminalRetainArgs))]
[JsonSerializable(typeof(DshTerminalSnapshotFrame))]
[JsonSerializable(typeof(DshTerminalOutputFrame))]
[JsonSerializable(typeof(DshTerminalStateFrame))]
[JsonSerializable(typeof(DshTerminalRetainedFrame))]
[JsonSerializable(typeof(DshJobListArgs))]
[JsonSerializable(typeof(DshJobFollowArgs))]
[JsonSerializable(typeof(DshJobKillArgs))]
[JsonSerializable(typeof(DshRequestEnvelope<DshJobListArgs>))]
[JsonSerializable(typeof(DshRequestEnvelope<DshJobFollowArgs>))]
[JsonSerializable(typeof(DshRequestEnvelope<DshJobKillArgs>))]
[JsonSerializable(typeof(DshJobKillResult))]
[JsonSerializable(typeof(DshJobOutputInfo))]
[JsonSerializable(typeof(DshJobView))]
[JsonSerializable(typeof(DshJobRowsFrame))]
[JsonSerializable(typeof(DshJobOpenedFrame))]
[JsonSerializable(typeof(DshJobChunk))]
[JsonSerializable(typeof(DshJobOutputFrame))]
[JsonSerializable(typeof(DshJobStatusFrame))]
[JsonSerializable(typeof(DshRpcResponse))]
[JsonSerializable(typeof(DshRpcMultipartMetadata))]
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
[JsonSerializable(typeof(DshSubagentCatalogEntry))]
[JsonSerializable(typeof(List<DshSubagentCatalogEntry>))]
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
[JsonSerializable(typeof(DshTurnUsage))]
[JsonSerializable(typeof(DshWorkflowRunStartData))]
[JsonSerializable(typeof(DshWorkflowAgentStartData))]
[JsonSerializable(typeof(DshWorkflowAgentEndData))]
[JsonSerializable(typeof(DshWorkflowRunEndData))]
[JsonSerializable(typeof(List<JsonElement>))]
[JsonSerializable(typeof(DshAssistantStreamBaseline))]
[JsonSerializable(typeof(DshAssistantStreamAttempt))]
[JsonSerializable(typeof(DshAssistantStreamRecord))]
[JsonSerializable(typeof(DshAssistantStreamFrame))]
[JsonSerializable(typeof(DshAssistantStreamEnvelope))]
[JsonSerializable(typeof(DshAssistantStreamOutcome))]
[JsonSerializable(typeof(DshDeltaChunk))]
[JsonSerializable(typeof(Dictionary<string, DshSessionProjectionSnapshot>))]
[JsonSerializable(typeof(DshModelCatalog))]
[JsonSerializable(typeof(DshSessionSelectModelArgs))]
[JsonSerializable(typeof(DshSessionSelectModelResult))]
[JsonSerializable(typeof(DshSessionForkArgs))]
[JsonSerializable(typeof(DshSessionForkResult))]
[JsonSerializable(typeof(DshRequestEnvelope<DshSessionSelectModelArgs>))]
[JsonSerializable(typeof(DshRequestEnvelope<DshSessionForkArgs>))]
[JsonSerializable(typeof(DshAgentPresetList))]
[JsonSerializable(typeof(DshAgentPreset))]
[JsonSerializable(typeof(DshAgentPresetReadResult))]
[JsonSerializable(typeof(DshAgentPresetSelectArgs))]
[JsonSerializable(typeof(DshRequestEnvelope<DshSessionSearchArgs>))]
[JsonSerializable(typeof(DshSessionSearchArgs))]
[JsonSerializable(typeof(DshSessionSearchResult))]
[JsonSerializable(typeof(DshSessionSearchItem))]
[JsonSerializable(typeof(List<DshSessionSearchItem>))]
[JsonSerializable(typeof(DshRequestEnvelope<DshOpenWorkspacePathArgs>))]
[JsonSerializable(typeof(DshOpenWorkspacePathArgs))]
[JsonSerializable(typeof(DshOpenWorkspacePathResult))]
[JsonSerializable(typeof(DshRequestEnvelope<DshWorkspacePathAppsArgs>))]
[JsonSerializable(typeof(DshWorkspacePathAppsArgs))]
[JsonSerializable(typeof(DshWorkspacePathApp))]
[JsonSerializable(typeof(List<DshWorkspacePathApp>))]
[JsonSerializable(typeof(DshPermissionPresetCatalog))]
[JsonSerializable(typeof(DshWaterfallFrame))]
[JsonSerializable(typeof(DshApprovalRequestBody))]
[JsonSerializable(typeof(DshEventsResultArgs))]
[JsonSerializable(typeof(DshEventOutcome))]
[JsonSerializable(typeof(DshEventsResultValue))]
[JsonSerializable(typeof(DshApprovalAskedData))]
[JsonSerializable(typeof(DshApprovalDecidedData))]
[JsonSerializable(typeof(DshEventErrorBody))]
[JsonSerializable(typeof(DshCommandInfo))]
[JsonSerializable(typeof(DshCommandInputInfo))]
[JsonSerializable(typeof(DshCommandsExecuteArgs))]
[JsonSerializable(typeof(DshCommandsExecuteResult))]
[JsonSerializable(typeof(DshCommandOutcome))]
[JsonSerializable(typeof(List<DshCommandInfo>))]
[JsonSerializable(typeof(DshSettingsDescribe))]
[JsonSerializable(typeof(DshSettingsNamespaceView))]
[JsonSerializable(typeof(DshSettingsMutateArgs))]
[JsonSerializable(typeof(DshSettingsReplaceArgs))]
[JsonSerializable(typeof(DshSettingsUpdateArgs))]
[JsonSerializable(typeof(DshSettingsDocument))]
[JsonSerializable(typeof(DshSettingsNamespaceDocument))]
[JsonSerializable(typeof(DshSchemaEnvelope))]
[JsonSerializable(typeof(DshSkillList))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(DshDirectoryListArgs))]
[JsonSerializable(typeof(DshDirectoryCreateArgs))]
[JsonSerializable(typeof(DshDirectoryListing))]
[JsonSerializable(typeof(DshDirectoryCrumb))]
[JsonSerializable(typeof(List<DshDirectoryCrumb>))]
[JsonSerializable(typeof(DshDirectoryEntry))]
[JsonSerializable(typeof(List<DshDirectoryEntry>))]
[JsonSerializable(typeof(DshFileReferencesListArgs))]
[JsonSerializable(typeof(DshFileReferenceItem))]
[JsonSerializable(typeof(List<DshFileReferenceItem>))]
[JsonSerializable(typeof(DshMessageFeedbackListResult))]
[JsonSerializable(typeof(DshMessageFeedbackListValue))]
[JsonSerializable(typeof(DshMessageFeedbackItem))]
[JsonSerializable(typeof(DshRequestEnvelope<DshMessageFeedbackPutArgs>))]
[JsonSerializable(typeof(DshMessageFeedbackPutArgs))]
[JsonSerializable(typeof(DshMessageFeedbackPutResult))]
[JsonSerializable(typeof(DshRequestEnvelope<DshMessageFeedbackDeleteArgs>))]
[JsonSerializable(typeof(DshMessageFeedbackDeleteArgs))]
[JsonSerializable(typeof(DshMessageFeedbackDeleteResult))]
[JsonSerializable(typeof(DshMessageFeedbackDeleteValue))]
[JsonSerializable(typeof(DshMessageFeedbackSessionNotFoundError))]
[JsonSerializable(typeof(DshMessageFeedbackTargetNotFoundError))]
[JsonSerializable(typeof(DshMessageFeedbackVersionConflictError))]
[JsonSerializable(typeof(DshMessageFeedbackNoteBlankError))]
[JsonSerializable(typeof(DshMessageFeedbackNoteTooLargeError))]
[JsonSerializable(typeof(DshMessageFeedbackDocument))]
[JsonSerializable(typeof(DshProviderInfo))]
[JsonSerializable(typeof(List<DshProviderInfo>))]
[JsonSerializable(typeof(JsonElement[]))]
[JsonSerializable(typeof(Dictionary<string, DshCredentialState>))]
[JsonSerializable(typeof(DshRpcErrorDetails))]
[JsonSerializable(typeof(DshWorkspaceFilesArgs))]
[JsonSerializable(typeof(DshWorkspaceFilesListResult))]
[JsonSerializable(typeof(DshWorkspaceFilesReadResult))]
[JsonSerializable(typeof(DshWorkspaceFilesReadBytesResult))]
[JsonSerializable(typeof(DshWorkspaceFilesStatResult))]
[JsonSerializable(typeof(DshFileChangeFrame))]
[JsonSerializable(typeof(DshChangeReady))]
[JsonSerializable(typeof(DshGoal))]
[JsonSerializable(typeof(DshGoalRef))]
[JsonSerializable(typeof(DshGoalsRefArgs))]
[JsonSerializable(typeof(DshGoalsCreateArgs))]
[JsonSerializable(typeof(DshGoalsCreateResult))]
[JsonSerializable(typeof(DshGoalsEditArgs))]
[JsonSerializable(typeof(DshGoalChanges))]
[JsonSerializable(typeof(DshGoalsClearResult))]
[JsonSerializable(typeof(DshGoalsDocument))]
[JsonSerializable(typeof(DshGoalActivationChanged))]
[JsonSerializable(typeof(DshFileUploadsUploadArgs))]
[JsonSerializable(typeof(DshFileUploadResult))]
[JsonSerializable(typeof(DshAttachmentRecord))]
[JsonSerializable(typeof(DshSessionAttachmentArgs))]
[JsonSerializable(typeof(DshSessionAttachmentResult))]
[JsonSerializable(typeof(DshUpdateQueueArgs))]
[JsonSerializable(typeof(DshQueueAcceptedResult))]
[JsonSerializable(typeof(object))]
internal sealed partial class DshJsonContext : JsonSerializerContext;
