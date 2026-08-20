# Responses API 支持任务 + 协议自由切换

## 需求
1. 每个供应商可自由切换协议(修 Azure 硬编码)
2. 支持 Responses API(DeepSeek-V4-Flash 原生支持,OpenAI 新协议)
3. 所有模型都支持 Responses API(协议层,配置选择)

## Responses API 格式(DeepSeek 官方,对齐 OpenAI)
- 端点:`POST /responses`(base_url `https://api.deepseek.com`)
- 请求:`model` + `input`(字符串或 item 列表) + `instructions`(system) + `stream` + `temperature` + `top_p` + `max_output_tokens` + `tools` + `tool_choice` + `reasoning`(effort)
- 响应:`output` 数组(message/reasoning/function_call items) + `output_text` 便捷字段 + `usage`(input_tokens/output_tokens)
- 流式:语义化 SSE 事件(`response.created` / `response.output_text.delta` / `response.completed`),**无 `data: [DONE]`**
- 无状态:不支持 previous_response_id/store/background
- 认证:`Authorization: Bearer`(同 OpenAI)

## 任务拆分(TDD 渐进式)

### 任务R1:ProtocolKind 加 OpenAiResponses 枚举 ✅
- `[EnumValue("responses")] OpenAiResponses = 4`
- 全量重建(源码生成器)
- 提交: 3e390a731

### 任务R2:Responses API DTO + JsonContext ✅
- ResponsesRequest(model/input/instructions/stream/temperature/top_p/max_output_tokens/tools/tool_choice/reasoning)
- ResponsesResponse(id/object/model/output/usage/status/output_text)
- ResponsesOutputItem(type/message/reasoning/function_call)
- ResponsesUsage(input_tokens/output_tokens)
- NativeJsonContext 注册,7 个序列化往返测试
- 提交: cde5290c4

### 任务R3:ResponsesQueryService 实现 ✅
- 继承 QueryServiceBase
- CreateRequest:MessageList → ResponsesRequest(input + instructions),AOT 安全(字符串拼接,无 JsonNode)
- 非流式:POST /responses → 反序列化 ResponsesResponse → ApiMessage
- 流式:解析 SSE event(response.output_text.delta 等) → StreamEvent
- 复用 SendWithResilienceAsync
- 21 个单元测试全通过
- 提交: 8cc95a57e

### 任务R4:QueryServiceFactory 分派 ✅
- ProtocolKind.OpenAiResponses → ResponsesQueryService
- 提交: d8d3a1989

### 任务R5:协议端点支持 responses ✅
- OpenAiCompatibleProviderDefinition.GetChatEndpoint:config.ProtocolKind=Responses → "responses"
- FallbackProviderDefinition + OpenAICompatibleProviderDefinitionBase 同步
- ProviderDefinitionRegistry:protocol "responses" → OpenAiCompatibleProviderDefinition(else 分支已覆盖)
- 提交: b8b528047

### 任务R6:Azure 硬编码修复(协议自由切换) ✅
- ProviderDefinitionRegistry:Azure 未在 settings.json 配置时才回退到内置 AzureProviderDefinition
- Azure 可配 protocol 切换协议(配置大于代码)
- 3 个新测试验证:未配回退 / 配了不覆盖 / responses 协议端点
- 提交: d3af7ac0a

### 任务R7:文档 + 全量测试 ✅
- Llm.Tests: 351 通过
- Guard.Config.Tests: 858 通过
- Brain.Context.Tests: 755 通过
- 总计 1964 测试全通过

## E2E 验证(2026-08-20)

### Responses.MockServer
- 新增 `tests/MockServers/Responses.MockServer/` 项目,返回 Responses API 格式
- 非流式:`output` 数组 + `output_text` 便捷字段 ✅
- 流式 SSE:`response.created` → `response.output_text.delta`(逐词) → `response.completed` ✅
- 工具调用流式:`response.output_item.added` → `response.function_call_arguments.delta` → `response.completed` ✅

### jcc.exe E2E
- ✅ jcc.exe 正确读取 `JCC_PROTOCOL=responses` 环境变量
- ✅ jcc.exe 连接到正确端点 `/responses`(非 `/chat/completions`)
- ✅ 供应商/模型/端点配置正确(deepseek + deepseek-v4-flash + localhost:port)
- ✅ 流式 SSE 事件正确解析(11 chunks,无降级)
- ✅ 完整对话链路:文本响应 → 工具调用 → 跟进文本
- ✅ SettingsMapper 修复:优先从 settings.json profile 读取 protocol,不被 Definition.Protocol 覆盖

### ⚠️ 后续待办
- `JCC_APP_DATA_FOLDER` 环境变量与 `ConfigLoader.LoadSettingsJsonAsync` 的路径解析链路需排查(settings.json 在自定义目录下未被加载)

## 核心原则
- 配置大于代码:protocol:"responses" 即走 Responses API
- 所有供应商可选 responses 协议(配置驱动)
- Responses API 无状态,不实现 previous_response_id/store

<!-- 🤖 Auto Decision: 2026-08-20 -->
<!-- 决策: Responses API 作为独立 ProtocolKind.OpenAiResponses,而非复用 OpenAiCompatible -->
<!-- 原因: 请求/响应/流式格式完全不同(input vs messages,output vs choices,event SSE vs data SSE),复用会增加条件分支复杂度 -->
<!-- 替代方案: 在 OpenAIQueryService 内按协议分支(违反单一职责,不采用) -->

## 协议核查(2026-08-21) — 三个缺口调查结论

用户提供的自研 Responses API 封装设计存在两处协议错误,项目现有实现反而更接近官方规范:
- **tool 格式**: 用户设计用 Chat Completions 嵌套 `{"type":"function","function":{...}}`;Responses API 官方是**扁平格式** `{"type":"function","name":...,"description":...,"parameters":{...}}`,项目 `ResponsesTool` 正确 ✅
- **input item**: 用户设计 `{role, content}` 缺 `type`;官方 item 必须带 `{"type":"message","role":...,"content":[{"type":"input_text"/"output_text","text":...}]}`,项目 `CreateRequest` 正确构建 ✅

项目实现经逐项核对后确认存在 3 个缺口,本轮已全部处理:

### 缺口1 ✅ 已修复: response.incomplete 终结事件未处理
- **现象**: 流式 switch 只处理 `response.completed`/`response.failed`;当 `max_output_tokens` 截断时服务端发 `response.incomplete`,当前实现静默结束流,丢失 usage 和已累积的 tool_calls
- **修复**: switch 合并 `case "response.completed": case "response.incomplete":`(主循环 + 两阶段加载第二段循环),FinishReason="stop"(无 tool) / "tool_calls"(有 tool),usage 从 `response.usage` 提取,`yield break`
- **测试**: 2 个红测试(纯文本 + 工具调用截断) → 修复后绿

### 缺口2 ✅ 已评估: tool_groups 仅 MockServer 有效 — 设计决策,不改代码
- **现象**: `tool_groups` + `tool_description_request` 是自定义协议,真实 DeepSeek API 忽略这两个字段
- **结论**: **非协议错误,是两阶段工具加载的设计权衡**:
  - `OpenAITypes.cs` 注释明确"真实 LLM API 忽略此字段"
  - 系统提示词 `ToolsSection.cs` 说明:真实 API 下 MCP 工具不内联进 `tools` 数组,而是通过 **ToolSearch** 按需加载(模型调用 `tool_search` 系统工具 → 返回 `tool_description_request` → QueryService 检测到后发第二次请求补齐 tool schema)
  - `tool_groups` 只把工具分组传给 MockServer,让模拟环境免去 ToolSearch 往返
- **决策**: 保留现状,不修改代码。两阶段加载已在 c4514f51a 落地,与 MockServer 的 tool_groups 各司其职
- **待验证**: 真实 DeepSeek API 端到端 ToolSearch 链路尚未实测(缺 API key),列为后续集成测试项

### 缺口3 ✅ 已修复: SSE 无 event: 前缀时无容错
- **现象**: 解析依赖 `event: xxx` 前缀;若服务端/网关只发 `data: {...}`(data 内含 `type` 字段),当前实现丢弃所有事件
- **修复**: 每个 data 行解析后,优先从 `data.type` 字段推断事件类型(官方完整格式 data 均带 type),无 type 时回退到 `event:` 前缀。兼容两种格式
- **测试**: 1 个红测试(纯 data 无前缀流) → 修复后绿

### 验证
- Llm.Tests: 355 全通过(含新增 3 个流式测试)

### ⚠️ 后续待办
- 真实 DeepSeek API(非 MockServer)端到端验证 tool_description_request 两阶段加载链路
- 确认 `max_output_tokens` 截断时 FinishReason 是否应标记为 `length`(当前为 stop,待真实 API 行为确认)

## 真实 API 验证(2026-08-21) — Responses 协议链路修复

### 触发场景
用户配置了真实 DeepSeek key,运行 jcc.exe 调用工具时报 400 Bad Request。诊断出两处**协议错误**:

### 根因1 ✅ 已修复: 工具结果必须用 function_call_output item
- **错误格式**(会 400): `{"type":"message","role":"tool","content":[{"type":"input_text","text":"..."}]}`
- **正确格式**: `{"type":"function_call_output","call_id":"...","output":"..."}`
- **修复**: `CreateRequest` 历史循环 — tool 消息 → `function_call_output` item(call_id 从 ToolCallId metadata 读,output=content);assistant 带 ToolCalls/AllToolCalls → `function_call` item(call_id/name/arguments),然后 continue(工具轮 assistant content=null 安全);新增辅助方法 `AppendItem`/`AppendFunctionCallOutput`
- **测试**: 3 个红测试(工具历史/带 reasoning 的历史/工具+reasoning 组合) → 绿

### 根因2 ✅ 已修复: thinking 模式必须回传 reasoning_text
- **错误**: DeepSeek 报 `The reasoning_text in the thinking mode must be passed back to the API.`(缺失时 400)
- **正确格式**: `{"type":"reasoning","content":[{"type":"reasoning_text","text":"..."}]}`
- **修复**: 
  - 响应侧: `MessageMetadataKey` 新增 `[EnumValue("ReasoningText")] ReasoningText`;`ConvertToApiMessages` 非流式累积 reasoning item → assistant metadata;流式 `response.reasoning_text.delta` 用 `reasoningAccumulator` 累积,终局事件(completed/incomplete)写入 metadata
  - 请求侧: `CreateRequest` 读 assistant metadata 的 ReasoningText → 输出 `reasoning` item(在 function_call 之前)
- **测试**: 5 个红测试(非流式/无 reasoning 时缺键/带工具调用/流式累积/请求回传) → 绿

### 手工 curl 逐项验证(确认根因)
| 场景 | 结果 |
|------|------|
| tool_groups 单独 | 200(DeepSeek 忽略) |
| 全字段组合(instructions+reasoning+tool_choice+tools+tool_groups) | 200 |
| stream=true | 200 |
| 290 工具全量(66KB/198KB 请求体) | 200(非大小问题) |
| `role=tool` message | **400** ❌ |
| function_call+function_call_output 无 reasoning | **400** ❌ |
| 带 reasoning item | **200** ✅ |

### 真实 API 结果
- ✅ DeepSeek(Responses 协议)单轮工具调用链路跑通:Grep 工具执行成功返回 4 个文件,无 400
- ✅ sensenova(openai-compatible)多轮工具循环跑通:Grep 定位 → file_snip_lines 读取 → 输出答案,历史正确回传
- ✅ agnes(openai-compatible)对话连通
- 验证后移除临时诊断日志 `[WIRE-REQ]`/`[WIRE-REQ-BODY]`/`[WIRE-ERR]`

### 提交
- 38362c52d: 响应侧非流式 reasoning 存入 assistant metadata
- 4ff0731b1: 流式 reasoning_text.delta 累积
- 01e961975: 请求侧工具历史转 function_call/function_call_output/reasoning items
- 05b8c760c: 移除调试日志

### ⚠️ 后续待办
- Responses 协议多轮工具循环真实 API 验证未完成(用户暂停 DeepSeek 测试,key 已 401),多轮历史转换由单元测试覆盖
- 真实 DeepSeek API 端到端验证 tool_description_request 两阶段加载链路

## 真实 API 多轮验证完成(2026-08-21 第二轮,新 key)

用户提供新 DeepSeek key 后完成 **Responses 协议多轮工具循环**真实 API 验证:

- ✅ **2 轮工具循环**: Grep 搜索 `ReasoningText` → Read 读取文件前 2 行,成功输出,无 400
- ✅ **3 轮工具循环**: Grep 搜索 `ResponsesQueryServiceTests` → Read 前 3 行 → 再 Grep `SendRequestAsync` → 全部成功
- ✅ 验证了多轮累积历史(多个 function_call_output + reasoning 回传)在真实 API 下正常,修复完整生效
- 测试后移除临时诊断日志,工作区干净(Llm.Tests 380 全绿)

**遗留待办更新**:
- ~~Responses 多轮工具循环真实 API 验证~~ → ✅ 已验证通过(2026-08-21)
- 真实 DeepSeek API 端到端验证 tool_description_request 两阶段加载链路(仍需配置 MCP 工具触发 ToolSearch)

## MCP ToolSearch 链路验证与修复(2026-08-21)

### 触发场景
用 sensenova + Mcp.MockServer(http://localhost:18090) 验证 MCP 工具两阶段加载,发现 ToolSearch 搜不到 MCP 工具。

### Bug: mcp_connect 连接后未同步远程工具
- **现象**: `mcp_connect` 成功连接后,`tool_search` 搜索 "echo" 返回"未找到匹配的工具"("共有 311 个已注册的工具可用"),但 `mcp_list_tools` 能列出 5 个工具、`mcp_call_tool` 能正常调用
- **根因**: `ToolSearchToolHandlers` 用 `_toolRegistry.GetAllToolsAsync()` 搜索(本地注册表),而 `mcp_connect`(`McpClientToolHandlers.McpConnectAsync`)连接后只调 `RegisterRemoteClient` → `RemoteClientManager.RegisterClientAsync`,**未触发 `SyncToolsAsync`**,远程工具从未注册进 `_toolRegistry`。`SyncToolsAsync` 仅在重连/收到通知时调用(RemoteClientManager:251/69)
- **修复**: `McpConnectAsync` 连接成功后调用 `SyncRemoteToolsAsync(connection_name)`,把远程工具同步进注册表(工具名格式 `mcp__{clientId}__{toolName}`)
- **测试**: 红测试(连接成功→应同步)→ 修复 → 绿;Mcp.Tests 140 全绿
- **提交**: bfce3368c

### 真实 API 验证(sensenova + Mcp.MockServer)
```
[Tool] mcp_connect → 连接成功 (mock, JoinCode.Mcp.MockServer 1.0.0)
[Tool] ToolSearch (echo) → 找到 mcp__mock__echo: Echo back the input message (匹配 1 个工具, 共 316 个)
[Tool] mcp_call_tool → echo 回显 hello-mcp 成功
```
- ✅ 修复前: ToolSearch "未找到匹配的工具";修复后: 找到 `mcp__mock__echo` 并成功调用
- ✅ 完整链路验证: 网络通讯(MCP Streamable HTTP) + 工具搜索(ToolSearch) + MCP 工具调用(mcp_call_tool) 全部正常

### 遗留观察
- `SessionController.PostProcessMainAgentAsync` 在模型仅工具调用无文本输出时,`AddAssistantMessageAsync` 收到空白串抛 `ThrowIfNullOrWhiteSpace`(被 catch 记录,不影响主链路)。app 层测试受 JoinCodeTui 缺 libs 编译阻塞,未修复(用户明确不处理 TUI,聚焦底层)
