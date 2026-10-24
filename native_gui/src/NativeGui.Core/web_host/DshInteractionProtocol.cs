namespace JoinCode.Gui.WebHost;

// 交互类 DSH 协议 DTO（TASK066 C4）：代理预设、会话搜索、工作区路径打开、settings 替换/补丁、消息反馈。
// JsonSerializable 注册仍集中在 DshRpcProtocol.cs 的 DshJsonContext。

/// <summary>代理预设列表</summary>
internal sealed class DshAgentPresetList {
    /// <summary>预设</summary>
    public List<DshAgentPreset> Presets { get; set; } = [];
}

/// <summary>代理预设（字段与 DSH agentPresets/list 结果严格对齐）</summary>
internal sealed class DshAgentPreset {
    /// <summary>ID</summary>
    public string Id { get; set; } = "";
    /// <summary>是否默认</summary>
    public bool IsDefault { get; set; }
    /// <summary>显示名</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }
    /// <summary>说明</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
    /// <summary>加载失败原因（正常预设省略）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Broken { get; set; }
}

/// <summary>agentPresets/read 结果</summary>
internal sealed class DshAgentPresetReadResult {
    /// <summary>预设 ID</summary>
    public string AgentPreset { get; set; } = "";
    /// <summary>预设正文</summary>
    public string Content { get; set; } = "";
    /// <summary>显示名</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }
    /// <summary>说明</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
}

/// <summary>agentPresets/select 参数（agentId 由前端 scope 注入）</summary>
internal sealed class DshAgentPresetSelectArgs {
    /// <summary>会话/代理 ID</summary>
    public string AgentId { get; set; } = "";
    /// <summary>目标预设</summary>
    public string AgentPreset { get; set; } = "";
}

/// <summary>session/search 参数</summary>
internal sealed class DshSessionSearchArgs {
    /// <summary>字面量搜索词</summary>
    public string Query { get; set; } = "";
}

/// <summary>session/search 结果（最多 20 条）</summary>
internal sealed class DshSessionSearchResult {
    /// <summary>命中的会话与摘要片段</summary>
    public List<DshSessionSearchItem> Items { get; set; } = [];
    /// <summary>是否还有更多命中</summary>
    public bool HasMore { get; set; }
}

/// <summary>单条会话搜索命中</summary>
internal sealed class DshSessionSearchItem {
    /// <summary>会话 ID</summary>
    public string SessionId { get; set; } = "";
    /// <summary>最佳命中片段</summary>
    public string Snippet { get; set; } = "";
}

/// <summary>session/openWorkspacePath 参数</summary>
internal sealed class DshOpenWorkspacePathArgs {
    /// <summary>仅 reveal 时在文件管理器中选中</summary>
    public string? Action { get; set; }
    /// <summary>指定打开方式（工作区路径应用 ID）</summary>
    public string? Application { get; set; }
    /// <summary>目标路径</summary>
    public string Path { get; set; } = "";
}

/// <summary>session/openWorkspacePath 结果</summary>
internal sealed class DshOpenWorkspacePathResult {
    /// <summary>已发起打开</summary>
    public bool Opened { get; set; } = true;
}

/// <summary>session/workspacePathApplications 参数</summary>
internal sealed class DshWorkspacePathAppsArgs {
    /// <summary>目标路径</summary>
    public string Path { get; set; } = "";
}

/// <summary>工作区路径应用（系统文件处理器）</summary>
internal sealed class DshWorkspacePathApp {
    /// <summary>应用 ID</summary>
    public string Id { get; set; } = "";
    /// <summary>显示名</summary>
    public string Name { get; set; } = "";
    /// <summary>是否默认处理方式</summary>
    public bool Default { get; set; }
    /// <summary>图标（无图标时为 null，协议要求字段存在）</summary>
    public string? Icon { get; set; }
}

/// <summary>settings/replace 参数：整段替换命名空间</summary>
internal sealed class DshSettingsReplaceArgs {
    /// <summary>命名空间键</summary>
    public string Ns { get; set; } = "";
    /// <summary>新的整段值</summary>
    public JsonElement Section { get; set; }
    /// <summary>调用方读到的 revision；提供则做乐观并发校验</summary>
    public int? ExpectedRevision { get; set; }
}

/// <summary>settings/update 参数：按顶层字段补丁合并命名空间</summary>
internal sealed class DshSettingsUpdateArgs {
    /// <summary>命名空间键</summary>
    public string Ns { get; set; } = "";
    /// <summary>字段补丁（值为 null 表示删除该字段）</summary>
    public JsonElement Patch { get; set; }
    /// <summary>调用方读到的 revision；提供则做乐观并发校验</summary>
    public int? ExpectedRevision { get; set; }
}

/// <summary>
/// <c>llm/listConfigurableProviders</c> 声明项（TASK066 C8）：把引擎可路由供应商声明成
/// 「在某设置命名空间的某路径下可编辑」，前端据此把模型页的行接到 settings 编辑卡片。
/// </summary>
internal sealed class DshConfigurableProvider {
    /// <summary>供应商路由 ID（与 llm/listProviders 的 id 对齐）</summary>
    public string Provider { get; set; } = "";
    /// <summary>显示名（宿主暂用路由 ID）</summary>
    public string DisplayName { get; set; } = "";
    /// <summary>承载该供应商配置的 settings 命名空间</summary>
    public string SettingsNs { get; set; } = "";
    /// <summary>命名空间值中的路径（profiles/{vendor}）</summary>
    public List<string> SettingsPath { get; set; } = [];
}

/// <summary><c>llm-deepseek</c> 命名空间值：供应商名 → 连接配置</summary>
internal sealed class DshProviderProfilesValue {
    /// <summary>供应商配置</summary>
    public Dictionary<string, DshProviderProfileValue> Profiles { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>单个供应商的连接配置（字段名对齐前端 llm-deepseek 布局）</summary>
internal sealed class DshProviderProfileValue {
    /// <summary>API 端点（映射引擎 endpoint）</summary>
    [JsonPropertyName("baseURL")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BaseUrl { get; set; }
    /// <summary>凭据引用名（映射引擎 apiKeyEnvVar）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ApiKeyEnv { get; set; }
    /// <summary>默认模型 ID</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; set; }
    /// <summary>模型清单（前端模型编辑器逐行读写）</summary>
    public List<DshProviderModelValue> Models { get; set; } = [];
}

/// <summary>模型行（前端 DeepSeekModelsEditor 的字段集）</summary>
internal sealed class DshProviderModelValue {
    /// <summary>模型 ID</summary>
    public string Id { get; set; } = "";
    /// <summary>显示名（空表示跟随 ID）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }
    /// <summary>上下文窗口</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ContextWindow { get; set; }
    /// <summary>最大输出 token（引擎配置无此字段，写入时显式拒绝而非静默丢弃）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxTokens { get; set; }
    /// <summary>输入模态（text / image；空表示未指定，写入时沿用原模型能力）</summary>
    public List<string> InputModalities { get; set; } = [];
}

/// <summary>消息反馈列表请求</summary>
internal sealed class DshMessageFeedbackListRequest {
    /// <summary>会话 ID</summary>
    public string SessionId { get; set; } = "";
}

/// <summary>消息反馈列表结果（DSH 契约：{ok:true,value}|{ok:false,error}）</summary>
internal sealed class DshMessageFeedbackListResult {
    /// <summary>是否成功</summary>
    public bool Ok { get; set; } = true;
    /// <summary>成功值</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DshMessageFeedbackListValue? Value { get; set; }
    /// <summary>失败体（成功时省略）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Error { get; set; }
}

/// <summary>消息反馈列表值</summary>
internal sealed class DshMessageFeedbackListValue {
    /// <summary>反馈条目</summary>
    public List<DshMessageFeedbackItem> Items { get; set; } = [];
}

/// <summary>单条消息反馈（version 为不透明字符串，前端原样回传做乐观并发）</summary>
internal sealed class DshMessageFeedbackItem {
    /// <summary>消息 ID</summary>
    public string MessageId { get; set; } = "";
    /// <summary>评价（positive / negative）</summary>
    public string Rating { get; set; } = "positive";
    /// <summary>补充说明</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; set; }
    /// <summary>问题分类</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Category { get; set; }
    /// <summary>版本号（不透明字符串）</summary>
    public string Version { get; set; } = "";
    /// <summary>创建时间（Unix 毫秒）</summary>
    public long CreatedAt { get; set; }
    /// <summary>更新时间（Unix 毫秒）</summary>
    public long UpdatedAt { get; set; }
}

/// <summary>messageFeedback/put 参数（request 信封内）</summary>
internal sealed class DshMessageFeedbackPutArgs {
    /// <summary>会话 ID</summary>
    public string SessionId { get; set; } = "";
    /// <summary>目标助手消息 ID</summary>
    public string MessageId { get; set; } = "";
    /// <summary>评价（positive / negative）</summary>
    public string Rating { get; set; } = "";
    /// <summary>补充说明</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; set; }
    /// <summary>问题分类</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Category { get; set; }
    /// <summary>调用方观察到的版本；null 表示期望新建</summary>
    public string? IfVersion { get; set; }
}

/// <summary>messageFeedback/put 结果</summary>
internal sealed class DshMessageFeedbackPutResult {
    /// <summary>是否成功</summary>
    public bool Ok { get; set; } = true;
    /// <summary>成功值</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DshMessageFeedbackItem? Value { get; set; }
    /// <summary>失败体（成功时省略）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Error { get; set; }
}

/// <summary>messageFeedback/delete 参数（request 信封内）</summary>
internal sealed class DshMessageFeedbackDeleteArgs {
    /// <summary>会话 ID</summary>
    public string SessionId { get; set; } = "";
    /// <summary>目标助手消息 ID</summary>
    public string MessageId { get; set; } = "";
    /// <summary>调用方观察到的版本</summary>
    public string? IfVersion { get; set; }
}

/// <summary>messageFeedback/delete 结果</summary>
internal sealed class DshMessageFeedbackDeleteResult {
    /// <summary>是否成功</summary>
    public bool Ok { get; set; } = true;
    /// <summary>成功值</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DshMessageFeedbackDeleteValue? Value { get; set; }
    /// <summary>失败体（成功时省略）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Error { get; set; }
}

/// <summary>删除成功值（幂等语义：目标已不存在）</summary>
internal sealed class DshMessageFeedbackDeleteValue {
    /// <summary>已确认为不存在</summary>
    public bool Absent { get; set; } = true;
}

/// <summary>会话不存在错误体</summary>
internal sealed class DshMessageFeedbackSessionNotFoundError {
    /// <summary>稳定错误码</summary>
    public string Code { get; set; } = "session-not-found";
    /// <summary>目标会话</summary>
    public string SessionId { get; set; } = "";
}

/// <summary>目标消息不存在错误体</summary>
internal sealed class DshMessageFeedbackTargetNotFoundError {
    /// <summary>稳定错误码</summary>
    public string Code { get; set; } = "target-not-found";
    /// <summary>目标会话</summary>
    public string SessionId { get; set; } = "";
    /// <summary>目标消息</summary>
    public string MessageId { get; set; } = "";
}

/// <summary>版本冲突错误体（current 必须显式输出，可能为 null）</summary>
internal sealed class DshMessageFeedbackVersionConflictError {
    /// <summary>稳定错误码</summary>
    public string Code { get; set; } = "version-conflict";
    /// <summary>当前落盘条目</summary>
    public DshMessageFeedbackItem? Current { get; set; }
}

/// <summary>备注为空白错误体</summary>
internal sealed class DshMessageFeedbackNoteBlankError {
    /// <summary>稳定错误码</summary>
    public string Code { get; set; } = "note-blank";
}

/// <summary>备注超长错误体</summary>
internal sealed class DshMessageFeedbackNoteTooLargeError {
    /// <summary>稳定错误码</summary>
    public string Code { get; set; } = "note-too-large";
    /// <summary>字节上限</summary>
    public int MaxBytes { get; set; }
    /// <summary>实际字节数</summary>
    public int ActualBytes { get; set; }
}

/// <summary>消息反馈评价（DSH 契约词汇）</summary>
internal enum DshFeedbackRating {
    /// <summary>赞</summary>
    [EnumValue("positive")]
    Positive,
    /// <summary>踩</summary>
    [EnumValue("negative")]
    Negative,
}

/// <summary>消息反馈问题分类（DSH 契约词汇）</summary>
internal enum DshFeedbackCategory {
    /// <summary>其他</summary>
    [EnumValue("other")]
    Other,
    /// <summary>任务结果</summary>
    [EnumValue("task-result")]
    TaskResult,
    /// <summary>指令遵循</summary>
    [EnumValue("instruction-following")]
    InstructionFollowing,
    /// <summary>产品交互</summary>
    [EnumValue("product-interaction")]
    ProductInteraction,
    /// <summary>服务稳定性</summary>
    [EnumValue("service-stability")]
    ServiceStability,
    /// <summary>资源消耗</summary>
    [EnumValue("resource-cost")]
    ResourceCost,
    /// <summary>安全隐私权限</summary>
    [EnumValue("security-privacy-permission")]
    SecurityPrivacyPermission,
}

/// <summary>消息反馈持久化文档</summary>
internal sealed class DshMessageFeedbackDocument {
    /// <summary>会话 ID → 消息 ID → 反馈</summary>
    public Dictionary<string, Dictionary<string, DshMessageFeedbackItem>> Sessions { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>sessionFeedback/record 参数（request 信封内）</summary>
internal sealed class DshSessionFeedbackRecordArgs {
    /// <summary>目标会话</summary>
    public string SessionId { get; set; } = "";
    /// <summary>用户备注（前后空白丢弃，空串视同未提供）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }
    /// <summary>反馈分类（DSH 分类词汇，可省略）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Category { get; set; }
}

/// <summary>sessionFeedback/record 结果（DSH 契约：{ok:true,value}|{ok:false,error}）</summary>
internal sealed class DshSessionFeedbackRecordResult {
    /// <summary>是否成功</summary>
    public bool Ok { get; set; } = true;
    /// <summary>成功值</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DshSessionFeedbackRecordValue? Value { get; set; }
    /// <summary>失败体（成功时省略）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Error { get; set; }
}

/// <summary>记录成功值</summary>
internal sealed class DshSessionFeedbackRecordValue {
    /// <summary>已写入会话日志（未落盘）</summary>
    public bool Recorded { get; set; } = true;
}

/// <summary>feedback/record 会话事件载荷（log-only，不启动模型工作）</summary>
internal sealed class DshFeedbackRecordData {
    /// <summary>用户备注（空白时省略字段）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }
    /// <summary>反馈分类（未提供时省略字段）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Category { get; set; }
}

/// <summary>sessionReferenceResolver/candidates 参数（agentId 由前端 scope 注入，query 为 @ 后的输入）</summary>
internal sealed class DshSessionReferenceCandidatesArgs {
    /// <summary>发起补全的会话（用于排除自身与判断同工作区）</summary>
    public string AgentId { get; set; } = "";
    /// <summary>用户已输入的查询词（空串表示列出全部）</summary>
    public string Query { get; set; } = "";
}

/// <summary>会话引用候选（前端 chip 用 mention 作 ref、label 作显示名）</summary>
internal sealed class DshSessionReferenceCandidate {
    /// <summary>插入输入框的文本</summary>
    public string Mention { get; set; } = "";
    /// <summary>被引用的会话</summary>
    public string SessionId { get; set; } = "";
    /// <summary>显示名（无标题时退化为短会话 ID）</summary>
    public string Label { get; set; } = "";
    /// <summary>会话自定义标题（无标题时省略）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayTitle { get; set; }
    /// <summary>会话工作目录（未知时省略）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cwd { get; set; }
    /// <summary>是否与发起会话同一工作区</summary>
    public bool SameWorkspace { get; set; }
    /// <summary>会话创建时间（Unix 毫秒）</summary>
    public long CreatedAt { get; set; }
}

/// <summary>credentials/describe 参数</summary>
internal sealed class DshCredentialsDescribeArgs {
    /// <summary>要查询的凭据引用名</summary>
    public List<string> Refs { get; set; } = [];
}

/// <summary>credentials/set 参数</summary>
internal sealed class DshCredentialsSetArgs {
    /// <summary>凭据引用名（如 <c>DEEPSEEK_API_KEY</c>）</summary>
    public string Ref { get; set; } = "";
    /// <summary>明文密钥（只写不读）</summary>
    public string Value { get; set; } = "";
}

/// <summary>credentials/unset 参数</summary>
internal sealed class DshCredentialsUnsetArgs {
    /// <summary>凭据引用名</summary>
    public string Ref { get; set; } = "";
}

/// <summary>llm/discoverModels 参数</summary>
internal sealed class DshDiscoverModelsArgs {
    /// <summary>来源设置命名空间（前端原样回传；宿主按供应商路由推导密钥）</summary>
    public string SettingsNs { get; set; } = "";
    /// <summary>发现请求体</summary>
    public DshModelDiscoveryRequest Request { get; set; } = new();
}

/// <summary>模型发现请求（provider/baseURL/api/apiKey 均可省略，按已有配置补全）</summary>
internal sealed class DshModelDiscoveryRequest {
    /// <summary>供应商路由（如 deepseek/openai/anthropic）</summary>
    public string? Provider { get; set; }
    /// <summary>API 基地址（含或不含 /v1）</summary>
    [JsonPropertyName("baseURL")]
    public string? BaseUrl { get; set; }
    /// <summary>协议（openai-completions/openai-responses/anthropic-messages）</summary>
    public string? Api { get; set; }
    /// <summary>明文密钥（前端填写时优先于已存凭据）</summary>
    public string? ApiKey { get; set; }
}

/// <summary>发现到的模型（字段与 DSH 契约同形）</summary>
internal sealed class DshDiscoveredModel {
    /// <summary>模型 ID</summary>
    public string Id { get; set; } = "";
    /// <summary>显示名（无独立名称时省略）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }
    /// <summary>上下文窗口（供应商未提供时省略）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ContextWindow { get; set; }
    /// <summary>最大输出 token（供应商未提供时省略）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxTokens { get; set; }
    /// <summary>输入模态（供应商未提供时为空数组）</summary>
    public List<string> InputModalities { get; set; } = [];
}

