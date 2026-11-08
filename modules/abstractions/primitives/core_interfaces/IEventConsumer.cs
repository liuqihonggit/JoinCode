namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 事件消费策略接口 — 解耦事件处理与表示层输出
/// CLI 模式: CliEventConsumer (TerminalHelper 输出)
/// TUI 模式: TuiEventConsumer (IPresentationAdapter 输出)
/// </summary>
public interface IEventConsumer {
    /// <summary>
    /// 收到文本内容
    /// </summary>
    void OnText(string content);

    /// <summary>
    /// 收到思考内容
    /// </summary>
    void OnThinking(string thinking);

    /// <summary>
    /// 工具调用开始
    /// </summary>
    void OnToolStart(string toolName, string? toolCallId, string? arguments);

    /// <summary>
    /// 工具调用结束
    /// </summary>
    void OnToolEnd(string toolName, string? resultText, string? toolCallId, bool isError, StructuredPatchHunk[]? patch);

    /// <summary>
    /// 工具调用进度
    /// </summary>
    void OnToolProgress(string toolName, string progressType, string? progressMessage);

    /// <summary>
    /// 检测到循环输出
    /// </summary>
    void OnLoopDetected(int triggerCount, int loopStartIndex, string? repeatedPattern);

    /// <summary>
    /// 计时摘要
    /// </summary>
    void OnTimingSummary(string summary);

    /// <summary>
    /// 流式响应完成
    /// </summary>
    /// <param name="usage">令牌用量</param>
    /// <param name="modelId">模型标识</param>
    /// <param name="stopReason">停止原因（stop/tool_calls/length/content_filter 等）；null = 未提供</param>
    void OnDone(TokenUsage? usage, string? modelId, string? stopReason = null);

    /// <summary>
    /// 子代理启动（DSG041 批1 — 协议本体先扩，投影层后跟；默认空实现 — 既有实现方零改动，
    /// 默认实现须经接口类型调用才可见）
    /// </summary>
    /// <param name="subagentId">子代理标识（事件路由键，分组渲染 AddAgent 键）</param>
    /// <param name="agentType">代理类型（组头同型拼接 + 八色取色键）</param>
    /// <param name="description">任务描述（状态行括号段）</param>
    /// <param name="role">代理角色（executor/researcher/planner 等）；null = 未提供</param>
    void OnSubagentStarted(string subagentId, string agentType, string description, string? role = null) { }

    /// <summary>
    /// 子代理工具活动（DSG041 批1 — Started 与 Finished 间的过程事件；默认空实现）
    /// </summary>
    /// <param name="subagentId">子代理标识</param>
    /// <param name="toolName">工具名</param>
    /// <param name="summary">活动摘要（参数/结果/进度文本）</param>
    /// <param name="tokenGain">令牌增量（批2 通道接通后填充真实值；批1 恒 0）</param>
    void OnSubagentToolUse(string subagentId, string toolName, string summary, int tokenGain) { }

    /// <summary>
    /// 子代理完成（DSG041 批1 — 终态定格；默认空实现）
    /// </summary>
    /// <param name="subagentId">子代理标识</param>
    /// <param name="isError">是否失败</param>
    /// <param name="tokenTotal">子代理总令牌消耗（TASK062 P7 — AgentFinished 事件 Usage.TotalTokens 权威值；
    /// 完成态一次性校正优于逐活动增量 — 引擎数据流中 token 仅回合/完成粒度存在（RecordTokenUsage 亦完成时调用）；
    /// 0 = 无 usage 数据（FinalizePending 等兜底路径），消费方保持原累计值）</param>
    /// <param name="usage">子代理完整 token 用量（含 prompt/completion/cache_read/cache_creation/reasoning）；
    /// null = 无 usage 数据。NDJSON 消费方可据此显示子代理 token 详情</param>
    void OnSubagentFinished(string subagentId, bool isError, int tokenTotal, TokenUsage? usage = null) { }

    /// <summary>
    /// 上下文窗口余量上报（M8-7 — 每回合 Done 后消费侧 footer 告警显示；默认空实现，
    /// 既有实现方零改动，默认实现须经接口类型调用才可见）
    /// </summary>
    /// <param name="promptTokens">当前上下文窗口占用（回合 Done 权威 usage.PromptTokens 快照）</param>
    /// <param name="contextWindowTokens">上下文窗口大小（IContextWindowResolver 实时解析，缺省 200K）</param>
    /// <param name="percentLeft">剩余百分比 0-100（引擎侧 ICompactService.CalculateWarningState 权威算法；
    /// 阈值标志不进接口 — TUI 侧只显示百分比+文案，UI 自己判颜色）</param>
    void OnContextUsage(int promptTokens, int contextWindowTokens, int percentLeft) { }

    /// <summary>后台任务状态更新（M8-8 — 后台 fork/teammate 代理生命周期，TUI 常驻任务面板显示）。
    /// kind: fork|teammate；state: running|completed|failed|cancelled；upsert 语义按 (kind,taskId)。</summary>
    void OnBackgroundTask(string kind, string taskId, string description, string state) { }

    /// <summary>
    /// 上下文压缩完成（TASK070 C6/DSG046 — 手动 /compact 与回合末自动压缩共用出口；
    /// TUI 侧据此重置用量警告（TokenUsageTracker.Reset — 压缩后旧快照作废）+ 通知上屏。
    /// 默认空实现 — 既有实现方零改动，默认实现须经接口类型调用才可见）
    /// </summary>
    /// <param name="messagesSummarized">已摘要的消息数</param>
    /// <param name="originalTokens">压缩前 token 数（自动路径为 Done 权威 usage.PromptTokens；手动路径为字符估算）</param>
    /// <param name="compressedTokens">压缩后 token 数（摘要文本估算）</param>
    /// <param name="isAuto">是否自动触发（true=回合末阈值触发，false=用户 /compact）</param>
    void OnCompactCompleted(int messagesSummarized, long originalTokens, long compressedTokens, bool isAuto) { }

    /// <summary>
    /// 自动重试通知（TASK070 E2/DSG046 — QueryEngine 指数退避静默重试的感知上屏；
    /// TUI spinner 行提示 "Retrying in Xs (N/M)"，非 ErrorMessageItem — 重试非终态。
    /// 默认空实现 — 既有实现方零改动，默认实现须经接口类型调用才可见）
    /// </summary>
    /// <param name="reason">触发重试的异常摘要</param>
    /// <param name="attempt">即将进行的重试序号（1 基）</param>
    /// <param name="maxRetries">最大重试次数</param>
    /// <param name="delayMs">本次退避等待毫秒数</param>
    void OnRetry(string reason, int attempt, int maxRetries, int delayMs) { }

    /// <summary>
    /// 会话状态变更（TASK073 Phase 2 — 对齐基准侧 system/session_state_changed 事件；
    /// state: idle|running|requires_action。NDJSON 模式输出 SdkSessionStateChangedMessage，
    /// DX 模式忽略。默认空实现 — 既有实现方零改动）
    /// </summary>
    /// <param name="state">新状态（idle/running/requires_action）</param>
    void OnSessionStateChanged(string state) { }

    /// <summary>
    /// Hook 开始执行（TASK073 Phase 3 — 对齐基准侧 system/hook_started 事件；
    /// NDJSON 模式输出 SdkHookStartedMessage。默认空实现 — 既有实现方零改动）
    /// </summary>
    /// <param name="hookId">Hook 标识</param>
    /// <param name="hookName">Hook 名称</param>
    /// <param name="hookEvent">Hook 事件类型</param>
    void OnHookStarted(string hookId, string hookName, string hookEvent) { }

    /// <summary>
    /// Hook 执行进度更新（TASK073 Phase 3 — 对齐基准侧 system/hook_progress 事件；
    /// NDJSON 模式输出 SdkHookProgressMessage。默认空实现 — 既有实现方零改动）
    /// </summary>
    /// <param name="hookId">Hook 标识</param>
    /// <param name="hookName">Hook 名称</param>
    /// <param name="hookEvent">Hook 事件类型</param>
    /// <param name="stdout">标准输出</param>
    /// <param name="stderr">标准错误输出</param>
    void OnHookProgress(string hookId, string hookName, string hookEvent, string? stdout, string? stderr) { }

    /// <summary>
    /// Hook 执行完成（TASK073 Phase 3 — 对齐基准侧 system/hook_response 事件；
    /// NDJSON 模式输出 SdkHookResponseMessage。默认空实现 — 既有实现方零改动）
    /// </summary>
    /// <param name="hookId">Hook 标识</param>
    /// <param name="hookName">Hook 名称</param>
    /// <param name="hookEvent">Hook 事件类型</param>
    /// <param name="output">合并输出</param>
    /// <param name="stdout">标准输出</param>
    /// <param name="stderr">标准错误输出</param>
    /// <param name="exitCode">退出码</param>
    /// <param name="outcome">执行结果（success/error/cancelled）</param>
    void OnHookResponse(string hookId, string hookName, string hookEvent, string? output, string? stdout, string? stderr, int? exitCode, string outcome) { }

    /// <summary>
    /// 速率限制事件（TASK073 Phase 3 — 对齐基准侧 rate_limit_event 事件；
    /// NDJSON 模式输出 SdkRateLimitEventMessage。默认空实现 — 既有实现方零改动）
    /// </summary>
    /// <param name="status">限制状态（allowed/allowed_warning/rejected）</param>
    /// <param name="resetsAt">限制重置时间戳（Unix 秒），null = 未提供</param>
    /// <param name="rateLimitType">限制类型（five_hour/seven_day 等），null = 未提供</param>
    void OnRateLimit(string status, long? resetsAt, string? rateLimitType) { }

    /// <summary>
    /// 任务开始（TASK073 Phase 3 — 对齐基准侧 system/task_started 事件；
    /// NDJSON 模式输出 SdkTaskStartedMessage。默认空实现 — 既有实现方零改动）
    /// </summary>
    /// <param name="taskId">任务标识</param>
    /// <param name="description">任务描述</param>
    /// <param name="toolUseId">关联的工具调用标识；null = 未提供</param>
    /// <param name="taskType">任务类型；null = 未提供</param>
    /// <param name="workflowName">工作流名称；null = 未提供</param>
    /// <param name="prompt">任务提示词；null = 未提供</param>
    void OnTaskStarted(string taskId, string description, string? toolUseId = null, string? taskType = null, string? workflowName = null, string? prompt = null) { }

    /// <summary>
    /// 任务进度更新（TASK073 Phase 3 — 对齐基准侧 system/task_progress 事件；
    /// NDJSON 模式输出 SdkTaskProgressMessage。默认空实现 — 既有实现方零改动）
    /// </summary>
    /// <param name="taskId">任务标识</param>
    /// <param name="description">任务描述</param>
    /// <param name="toolUseId">关联的工具调用标识；null = 未提供</param>
    /// <param name="lastToolName">最后使用的工具名称；null = 未提供</param>
    /// <param name="summary">任务摘要；null = 未提供</param>
    void OnTaskProgress(string taskId, string description, string? toolUseId = null, string? lastToolName = null, string? summary = null) { }

    /// <summary>
    /// 任务完成通知（TASK073 Phase 3 — 对齐基准侧 system/task_notification 事件；
    /// NDJSON 模式输出 SdkTaskNotificationMessage。默认空实现 — 既有实现方零改动）
    /// </summary>
    /// <param name="taskId">任务标识</param>
    /// <param name="status">任务状态（completed/failed/stopped）</param>
    /// <param name="summary">任务摘要</param>
    /// <param name="toolUseId">关联的工具调用标识；null = 未提供</param>
    /// <param name="outputFile">输出文件路径；null = 未提供</param>
    void OnTaskNotification(string taskId, string status, string summary, string? toolUseId = null, string? outputFile = null) { }

    /// <summary>
    /// 状态消息（TASK073 Phase 3 — 对齐基准侧 system/status 事件；
    /// NDJSON 模式输出 SdkStatusMessage。默认空实现 — 既有实现方零改动）
    /// </summary>
    /// <param name="status">状态描述（compacting 等）；null = 未提供</param>
    /// <param name="permissionMode">权限模式；null = 未提供</param>
    void OnStatus(string? status, string? permissionMode = null) { }

    /// <summary>
    /// 认证状态（TASK073 Phase 3 — 对齐基准侧 auth_status 事件；
    /// NDJSON 模式输出 SdkAuthStatusMessage。默认空实现 — 既有实现方零改动）
    /// </summary>
    /// <param name="isAuthenticating">是否正在认证</param>
    /// <param name="output">认证输出信息列表</param>
    /// <param name="error">认证错误信息；null = 无错误</param>
    void OnAuthStatus(bool isAuthenticating, System.Collections.Generic.List<string>? output = null, string? error = null) { }

    /// <summary>
    /// 工具使用摘要（TASK073 Phase 3 — 对齐基准侧 tool_use_summary 事件；
    /// NDJSON 模式输出 SdkToolUseSummaryMessage。默认空实现 — 既有实现方零改动）
    /// </summary>
    /// <param name="summary">工具使用摘要文本</param>
    /// <param name="precedingToolUseIds">前置工具调用标识列表</param>
    void OnToolUseSummary(string summary, System.Collections.Generic.List<string>? precedingToolUseIds = null) { }

    /// <summary>
    /// 本地命令输出（TASK073 Phase 4 — 对齐基准侧 system/local_command_output 事件；
    /// NDJSON 模式输出 SdkLocalCommandOutputMessage。默认空实现 — 既有实现方零改动）
    /// </summary>
    /// <param name="content">命令输出内容</param>
    void OnLocalCommandOutput(string content) { }

    /// <summary>
    /// 文件持久化（TASK073 Phase 4 — 对齐基准侧 system/files_persisted 事件；
    /// NDJSON 模式输出 SdkFilesPersistedMessage。默认空实现 — 既有实现方零改动）
    /// </summary>
    /// <param name="files">成功持久化的文件列表</param>
    /// <param name="failed">持久化失败的文件列表</param>
    void OnFilesPersisted(System.Collections.Generic.List<(string Filename, string FileId)>? files = null, System.Collections.Generic.List<(string Filename, string Error)>? failed = null) { }

    /// <summary>
    /// 引导完成（TASK073 Phase 4 — 对齐基准侧 system/elicitation_complete 事件；
    /// NDJSON 模式输出 SdkElicitationCompleteMessage。默认空实现 — 既有实现方零改动）
    /// </summary>
    /// <param name="mcpServerName">MCP 服务器名称</param>
    /// <param name="elicitationId">引导标识</param>
    void OnElicitationComplete(string mcpServerName, string elicitationId) { }

    /// <summary>
    /// 提示建议（TASK073 Phase 4 — 对齐基准侧 prompt_suggestion 事件；
    /// NDJSON 模式输出 SdkPromptSuggestionMessage。默认空实现 — 既有实现方零改动）
    /// </summary>
    /// <param name="suggestion">建议内容</param>
    void OnPromptSuggestion(string suggestion) { }
}

/// <summary>
/// 可重置的事件消费策略 — SessionController 在每轮开始前自动调用 Reset
/// </summary>
public interface IResettableEventConsumer : IEventConsumer {
    /// <summary>
    /// 重置内部状态（新一轮对话前调用）
    /// </summary>
    void Reset();
}
