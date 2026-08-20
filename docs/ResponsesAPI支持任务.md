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
- ✅ jcc.exe 正确读取 `protocol: "responses"` 配置
- ✅ jcc.exe 连接到正确端点 `/responses`(非 `/chat/completions`)
- ✅ 供应商/模型/端点配置正确(deepseek + deepseek-v4-flash + localhost:port)
- ⚠️ 流式降级为非流式(`StreamingFallbackDecorator` 在 Responses 协议下降级走了 `/chat/completions`)

### ⚠️ 后续待办
- `StreamingFallbackDecorator` 协议感知:流式失败降级时应走同一协议端点(`/responses`),而非硬编码 `/chat/completions`
- `ResponsesQueryService.GetStreamEventContentsAsync` 流式解析与 `StreamingFallbackDecorator` 集成调试

## 核心原则
- 配置大于代码:protocol:"responses" 即走 Responses API
- 所有供应商可选 responses 协议(配置驱动)
- Responses API 无状态,不实现 previous_response_id/store

<!-- 🤖 Auto Decision: 2026-08-20 -->
<!-- 决策: Responses API 作为独立 ProtocolKind.OpenAiResponses,而非复用 OpenAiCompatible -->
<!-- 原因: 请求/响应/流式格式完全不同(input vs messages,output vs choices,event SSE vs data SSE),复用会增加条件分支复杂度 -->
<!-- 替代方案: 在 OpenAIQueryService 内按协议分支(违反单一职责,不采用) -->
