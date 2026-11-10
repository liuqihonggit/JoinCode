# ADR 0155: AgentHubServer — 多 Agent 中央服务器架构

**状态**: proposed  
**日期**: 2026-11-10  

## 背景

当前 jcc.exe 实例各自直连 LLM API，每个实例需要独立配置 API key 和模型。用户希望：

1. 多个 jcc.exe 窗口同时运行，通过一个中央服务器转发消息
2. jcc 实例只用 jcc.exe 收发消息，发送到同一个服务器地址
3. 启动 jcc.exe 后自动启动服务器
4. 服务器端统一管理所有模型对接（API key、endpoint、模型路由）

## 决策

新建 **AgentHubServer** — jcc.exe 启动时自动启动的中央 HTTP 服务器，同时承担：

### 1. 模型代理网关
- 接收 jcc 实例的 LLM 请求（OpenAI/Anthropic 格式）
- 根据服务器端配置选择 Provider 和 API key
- 转发请求到真实 LLM API，流式响应透传（SSE）

### 2. Agent 消息转发中枢
- jcc 实例注册到服务器（agentId + WebSocket 连接）
- Agent 间消息通过服务器路由（定向/广播）
- 复用现有 IMailbox 接口语义

### 架构图

```
[jcc.exe #1] ──┐
[jcc.exe #2] ──┼──→ [AgentHubServer :port] ──→ [LLM API]
[jcc.exe #3] ──┘         │
                          ├──→ [OpenAI API]
                          ├──→ [Anthropic API]
                          ├──→ [DeepSeek API]
                          └──→ [其他模型 API]
```

### 关键组件

| 组件 | 职责 | 复用 |
|------|------|------|
| `AgentHubServer` | HTTP 服务器，模型代理 + Agent 消息转发 | BridgeServer 的 HttpListener 模式 |
| `HubQueryService` | jcc 实例的 LLM 请求通过 HTTP 发给 AgentHubServer | PipeQueryService 的代理模式 |
| `ProtocolKind.Hub` | 新协议类型 | ProtocolKind 枚举扩展 |
| `AgentHubConfig` | 服务器端模型配置管理 | settings.json 的 vendor 字典 |
| `AgentHubBootstrap` | 启动时检测/启动服务器 | Program.cs 启动流程扩展 |

### 启动机制

1. jcc.exe 启动 → 检测 AgentHubServer 端口（默认 3457）
2. 端口未占用 → 启动 AgentHubServer（后台线程）
3. 端口已占用 → 直接连接已运行的服务器
4. jcc 实例注册 agentId

### 模型配置

- 所有模型对接配置（API key、endpoint、模型列表）只在服务器端管理
- jcc 实例不需要任何模型配置，只发请求给服务器
- 服务器端配置文件：`~/.jcc/hub.json`

### 考虑过的替代方案

1. **扩展现有 BridgeServer** — 否决：BridgeServer 职责是 IDE 桥接，耦合会导致职责过载
2. **纯消息转发（不做模型代理）** — 否决：用户明确选择二合一方案
3. **独立命令显式启动** — 否决：用户选择随首个 jcc 自动启动

## 影响

- 新增 `ProtocolKind.Hub` 枚举值
- 新增 `HubQueryService` 实现 `IQueryService`
- 新增 `AgentHubServer` HTTP 服务器
- 修改 jcc.exe 启动流程（Program.cs）
- 修改 `ServiceRegistration` 注册 Hub 协议
- 新增 `hub.json` 配置文件格式
