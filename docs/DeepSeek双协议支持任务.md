# DeepSeek 双协议支持任务

## 目标
DeepSeek 官方同时支持 OpenAI 兼容协议和 Anthropic 兼容协议。让用户在 settings.json 配什么 `protocol` 就走什么协议,给用户最大操作度。

## 核心原则:配置大于代码
- `protocol` 字段是唯一真相源,决定走哪个 QueryService
- 代码不硬编码供应商→协议映射
- 端点、认证头、beta 特性都应可配置覆盖
- 供应商身份(Vendor)从 providerName 推导,不因协议改变而丢失

## DeepSeek 官方协议(2026-08 查证)
| 协议 | base_url | 端点 | 认证 |
|------|----------|------|------|
| OpenAI 兼容 | `https://api.deepseek.com` | `/chat/completions` | `Authorization: Bearer` |
| Anthropic 兼容 | `https://api.deepseek.com/anthropic` | `/v1/messages` | `x-api-key`+`anthropic-version` |
模型: `deepseek-v4-flash` / `deepseek-v4-pro`,扩展字段 `thinking:{"type":"enabled"}` + `reasoning_effort`

## 当前架构阻断点
`AnthropicProviderDefinition.cs` 硬编码 Anthropic 供应商身份(Vendor=:20, DisplayName=:23, GetBaseUrl=:31 回退 anthropic.com),导致 DeepSeek 配 `protocol:"anthropic"` 时身份丢失、端点错发。`OpenAiCompatibleProviderDefinition` 已通用化,Anthropic 侧未对齐。

## 任务拆分(TDD 渐进式)

### 任务1:新增 AnthropicCompatibleProviderDefinition(通用化)
- 新建类 `core/safety/Guard/src/Configuration/Configuration2/Core/Providers/Shared/AnthropicCompatibleProviderDefinition.cs`
- Vendor 从 providerName 推导(对齐 OpenAiCompatible 模式)
- DisplayName = providerName
- GetBaseUrl 优先 config.Endpoint,回退:providerName=anthropic→官方,其他→抛异常强制配置(避免静默错发)
- 认证头:x-api-key + anthropic-version(协议固有)
- 红测试→实现→编译→绿测试→提交

### 任务2:ProviderDefinitionRegistry 分派逻辑升级
- `protocol:"anthropic"` → AnthropicCompatibleProviderDefinition(通用类)
- 原 AnthropicProviderDefinition 保留为 Anthropic 供应商特化(继承或直接被替代)
- 红测试→实现→编译→绿测试→提交

### 任务3:OpenAIChatRequest 补 thinking 字段
- 新增 `thinking` 字段 + OpenAIThinkingOptions DTO
- NativeJsonContext 注册
- 红测试→实现→编译→绿测试→提交

### 任务4:OpenAIQueryService 按配置发送 thinking 字段
- 构造请求时按供应商能力+配置决定是否发送 thinking
- 红测试→实现→编译→绿测试→提交

### 任务5:文档更新 + E2E 验证
- settings.json 配置示例(双协议可切换)
- E2E 测试验证 DeepSeek 走 Anthropic 协议

## 决策依据
- 不引入 ProtocolKind.DeepSeekAnthropic:协议枚举描述"怎么发",DeepSeek 用 Anthropic 协议就是 ProtocolKind.Anthropic
- 不做运行时双协议故障转移:与"配置大于代码"相反,用户主动选择协议
- AnthropicQueryService(835行)不动:已是协议无关纯实现
- QueryServiceFactory 分派不动:已按 ProtocolKind 正确路由

<!-- 🤖 Auto Decision: 2026-08-20 -->
<!-- 决策: 新增 AnthropicCompatibleProviderDefinition 通用类,而非改造原 AnthropicProviderDefinition -->
<!-- 原因: 对齐 OpenAiCompatibleProviderDefinition 的通用化模式,保持架构对称,原类可保留为 Anthropic 供应商特化 -->
<!-- 替代方案: 直接改造原类(风险:破坏现有 Anthropic 测试基线) -->
