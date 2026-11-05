# TASK081-VV 全功能行为打点覆盖清单

> 由 6 个 explore 子代理并行普查汇总，覆盖 jcc 全部可观测行为分支。
> 每个助记符（vv001-vv150）对应一个可观测行为 + 触发条件 + 检测信号 + 是否需 MockServer 构造。
> 实际运行检测时：自然能触发的直接跑 jcc 检测；不能自然触发的用 MockServer 注入构造条件逼出每条行为分支。

## 领域分组

| 领域 | vv 范围 | 数量 | 来源子代理 |
|------|---------|------|-----------|
| NDJSON 事件输出 | vv001-vv015 | 15 | 子代理3 |
| 内置工具 | vv016-vv050 | 35 | 子代理1 |
| MCP 工具+外部工具 | vv051-vv075 | 25 | 子代理2 |
| 错误处理+退出码 | vv076-vv100 | 25 | 子代理4 |
| 多轮+子代理+会话 | vv101-vv125 | 25 | 子代理5 |
| 命令行+开关+权限 | vv126-vv150 | 25 | 子代理6 |

---

## 领域1: NDJSON 事件输出 (vv001-vv015)

| vv | 行为 | 触发条件 | 可观测信号 | 代码位置 | MockServer |
|----|------|---------|-----------|---------|-----------|
| vv001 | system/init 事件发射 | NDJSON 模式首次收到任意 LLM 事件 | `"type":"system","subtype":"init"` 首行 | CliEventConsumer.cs:235 | 否(任意流式响应即可) |
| vv002 | system/init model 字段 | system/init 发射 | `"model":"<modelId>"` 字段 | CliEventConsumer.cs:238 | 否 |
| vv003 | system/init session_id 字段 | system/init 发射 | `"session_id":"<Guid无连字符>"` 字段 | CliEventConsumer.cs:234,243 | 否 |
| vv004 | system/init uuid 字段 | system/init 发射 | `"uuid":"<标准UUID>"` 字段 | CliEventConsumer.cs:242 | 否 |
| vv005 | system/init permission_mode 字段 | system/init 发射 | `"permission_mode":"bypass\|default"` 字段 | CliEventConsumer.cs:240 | 否 |
| vv006 | assistant + text block | LLM 返回文本内容 | `"type":"assistant","content":[{"type":"text","text":"..."}]` | CliEventConsumer.cs:250 | 是(返回 content delta) |
| vv007 | assistant + thinking block | LLM 返回思考内容 | `"type":"thinking","thinking":"..."` | CliEventConsumer.cs:250 | 是(返回 thinking delta) |
| vv008 | assistant + tool_use block (input 是 JSON 对象) | LLM 返回 tool_calls | `"type":"tool_use","input":{"key":"value"}` (input 是对象非字符串) | CliEventConsumer.cs:123-127 | 是(返回 tool_calls chunk) |
| vv009 | user + tool_result block (is_error 是 bool) | 工具执行完成 | `"type":"tool_result","is_error":false` (bool 非 nullable) | CliEventConsumer.cs:265 | 是(触发工具调用) |
| vv010 | tool_progress 事件 | 工具执行期间产生 progress | `"type":"tool_progress"` | CliEventConsumer.cs:180 | 是(长时工具) |
| vv011 | result success 事件 | 管道正常完成 | `"type":"result","subtype":"success","is_error":false` | NonInteractiveModeRunner.cs:64 | 否(任意成功响应) |
| vv012 | result error 事件 | 管道异常或 exitCode!=0 | `"type":"result","subtype":"error_during_execution","is_error":true` | NonInteractiveModeRunner.cs:64; Program.cs:235 | 是(HTTP 500/超时) |
| vv013 | result uuid/session_id 字段 (**缺陷**) | result 发射 | 当前**缺失**，基准要求有 uuid/session_id | SdkMessage.cs:204-207 | 否 |
| vv014 | result modelUsage 字段 | result 发射 | `"modelUsage":{"<model>":{"inputTokens":N,...}}` | SdkMessage.cs:200 | 否 |
| vv015 | result permission_denials 字段 | result 发射 | `"permission_denials":[{"tool_name":"...","tool_use_id":"...","tool_input":"..."}]` | SdkMessage.cs:200-202 | 否 |

---

## 领域2: 内置工具 (vv016-vv050)

| vv | 行为 | 触发条件 | 可观测信号 | 代码位置 | MockServer |
|----|------|---------|-----------|---------|-----------|
| vv016 | bash 成功执行 | pipeline 正常完成 exit=0 | ToolResult success + stdout | ShellToolHandlers.cs:79-83 | 否 |
| vv017 | bash 失败 (exit!=0) | 命令返回非零退出码 | ToolResult success + stderr + exit code | ShellToolHandlers.cs:79 | 否 |
| vv018 | bash 超时 | 命令执行超时 | 超时错误诊断 | ShellPipelineContext | 否(长命令) |
| vv019 | bash 命令不存在 | 执行不存在的命令 | "command not found" + exit 127 | ShellToolHandlers.cs:79 | 否 |
| vv020 | bash 超大 stdout 截断 | stdout 超过截断阈值 | 截断后的输出 + hidden lines | ShellOutputMiddleware | 否(大输出命令) |
| vv021 | read 成功 | 读取存在的文件 | 带行号内容 | FileToolHandlers.Read.cs:155-197 | 否 |
| vv022 | read 文件不存在 | 读取不存在的文件 | FileNotFound 诊断 + 相似文件建议 | FileToolHandlers.Read.cs:116-122 | 否 |
| vv023 | read 目录而非文件 | 路径是目录 | "IsDirectoryNotFile" 诊断 | FileReader.cs | 否 |
| vv024 | read 超大文件 token 超限 | 文件 > 25000 tokens | "TokenLimitExceeded" 诊断 | FileToolHandlers.Read.cs:141-153 | 否(大文件) |
| vv025 | read 二进制文件拒绝 | .exe/.dll 等扩展名 | "This tool cannot read binary files" | FileToolHandlers.Read.cs:60-67 | 否 |
| vv026 | read 图像文件 (base64) | .png/.jpg 等图像 | base64 编码内容 | FileToolHandlers.Read.cs:46-48 | 否 |
| vv027 | write 创建新文件 | 写入不存在的新路径 | "File created successfully at: {path}" | FileToolHandlers.Write.cs:61-63 | 否 |
| vv028 | write 覆盖已有文件 | 写入已存在的文件 | "has been updated successfully" + StructuredPatch | FileToolHandlers.Write.cs:61-69 | 否 |
| vv029 | write 未读先写拒绝 | 未先 read 就 write | "必须先读再写" 拒绝 | FileToolHandlers.Write.cs:25 | 否 |
| vv030 | edit 成功 | 精确匹配 oldString 并替换 | "updated successfully" + StructuredPatch | FileToolHandlers.Edit.cs:73-75 | 否 |
| vv031 | edit oldString 未找到 | oldString 不在文件中 | "oldString not found" 诊断 + 上下文建议 | FileToolHandlers.Edit.cs:62-68 | 否 |
| vv032 | edit 多处匹配 + replaceAll | oldString 多处出现 | replaceAll=false → "multiple occurrences" 错误; replaceAll=true → "All N occurrences replaced" | FileToolHandlers.Edit.cs:73 | 否 |
| vv033 | glob 匹配 | pattern 匹配文件 | 匹配文件路径列表 | SearchToolHandlers.cs:230-238 | 否 |
| vv034 | glob 无匹配 | pattern 无匹配 | "NoResults" 诊断 + 通配符提示 | SearchToolHandlers.cs:224-228 | 否 |
| vv035 | grep 匹配 | 正则匹配内容 | 匹配行内容 + 分页 | SearchToolHandlers.cs:342-398 | 否 |
| vv036 | grep 无匹配 | 正则无匹配 | "NoResults" 诊断 + 大小写提示 | SearchToolHandlers.cs:333-337 | 否 |
| vv037 | todo_write 创建 | 写入待办列表 | "successfully written" | TodoToolHandlers.cs:79-91 | 否 |
| vv038 | todo_list 列表 | 列出待办 | Statistics + Todo List | TodoToolHandlers.cs:132-148 | 否 |
| vv039 | todo_update 状态更新 | 更新待办状态 | "updated successfully" | TodoToolHandlers.cs:197-204 | 否 |
| vv040 | task_create 创建任务 | 创建任务 | "TaskCreated" | TaskToolHandlers.cs:54-55 | 否 |
| vv041 | task_list 列表 | 列出任务 | TaskList + 任务摘要 | TaskToolHandlers.cs:87-96 | 否 |
| vv042 | skill 加载 | 加载存在的 skill | "LabelSkill" + Output | SkillToolHandlers.cs:102-110 | 否 |
| vv043 | skill 不存在 | 加载不存在的 skill | "SkillNotFound" | SkillToolHandlers.cs:50-52 | 否 |
| vv044 | webfetch 成功 | 抓取成功 URL | processedResult + http_status_code | WebToolHandlers.cs:104-118 | 是(MockServer 200) |
| vv045 | webfetch 重定向 | URL 返回 301/307 | "REDIRECT DETECTED" + 重定向 URL | WebToolHandlers.cs:74-89 | 是(MockServer 301) |
| vv046 | webfetch 失败 (非200) | URL 返回 500 | "FetchFailed" 诊断 + statusCode | WebToolHandlers.cs:61-72 | 是(MockServer 500) |
| vv047 | question 提问+回答 | ask_user_question 工具调用 | "User has answered your questions" | UserInteractionToolHandlers.cs:65-71 | 否 |
| vv048 | question 选项校验 | 选项 <2 或 >4 | "must have at least 2 options" | UserInteractionToolHandlers.cs:121-125 | 否 |
| vv049 | agent 创建子代理 | agent 工具调用 | "Agent started" + Agent ID | AgentToolHandlers.Create.cs | 是(模拟 LLM 流) |
| vv050 | agent_status 查询 | agent_status 工具调用 | status + ID + Description | AgentToolHandlers.Query.cs:82-107 | 否 |

---

## 领域3: MCP 工具+外部工具 (vv051-vv075)

| vv | 行为 | 触发条件 | 可观测信号 | 代码位置 | MockServer |
|----|------|---------|-----------|---------|-----------|
| vv051 | mcp_serve HTTP 启动 | `jcc mcp_serve --transport http --port N` | HTTP 服务监听 + token 输出 | McpServeCommand.cs:11-23 | 否 |
| vv052 | mcp_serve token 鉴权 | 请求带错误 token | 401 Unauthorized | JccMcpServer.cs | 否 |
| vv053 | mcp_serve 协议版本协商 | MCP-Protocol-Version 头 | 版本匹配/不匹配响应 | McpHttpServer | 否 |
| vv054 | mcp_call 工具调用成功 | `jcc mcp_call <tool> args` | 工具返回结果 JSON | McpCallCommand.cs:13-39 | 否 |
| vv055 | mcp_call 工具不存在 | 调用不存在的工具 | "Tool not found" 错误 | McpCallCommand.cs | 否 |
| vv056 | mcp_call 参数校验失败 | 参数不合法 | 参数校验错误诊断 | McpCallCommand.cs | 否 |
| vv057 | mcp_list 列出工具 | `jcc mcp_list` | 工具列表 JSON | McpListCommand.cs:11-20 | 否 |
| vv058 | mcp_list 按分类过滤 | `jcc mcp_list --category github` | 过滤后工具列表 | McpListCommand.cs | 否 |
| vv059 | mcp_schema 查 schema | `jcc mcp_schema <tool>` | 工具参数 schema JSON | McpSchemaCommand.cs:13-26 | 否 |
| vv060 | MCP 连接添加 | mcp_connect 工具 | 连接成功 + 持久化 | McpClientToolHandlers.cs | 否 |
| vv061 | MCP 连接移除 | mcp_disconnect 工具 | 连接移除 + 持久化 | McpClientToolHandlers.cs | 否 |
| vv062 | MCP 连接持久化 | 连接变更后 | connections.json 更新 | McpClientToolHandlers.Persistence.cs | 否 |
| vv063 | MCP 连接恢复 | jcc 启动时 | 从 connections.json 恢复 | McpClientToolHandlers.cs | 否 |
| vv064 | TODO 工具 (通过 MCP) | MCP 暴露的 TODO 工具 | TODO 操作结果 | 外部 MCP 服务 | 是(需外部 MCP) |
| vv065 | 笔记工具 (通过 MCP) | MCP 暴露的笔记工具 | 笔记操作结果 | 外部 MCP 服务 | 是(需外部 MCP) |
| vv066 | 记忆工具保存 | memory_save 工具 | 保存成功 | MemoryToolHandlers | 否 |
| vv067 | 记忆工具检索 | memory_retrieve 工具 | 检索结果 | MemoryToolHandlers | 否 |
| vv068 | gh 工具 pr list | `jcc gh pr list` | PR 列表 | GhSubCommand.cs | 否 |
| vv069 | gh 工具 issue | `jcc gh issue list` | Issue 列表 | GhSubCommand.cs | 否 |
| vv070 | cron 工具创建定时任务 | cron_create 工具 | 定时任务创建成功 | CronToolHandlers | 否 |
| vv071 | MCP 权限拒绝 | 权限模式拦截 MCP 工具 | permission_denials 记录 | CliEventConsumer.cs:142-149 | 否 |
| vv072 | MCPB 包加载 | 加载 MCPB 包 | 包加载成功/失败 | McpBLoader | 是(需 MCPB 包) |
| vv073 | ToolInterventionManager 黑名单 | 工具在黑名单中 | 工具被拦截 | ToolInterventionManager | 否 |
| vv074 | ToolInterventionManager 降权 | 工具被降权 | 工具以降权模式执行 | ToolInterventionManager | 否 |
| vv075 | ToolInterventionManager 重定向 | 工具被重定向 | 工具重定向到替代实现 | ToolInterventionManager | 否 |

---

## 领域4: 错误处理+退出码 (vv076-vv100)

| vv | 行为 | 触发条件 | 可观测信号 | 代码位置 | MockServer |
|----|------|---------|-----------|---------|-----------|
| vv076 | HTTP 200 成功 | LLM API 返回 200 | 正常响应 | ResilientHttpExecutor.cs:245 | 否 |
| vv077 | HTTP 401 认证错误 | LLM API 返回 401 | exit=10 LLM_CALL_FAILED + "API Key 无效" | ChatErrorHandlingMiddleware.cs:63 | 是(注入 401) |
| vv078 | HTTP 403 授权错误 | LLM API 返回 403 | exit=10 + "API 访问被拒绝" | ChatErrorHandlingMiddleware.cs:64 | 是(注入 403) |
| vv079 | HTTP 429 速率限制 | LLM API 返回 429 | 可重试 + "RateLimit" | ChatErrorHandlingMiddleware.cs:65 | 是(注入 429) |
| vv080 | HTTP 500 服务端错误 | LLM API 返回 500 | exit=10 + 响应体内容 | ResilientHttpExecutor.cs:257-264 | 是(注入 500) |
| vv081 | HTTP 502 网关错误 | LLM API 返回 502 | "网关错误(Bad Gateway)" | ChatErrorHandlingMiddleware.cs:66 | 是(注入 502) |
| vv082 | HTTP 503 服务不可用 | LLM API 返回 503 | "服务端暂时不可用" | ChatErrorHandlingMiddleware.cs:67 | 是(注入 503) |
| vv083 | 流式降级 fallback | 流式失败(502/503/529) | "[FALLBACK] 流式失败, 降级为非流式" | StreamingFallbackDecorator.cs:171-174 | 是(注入 502 流式+200 非流式) |
| vv084 | 连接级错误不 fallback | HttpRequestException(无 StatusCode) | 连接错误直接传播,不降级 | StreamingFallbackDecorator.cs:197-230 | 是(关闭 MockServer) |
| vv085 | 超时 → exit=1240 | LLM API 超时 | exit=1240 NET_TIMEOUT | NonInteractiveExecuteStep.cs:81-87 | 是(慢响应) |
| vv086 | --await 超时 → exit=1234 | `--await N` 超时 | exit=1234 TIMEOUT_AWAIT_DIAGNOSTIC | Program.cs:318-334 | 否 |
| vv087 | 空响应 {} | LLM 返回 `{}` | "模型返回空回复" | SessionController.cs:202-203 | 是(注入空对象) |
| vv088 | 空响应体 → SSE idle timeout | LLM 返回空 body/流卡死 | TimeoutException + 诱导信息 | OpenAIQueryService.cs:538-554 | 是(注入空 body) |
| vv089 | 重试耗尽 | 重试次数超限 | "error_max_retries" stop_reason | QueryEngine.cs:210-213 | 是(持续 500) |
| vv090 | 熔断器开启 | 连续失败触发熔断 | "熔断器开启" + CircuitBreakerOpenException | ResilientHttpExecutor.cs:53-56 | 是(持续 500) |
| vv091 | 预算耗尽 | 重试预算耗尽 | "重试预算耗尽" | ResilientHttpExecutor.cs:157-160 | 是(持续 500+小预算) |
| vv092 | Ctrl+C → exit=130 | 用户按 Ctrl+C | exit=130 Interrupted | Program.cs:103-106 | 否 |
| vv093 | 配置错误 → exit=2 | ConfigurationException | exit=2 CONFIG_INVALID_VALUE | Program.cs:116-134 | 否 |
| vv094 | API Key 缺失 → exit=4 | 非交互模式无 API Key | exit=4 AUTH_API_KEY_MISSING | NonInteractiveApiKeyCheckStep.cs:20 | 否 |
| vv095 | 参数解析错误 → exit=3 | 无效参数值 | exit=3 ARG_PARSE_ERROR | Program.cs:21,36,47 | 否 |
| vv096 | 会话恢复失败 → exit=5 | --resume 不存在的会话 | exit=5 RESOURCE_SESSION_NOT_FOUND | SessionResumeStep.cs:54-63 | 否 |
| vv097 | CrashStore 快照写入 | 未处理异常 | crash_{timestamp}.json + .log | Program.cs:480-554 | 否 |
| vv098 | 三层重试叠加 | 500 错误 | ResilientHttpExecutor × StreamingFallback × QueryEngine | 三层 | 是(持续 500) |
| vv099 | 异常分类递归检查 | HTTP 错误被包为 InvalidOperationException | ClassifyException 递归 InnerException 链 | NonInteractiveExecuteStep.cs:127-149 | 是(注入 500) |
| vv100 | content_filter stop_reason | LLM 返回 content_filter | "模型回复被安全过滤" | SessionController.cs:199-200 | 是(注入 content_filter) |

---

## 领域5: 多轮+子代理+会话 (vv101-vv125)

| vv | 行为 | 触发条件 | 可观测信号 | 代码位置 | MockServer |
|----|------|---------|-----------|---------|-----------|
| vv101 | --continue 恢复历史会话 | `jcc -c` 有历史会话 | "已恢复会话: {title} ({N} 条消息)" | SessionResumeStep.cs:42 | 否 |
| vv102 | --continue 无历史会话 | `jcc -c` 无历史 | "无历史会话可恢复，将启动新会话" | SessionResumeStep.cs:46-51 | 否 |
| vv103 | --resume <id> 精确匹配 | `jcc -r <UUID>` | 恢复成功 | SessionResumeStep.cs:123-128 | 否 |
| vv104 | --resume <id> 未找到 | `jcc -r <不存在>` | exit=5 "未找到会话" | SessionResumeStep.cs:54-63 | 否 |
| vv105 | --resume <title> 模糊匹配 | `jcc -r <关键词>` | customTitle Contains 匹配 | SessionResumeStep.cs:134-141 | 否 |
| vv106 | 多轮 messages 数组序列正确性 | --continue 后对话 | system→system→user→assistant→user | SessionResumeStep.cs:68-71 | 是(多轮 LLM 响应) |
| vv107 | transcript 写入 | 对话完成 | transcript.jsonl 追加条目 | TranscriptPersistMiddleware.cs:37-89 | 否 |
| vv108 | transcript 恢复 | --continue 加载 | 从 transcript.jsonl 加载历史 | TranscriptService.cs:58-62 | 否 |
| vv109 | 会话 ID 一致性 | NDJSON 输出 + transcript | session_id 字段一致 | CliEventConsumer.cs:36-43 | 否 |
| vv110 | subagent_type 解析 | agent 工具 subagent_type 参数 | PrimaryType + AllowedTypes | AgentTypeSpecParser.cs:12-26 | 否 |
| vv111 | 子代理启动 (agent_started) | 子代理首个 chunk | AgentStarted 事件 | AgentStreamExecutionMiddleware.cs:63 | 是(模拟 LLM 流) |
| vv112 | 子代理完成 (agent_finished) | 子代理完成 | AgentFinished 事件 + usage | AgentStreamExecutionMiddleware.cs:136 | 是 |
| vv113 | 子代理异常兜底 | 子代理 LLM 500 | 仍发射 agent_finished(success=false) | AgentStreamExecutionMiddleware.cs:117-136 | 是(注入 500) |
| vv114 | subagent_control list/pause/resume/stop | subagent_control 工具 | 子代理状态切换 | SubAgentControlToolHandlers.cs:44-56 | 是 |
| vv115 | 子代理 usage 聚合到主代理 | 子代理完成 | 主代理 usage += 子代理 usage | QueryEngine.cs:313-320 | 是 |
| vv116 | num_turns 计数 | 每轮 LLM 调用 | num_turns 递增 | QueryEngine.cs:175-179 | 是 |
| vv117 | 并发会话 | 多进程同时运行 | 各进程独立成功 | - | 是(多进程) |
| vv118 | Fork 子代理 | subagent_type 为空 + Fork 可用 | "Fork sub-agent launched" | AgentForkMiddleware.cs:123-167 | 是 |
| vv119 | teammate 子代理 | subagent_type 为空 + teammate 可用 | "Teammate sub-agent launched" | AgentForkMiddleware.cs:70-106 | 是 |
| vv120 | 子代理 steer 打断 | send_message 到运行中子代理 | "_steerRequested=true" | AgentBase.cs:516-519 | 是 |
| vv121 | 子代理从 transcript 恢复 | resume_from_transcript | "已从 transcript 恢复" | AgentServiceImpl.cs:476-548 | 是 |
| vv122 | 自动压缩 | 上下文超阈值 | OnCompactCompleted 事件 | SessionController.cs:355-395 | 是(大上下文) |
| vv123 | 上下文余量 | onDone | OnContextUsage 事件 | SessionController.cs:337-348 | 是 |
| vv124 | 权限拒绝列表累积 | 工具被权限拒绝 | permission_denials 累积 | CliEventConsumer.cs:142-149 | 否 |
| vv125 | 思考内容持久化 | thinking 内容非空 | ThinkingStore.StoreAsync | SessionController.cs:573-583 | 是(返回 thinking) |

---

## 领域6: 命令行+开关+权限 (vv126-vv150)

| vv | 行为 | 触发条件 | 可观测信号 | 代码位置 | MockServer |
|----|------|---------|-----------|---------|-----------|
| vv126 | chat 子命令 (默认) | `jcc -p "hello"` | 正常对话 | Program.cs | 是 |
| vv127 | --non-interactive 模式 | `jcc --non-interactive` | 非交互模式 | CliArg.cs:33 | 否 |
| vv128 | --format ndjson | `jcc --format ndjson` | NDJSON 事件流 | NonInteractiveModeRunner.cs:61-80 | 否 |
| vv129 | --format json | `jcc --format json` | `{ok,data,meta}` JSON 信封 | NonInteractiveModeRunner.cs:54-60 | 否 |
| vv130 | --format text | `jcc --format text` | 人类可读彩色输出 | NonInteractiveModeRunner.cs:83 | 否 |
| vv131 | --permission-mode bypass | `jcc --permission-mode bypass` | 所有操作自动批准 | PermissionMode.cs:26 | 否 |
| vv132 | --permission-mode plan | `jcc --permission-mode plan` | 读取自动批准+写入需确认 | PermissionMode.cs:11 | 否 |
| vv133 | --trust (不跳过权限) | `jcc --trust` | 仅跳过信任确认,不跳过权限检查 | StartupWorkflow.cs:170-208 | 否 |
| vv134 | --continue / -c | `jcc -c` | 恢复最近会话 | CliArg.cs:57 | 否 |
| vv135 | --await 超时 | `jcc --await 5` | 5秒后 exit=1234 | Program.cs:311-339 | 否 |
| vv136 | --dry-run | `jcc --dry-run` | 只显示将执行操作 | CliArg.cs:97 | 否 |
| vv137 | JCC_MODEL_ID | 环境变量设置 | 覆盖模型 ID | JccEnvVar.cs:11 | 否 |
| vv138 | JCC_ENDPOINT | 环境变量设置 | 覆盖 LLM 端点 | JccEnvVar.cs:14 | 否 |
| vv139 | JCC_PERMISSION_MODE | 环境变量设置 | 覆盖权限模式 | JccEnvVar.cs:108 | 否 |
| vv140 | JCC_DISABLE_STREAMING_FALLBACK | `=1` | 禁用流式降级 | StreamingFallbackConfig.cs:60 | 否 |
| vv141 | JCC_FILE_SYSTEM_MODE=InMemory | 环境变量设置 | 纯内存文件系统 | FileSystemFactory.cs | 否 |
| vv142 | JCC_HTTP_MODE=Mock | 环境变量设置 | Mock HTTP 拦截 | HttpClientProviderFactory.cs | 是(需预设响应) |
| vv143 | JCC_NO_MOUSE_TRACKING | `=1` | 禁用鼠标追踪走 OSC 8 | JccFeatureFlags.cs:18 | 否 |
| vv144 | JCC_DEBUGLOG | `=1` | 启用诊断日志 | Diag.cs:5 | 否 |
| vv145 | JCC_SKIP_MODEL_FETCH | `=1` | 跳过启动时 model list 拉取 | EngineSessionFactory.cs:186-192 | 否 |
| vv146 | schema 子命令 | `jcc schema` | CLI 参数定义 JSON | ApplicationBuilder.cs:44-50 | 否 |
| vv147 | config-print 子命令 | `jcc config-print` | 配置树 JSON | ConfigPrintCommand.cs:11-23 | 否 |
| vv148 | mcp_serve 子命令 | `jcc mcp_serve` | 启动 MCP 服务端 | McpServeCommand.cs:11-23 | 否 |
| vv149 | gh 子命令 | `jcc gh pr list` | PR 列表 | GhSubCommand.cs:10-39 | 否 |
| vv150 | 未知子命令错误 | `jcc <未知>` | "未知子命令" + exit=1 | ApplicationBuilder.cs:59-60 | 否 |

---

## 缺陷汇总（普查中发现）

| vv | 缺陷 | 严重度 | 状态 |
|----|------|--------|------|
| vv013 | result 事件缺 uuid/session_id 赋值（基准要求有） | 中 | ✅ 已修复（提取 SdkMessageBase + Create() 工厂方法） |
| vv088 | 空响应体导致卡死30秒（应优雅处理） | 高 | ✅ 已修复（OpenAIQueryService SSE idle timeout + StreamIdleTimeoutMs 属性） |
| vv099 | HTTP 异常被包为 InvalidOperationException 导致分类可能误判 | 中 | ✅ 已修复（ClassifyException 递归 InnerException + SessionTurnResult.OriginalException） |
| ~~子代理事件空实现~~ | ~~OnSubagentStarted/Finished NDJSON 模式不发射~~ | - | ✅ 非缺陷：基准侧 SDK 也无 agent_started/finished 事件，空实现正确对齐 |
| ~~OnContextUsage/OnLoopDetected/OnTimingSummary 空实现~~ | ~~上下文余量/循环检测/计时摘要不发射~~ | - | ✅ 非缺陷：基准侧 SDK 也无这些事件类型，空实现正确对齐 |

---

## 检测方法

### 自然触发（不需 MockServer）
直接跑 jcc 命令，检查输出/退出码/文件：
```bash
jcc -p "hello" --format ndjson --trust --permission-mode bypass 2>&1 | python validate.py
```

### MockServer 构造条件
启动 MockServer 注入特定响应，逼出目标行为：
```bash
# 1. 启动 MockServer
dotnet artifacts/bin/OpenAI.MockServer/Debug/net10.0/JoinCode.OpenAI.MockServer.dll --port 8093 &
# 2. 注入特定响应
curl -X POST http://127.0.0.1:8093/__control__/inject -d '{"is_stream":true,"content_type":"text/event-stream","stream_chunks":[...]}'
# 3. 跑 jcc 检测
JCC_ENDPOINT=http://127.0.0.1:8093/v1 jcc -p "test" --format ndjson --trust --permission-mode bypass
# 4. 验证输出
python validate_vvXXX.py
```

---

## 自动决策记录

<!-- 🤖 Auto Decision: 2026-11-06 -->
<!-- 决策: 派 6 个 explore 子代理并行普查 jcc 全功能行为分支 -->
<!-- 原因: 用户要求全面调查所有功能（包括 TODO/笔记/记忆等 MCP 工具），不只是 NDJSON 输出 -->
<!-- 替代方案: 手动逐文件阅读（太慢，6 个子代理并行覆盖 800+ 行为分支） -->
<!-- 验证: 6 个子代理全部返回详细报告 ✅ -->
