# ADR 0155: AgentHubServer — 多 Agent 中央服务器架构（独立进程）

**状态**: proposed  
**日期**: 2026-11-10  

## 背景

当前 jcc.exe 实例各自直连 LLM API，每个实例需要独立配置 API key 和模型。用户希望：

1. 多个 jcc.exe 窗口同时运行，通过一个中央服务器转发消息
2. jcc 实例只用 jcc.exe 收发消息，发送到同一个服务器地址
3. 启动 jcc.exe 后自动拉起服务器
4. 服务器端统一管理所有模型对接（API key、endpoint、模型路由）
5. 大量链接逻辑、模型逻辑、校验配置逻辑从客户端移除
6. 服务端代码调整不影响客户端
7. 一切默认，不需要环境变量控制

## 决策

新建 **jcchub.exe** — 独立可执行进程，作为多 Agent 中央服务器。jcc.exe 启动时自动通过 `Process.Start` 拉起 jcchub.exe，固定端口 3457。

### 架构图

```
[jcc.exe #1] ──┐
[jcc.exe #2] ──┼──→ HTTP ──→ [jcchub.exe :3457] ──→ [LLM API]
[jcc.exe #3] ──┘                │
                                ├──→ [OpenAI API]
                                ├──→ [Anthropic API]
                                ├──→ [DeepSeek API]
                                └──→ [其他模型 API]
```

### 进程隔离

- **jcchub.exe** 是独立进程，不是 jcc.exe 进程内的线程
- 客户端通过 `Process.Start` 拉起 jcchub.exe，固定端口 3457
- 服务端代码调整不影响客户端（只要 HTTP API 契约不变）
- 不需要环境变量控制 — 一切默认

### 客户端瘦身

当 Hub 服务可用时，客户端跳过所有 Provider 配置加载：

| 跳过的步骤 | 原因 |
|-----------|------|
| ProviderDefinitionRegistry 创建 | 服务端已有 |
| ConfigLoader 7 步管道 | 只需 Hub endpoint，不需要 Provider 配置 |
| API Key 解析（auth.json/apiKeyHelper/环境变量） | 服务端统一处理认证 |
| Provider 验证 | 服务端已有验证逻辑 |
| NonInteractiveApiKeyCheckStep | Hub 协议不需要客户端 API Key |

客户端保留的逻辑：
- settings.json current 部分（UI 偏好、行为开关）
- 规则文件加载
- CLI 参数解析
- Hub endpoint（固定 `http://localhost:3457`）

### 关键组件

| 组件 | 职责 | 位置 |
|------|------|------|
| `jcchub.exe` (Program.cs) | 独立进程入口点，加载配置+启动服务器 | `modules/services/hub/app/Program.cs` |
| `AgentHubServer` | HTTP 服务器，模型代理 + Agent 消息转发 | `modules/services/hub/server/AgentHubServer.cs` |
| `HubQueryService` | jcc 实例的 LLM 请求通过 HTTP 发给 AgentHubServer | `modules/llm/core/Adapters/Chat/services/HubQueryService.cs` |
| `ProtocolKind.Hub` | 新协议类型 | `modules/abstractions/primitives/core_configuration/providers/ProtocolKind.cs` |
| `LoadHubConfigAsync` | Hub 模式简化配置加载（跳过 Provider 步骤） | `modules/apps/engine/app/builder/EngineApplicationBuilder.cs` |

### HTTP API

| 端点 | 方法 | 说明 |
|------|------|------|
| `/health` | GET | 健康检查（状态 + Agent 数 + Provider 数） |
| `/agents` | GET | 已注册 Agent 列表 |
| `/v1/models` | GET | 可用模型列表（兼容 OpenAI 格式） |
| `/v1/chat/completions` | POST | LLM 请求转发（流式/非流式） |
| `/agent/register` | POST | Agent 注册 |
| `/agent/unregister` | POST | Agent 注销 |
| `/agent/message` | POST | Agent 间消息转发（定向/广播） |

### 启动机制

1. jcc.exe 启动 → `EnsureHubServerAsync()` 检测端口 3457
2. 端口无响应 → `Process.Start("jcchub.exe")` 拉起独立进程
3. 等待服务就绪（最多 6s，30 次 × 200ms health check）
4. `LoadHubConfigAsync()` 构造 Hub 协议 WorkflowConfig（跳过 Provider 配置）
5. jcc 实例通过 `HubQueryService` 发送 LLM 请求到 `http://localhost:3457`

### 模型配置

- 所有模型对接配置（API key、endpoint、模型列表）只在 jcchub.exe 服务端管理
- 服务端复用 `ConfigLoader.LoadSettingsJsonAsync` 加载 `~/.jcc/settings.json`
- 服务端复用 `ProviderDefinitionRegistry.Create` 构建 Provider 注册表
- jcc 客户端不需要任何模型配置，只发 HTTP 请求给服务端

### 考虑过的替代方案

1. **扩展现有 BridgeServer** — 否决：BridgeServer 职责是 IDE 桥接，耦合会导致职责过载
2. **纯消息转发（不做模型代理）** — 否决：用户明确选择二合一方案
3. **独立命令显式启动** — 否决：用户选择随首个 jcc 自动拉起
4. **jcc.exe 进程内线程** — 否决：用户要求独立 exe 进程，进程级隔离
5. **环境变量控制端口** — 否决：用户要求一切默认，不需要变量控制

## 影响

- 新增 `ProtocolKind.Hub` 枚举值
- 新增 `HubQueryService` 实现 `IQueryService`
- 新增 `jcchub.exe` 独立可执行项目（`Hub.csproj` 改为 Exe）
- 新增 `LoadHubConfigAsync` Hub 模式简化配置加载
- 修改 `NonInteractiveApiKeyCheckStep` — Hub 协议跳过 API Key 检查
- 修改 `EngineApplicationBuilder.LoadConfigAsync` — Hub 服务可用时走简化路径
- 修改 jcc.exe `Program.cs` — `EnsureHubServerAsync` 拉起 jcchub.exe
- 从 `JoinCode.csproj` 移除 Hub 项目引用（客户端不再直接依赖 Hub 代码）
- 移除 `AgentHubBootstrap`（逻辑移到 jcchub.exe Program.cs）
- 移除 `JCC_HUB_PORT` 环境变量（固定端口 3457）
