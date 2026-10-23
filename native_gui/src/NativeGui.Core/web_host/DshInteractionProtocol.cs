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

