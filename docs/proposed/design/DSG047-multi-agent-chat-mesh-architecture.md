# DSG047 — 多 Agent 群聊 Mesh 架构

> 状态：proposed
> 日期：2026-11-12
> 参考：DSH Super-agent（2026-10-31）+ Google Harness²（arXiv:2610.14224）

## 一、背景与目标

### 1.1 问题

用户希望 codex、ClaudeCode、jcchub、jcc 四者能像 QQ 群聊一样串联聊天，核心诉求：

- **群聊模式**：所有参与者能看到彼此的消息
- **聊天记录长期有效**：重启不丢失
- **网络抖动容错**：断线重连不丢消息
- **本地打通**：localhost 即可运行
- **方便接入网络**：可扩展到远程
- **方便接入真实 LLM**：统一模型适配层

### 1.2 目标

设计一套基于现有 Hub 基础设施的多 Agent 群聊架构，实现：

1. 任意 Agent（codex/ClaudeCode/jcc）可加入群聊会话
2. 消息在所有参与者间广播，上下文各自维护
3. 聊天记录持久化，重启可恢复
4. 网络抖动下消息不丢，断线可续
5. 模型适配层支持任意 LLM 即插即用
6. 经验可跨任务继承（Harness² 理念）

## 二、参考架构

### 2.1 DSH Super-agent（DeepSeek Harness, 2026-10-31）

**核心设计：双层 Router + Worker 架构**

| 模块 | 设计要点 |
|---|---|
| 双层架构 | Router（常驻，只分派不执行，维护全局上下文）+ Worker（上下文隔离，只干活不对外说话，结果由 Router 统一回复） |
| 事件驱动 | 环境事件当输入，两级过滤：程序层（Fast Path，确定性条件筛噪音）+ 语义层（Slow Path，大模型判断是否行动） |
| 订阅直通 | Worker 接手任务后直接订阅 Thread，后续事件不绕 Router；订阅多对多，两个 Worker 订阅同一 Thread 能看到彼此操作 |
| 上下文缓存 | Inline & Spill：高频信息内联进上下文，长内容截断留续读入口；每个对象内联下一跳 ID，链式召回 |
| 持久在线 | 存算分离：计算放云端，会话状态单独存，代码仓库留本地；外部 Watchdog 定时巡检 |

**四特征**：连续（跨渠道理解同一件事）、高效（多任务并行）、主动（持续观察环境事件）、持久（始终在线，异常自恢复）

### 2.2 Google Harness²（arXiv:2610.14224, 2026-10-09）

**核心设计：递归自我改进（RSI），不修改模型，改进 harness 本身**

| 模块 | 设计要点 |
|---|---|
| 任务级递归 | Propose-Probe-Compose 三步：对比提出修改 → 试跑验证 → 组合确认，无需 ground-truth verifier |
| Harnessing级递归 | 从任务编辑中提取经验，更新持续改进 harness，让 agent 更好适配未来任务 |
| 继承操作 | 不修改模型权重，通过调整系统提示/工具/记忆/护栏来改进；经验跨任务继承 |
| Plug-and-play | 支持多种模型（GLM-5、Gemini 3.5 Flash 等），harness 可适配不同模型 |

**关键洞察**：Agent 的能力不只取决于模型，还取决于执行 harness。改进 harness = 改进 Agent 能力，无需重新训练模型。

## 三、整体架构

### 3.1 角色定义

```
┌─────────────────────────────────────────────────────────┐
│                    用户（人）                            │
│              通过任意窗口参与群聊                         │
└────────────┬────────────────────────────────────────────┘
             │
    ┌────────┴────────┬────────────────┐
    │                 │                │
┌───▼────┐    ┌──────▼──────┐  ┌──────▼──────┐
│  jcc   │    │   codex     │  │ ClaudeCode  │
│ Worker │    │   Worker    │  │   Worker    │
└───┬────┘    └──────┬──────┘  └──────┬──────┘
    │                 │                │
    └────────┬────────┴────────────────┘
             │
┌────────────▼────────────────────────────────────────────┐
│                   jcchub (Router)                        │
│                                                          │
│  ┌─────────────┐  ┌──────────────┐  ┌────────────────┐ │
│  │ 消息路由层   │  │ 模型适配层    │  │ 经验提取层     │ │
│  │ (conversation│  │ (Provider字典 │  │ (Harness²继承  │ │
│  │  + 订阅直通) │  │  + 热重载)    │  │  操作)         │ │
│  └─────────────┘  └──────────────┘  └────────────────┘ │
│                                                          │
│  ┌─────────────┐  ┌──────────────┐  ┌────────────────┐ │
│  │ 持久化层     │  │ Watchdog     │  │ 消息确认层     │ │
│  │ (JSONL/SQLite)│ │ (定时巡检)   │  │ (游标恢复)     │ │
│  └─────────────┘  └──────────────┘  └────────────────┘ │
│                                                          │
│  ┌─────────────────────────────────────────────────────┐│
│  │              LLM 代理网关                            ││
│  │  /v1/chat/completions → Provider 转发               ││
│  └─────────────────────────────────────────────────────┘│
└──────────────────────────────────────────────────────────┘
             │
    ┌────────┴────────┐
    │                 │
┌───▼────┐    ┌──────▼──────┐
│DeepSeek│    │   OpenAI    │
│  API   │    │    API      │
└────────┘    └─────────────┘
```

### 3.2 核心概念

| 概念 | 定义 | 对应 DSH/Harness² |
|---|---|---|
| **Router** | jcchub，常驻中央路由，承接所有入口交互，维护全局上下文，分派任务 | DSH Router |
| **Worker** | codex/ClaudeCode/jcc，上下文隔离的执行者，专注单一任务 | DSH Worker |
| **Conversation** | 群聊会话，用 conversation_id 标识，所有消息属于某个会话 | DSH Thread |
| **任务通讯管道** | conversation_id 绑定 + 订阅直通机制，Worker 接手任务后订阅相关 Thread | DSH 订阅直通 |
| **继承操作** | 从任务执行中提取经验，更新 harness 配置，跨任务继承，适配不同模型 | Harness² Harnessing级递归 |
| **存算分离** | 会话状态单独持久化，计算（LLM 调用）无状态，重启可恢复 | DSH 存算分离 |
| **Watchdog** | 独立巡检进程，判断各 Worker 有无实质进展、是否陷入无效循环 | DSH Watchdog |

### 3.3 与现有基础设施的对应

| 现有能力 | 文件位置 | 在新架构中的角色 |
|---|---|---|
| AgentHubServer (:3457) | `modules/services/hub/server/AgentHubServer.cs` | Router 主体 |
| AgentRegistry（邮箱+路由） | `modules/services/hub/server/AgentRegistry.cs` | 消息路由层 |
| `/agent/message` | AgentHubServer 路由 | 群聊消息发送 |
| `/agent/messages/{id}` SSE | AgentHubServer 路由 | 消息推送（需加游标） |
| `/v1/chat/completions` | AgentHubServer 路由 | LLM 代理网关（继承操作的基础） |
| Provider 字典 + settings.json 热重载 | `modules/services/hub/app/Program.cs` | 模型适配层 |
| HubTransportConfig | `modules/llm/agents.fork/HubTransportConfig.cs` | Worker 连接 Router 的配置 |
| TranscriptService (JSONL) | `modules/storage/vault/state/transcript/core/TranscriptService.cs` | 持久化层（需扩展到 Hub 侧） |
| TeammateMailboxService | `modules/llm/agents.team/TeammateMailboxService.cs` | 文件邮箱（参考实现） |
| MeshTransport/BusTransport | `modules/async_primitives/async_lock/transport/` | 本地传输拓扑 |
| MCP 协议 (2025-11-25) | `modules/mcp/mcp.protocol/` | 能力声明层（工具发现） |

## 四、接口设计

### 4.1 群聊会话管理

#### 创建会话

```
POST /conversation/create
```

请求 DTO：
```json
{
  "title": "string?",
  "creator_agent_id": "string",
  "participant_agent_ids": ["string"]
}
```

响应 DTO：
```json
{
  "conversation_id": "string",
  "title": "string?",
  "created_at": "2026-11-12T00:00:00Z",
  "participants": ["string"]
}
```

#### 加入/离开会话

```
POST /conversation/{conversation_id}/join
POST /conversation/{conversation_id}/leave
```

请求 DTO：
```json
{
  "agent_id": "string"
}
```

#### 列出会话

```
GET /conversations?agent_id={agent_id}
```

响应 DTO：
```json
{
  "conversations": [
    {
      "conversation_id": "string",
      "title": "string?",
      "participant_count": 3,
      "last_message_at": "2026-11-12T00:00:00Z"
    }
  ]
}
```

### 4.2 群聊消息

#### 发送消息（扩展现有 `/agent/message`）

```
POST /conversation/{conversation_id}/message
```

请求 DTO：
```json
{
  "from_agent_id": "string",
  "message_type": "text",
  "content": "string",
  "reply_to_message_id": "string?",
  "timestamp": "2026-11-12T00:00:00Z"
}
```

响应 DTO：
```json
{
  "message_id": "string",
  "accepted": true,
  "delivered_to": ["string"]
}
```

#### 拉取消息（断线恢复）

```
GET /conversation/{conversation_id}/messages?cursor={cursor}&limit=50
```

响应 DTO：
```json
{
  "messages": [
    {
      "message_id": "string",
      "from_agent_id": "string",
      "message_type": "text",
      "content": "string",
      "timestamp": "2026-11-12T00:00:00Z",
      "reply_to_message_id": "string?"
    }
  ],
  "next_cursor": "string?",
  "has_more": false
}
```

#### SSE 消息推送（扩展现有 `/agent/messages/{id}`）

```
GET /conversation/{conversation_id}/stream?cursor={cursor}
```

SSE 事件格式：
```
event: message
data: {"message_id":"...","from_agent_id":"...","content":"...","timestamp":"..."}

event: cursor
data: {"cursor":"...","message_id":"..."}
```

**游标机制**：每条消息携带递增的 cursor（单调递增序号），客户端断线重连时传入最后收到的 cursor，Hub 从该位置继续推送。

### 4.3 订阅直通

#### Worker 订阅 Thread

```
POST /conversation/{conversation_id}/subscribe
```

请求 DTO：
```json
{
  "agent_id": "string",
  "filter": {
    "message_types": ["text"],
    "from_agents": ["string"],
    "mention_only": false
  }
}
```

订阅后，该 Worker 直接通过 SSE 接收该会话的后续消息，不需要 Router 中转。

#### 取消订阅

```
POST /conversation/{conversation_id}/unsubscribe
```

请求 DTO：
```json
{
  "agent_id": "string"
}
```

### 4.4 模型适配层（继承操作）

#### 当前能力（已有）

Hub 的 `/v1/chat/completions` + Provider 字典 + settings.json 热重载已实现模型适配：

- 请求中携带 `model` 字段 → Hub 在 Provider 字典中匹配 → 转发到上游 Provider
- settings.json 热重载：新增/修改 Provider 配置后自动生效
- 支持 OpenAI 兼容协议

#### 扩展：Harness² 继承操作

新增接口：

```
POST /harness/experience
```

请求 DTO：
```json
{
  "conversation_id": "string",
  "agent_id": "string",
  "task_summary": "string",
  "successful_strategies": ["string"],
  "failed_approaches": ["string"],
  "model_used": "string",
  "harness_config_snapshot": {
    "system_prompt_hash": "string",
    "tools_enabled": ["string"],
    "memory_entries": ["string"]
  }
}
```

```
GET /harness/experience?agent_id={agent_id}&limit=10
```

响应 DTO：
```json
{
  "experiences": [
    {
      "experience_id": "string",
      "task_summary": "string",
      "successful_strategies": ["string"],
      "failed_approaches": ["string"],
      "model_used": "string",
      "created_at": "2026-11-12T00:00:00Z",
      "applicable_to": ["string"]
    }
  ]
}
```

**继承机制**：Worker 接手新任务时，从 Hub 拉取相关经验，注入到自己的系统提示/工具配置中，实现跨任务经验继承。

## 五、数据流

### 5.1 群聊消息流（正常路径）

```
1. 用户在 jcc 窗口输入消息
2. jcc (Worker) → POST /conversation/{id}/message → Hub (Router)
3. Hub 持久化消息到 JSONL/SQLite
4. Hub 广播消息到所有订阅该会话的 Worker
   ├── codex (Worker) ← SSE 推送
   ├── ClaudeCode (Worker) ← SSE 推送
   └── jcc (Worker) ← SSE 推送（自己也收到）
5. 各 Worker 将消息注入到自己的 LLM 上下文
6. Worker 生成回复 → POST /conversation/{id}/message → Hub
7. Hub 持久化 + 广播（回到步骤 4）
```

### 5.2 断线恢复流

```
1. Worker SSE 连接断开
2. Worker 等待重连（指数退避：1s → 2s → 4s → 8s → 16s → 30s 上限）
3. Worker 重连：GET /conversation/{id}/stream?cursor={last_cursor}
4. Hub 从 cursor 位置开始推送未读消息
5. Worker 收到积压消息，注入到 LLM 上下文
6. 恢复正常 SSE 流
```

### 5.3 订阅直通流

```
1. Router 分派任务给 Worker A
2. Worker A → POST /conversation/{id}/subscribe → Hub
3. 后续该会话的消息直接通过 SSE 推送给 Worker A
4. Router 不再中转这些消息给 Worker A
5. 如果 Worker A 需要退出，POST /conversation/{id}/unsubscribe
```

### 5.4 经验继承流（Harness²）

```
1. Worker 完成任务
2. Worker → POST /harness/experience → Hub（提交经验摘要）
3. Hub 持久化经验到经验库
4. 下一个 Worker 接手新任务
5. Worker → GET /harness/experience → Hub（拉取相关经验）
6. Worker 将经验注入到系统提示/工具配置
7. Worker 用改进后的 harness 执行任务
```

## 六、持久化方案

### 6.1 Hub 侧消息持久化（新增）

**存储格式**：JSONL（与现有 TranscriptService 一致）

**存储路径**：
```
{AppData}/jcc/conversations/
├── {conversation_id}/
│   ├── messages.jsonl          # 所有消息记录
│   ├── meta.json               # 会话元数据（标题、参与者、创建时间）
│   └── subscriptions.json      # 当前订阅状态
```

**messages.jsonl 每行格式**：
```json
{
  "message_id": "uuid",
  "conversation_id": "uuid",
  "from_agent_id": "string",
  "message_type": "text",
  "content": "string",
  "reply_to_message_id": "uuid?",
  "timestamp": "2026-11-12T00:00:00Z",
  "cursor": 42
}
```

**cursor**：单调递增整数，每条消息 +1，用于断线恢复定位。

### 6.2 经验库持久化（新增）

**存储路径**：
```
{AppData}/jcc/harness/
├── experiences/
│   ├── {experience_id}.json    # 单条经验
├── index.json                  # 经验索引（按 agent_id、task_type 分类）
```

### 6.3 现有持久化（保持不变）

- Worker 侧 TranscriptService：各自维护自己的对话历史
- TeammateMailboxService：文件邮箱（跨进程消息传递）
- AgentTranscriptService：子 Agent 对话记录

## 七、网络抖动处理

### 7.1 消息确认机制

每条消息携带 `message_id`，Hub 投递后等待 Worker 确认：

```
Hub → SSE push message → Worker
Worker → POST /conversation/{id}/ack { message_id } → Hub
```

未确认的消息标记为 `pending`，Worker 重连后重新推送。

### 7.2 游标恢复

- 每条消息携带递增 `cursor`
- SSE 连接断开后，Worker 用最后收到的 `cursor` 重连
- Hub 从 `cursor + 1` 开始推送
- 游标持久化在 Hub 侧（JSONL 中每条消息都有 cursor 字段）

### 7.3 重连策略

指数退避：1s → 2s → 4s → 8s → 16s → 30s（上限）

SSE 三次连接失败后自动切换 TCP 降级（现有能力）。

### 7.4 心跳保活

现有：Agent 心跳 90 秒超时，60 秒扫描间隔。

扩展：SSE 连接每 30 秒发送 heartbeat 事件，Worker 60 秒未收到 heartbeat 则判定连接断开。

## 八、模型适配层

### 8.1 现有能力（已有）

- Hub `/v1/chat/completions` 接收请求 → 从请求体提取 `model` → Provider 字典匹配 → 转发到上游
- settings.json 热重载：新增/修改 Provider 配置后自动生效
- 支持 OpenAI 兼容协议（流式 SSE 透传 + 非流式）

### 8.2 继承操作扩展（Harness² 理念）

**核心思路**：不修改模型本身，通过调整 harness（系统提示/工具/记忆/护栏）来改进 Agent 能力。

**实现方式**：

1. **经验提交**：Worker 完成任务后，向 Hub 提交经验摘要（成功策略、失败方法、使用的模型）
2. **经验拉取**：Worker 接手新任务时，从 Hub 拉取相关经验
3. **经验注入**：Worker 将经验注入到自己的系统提示/工具配置中
4. **跨模型适配**：不同模型的经验可以互相参考（例如 DeepSeek 的成功策略可以给 Claude 参考）

**关键约束**：
- 经验是 harness 级别的（提示/工具/记忆/护栏），不是模型权重级别的
- 经验继承是可选的，Worker 可以选择不拉取经验
- 经验有适用范围标记（`applicable_to`），避免错误继承

## 九、本地 vs 网络部署

### 9.1 本地部署（默认）

- Hub 监听 `localhost:3457`
- Worker 通过 HTTP 连接 `http://localhost:3457`
- SSE 推送通过 localhost 回环
- NamedPipe 可作为本地传输优化（现有 MeshTransport/BusTransport）

### 9.2 网络部署

- Hub 监听 `0.0.0.0:3457`（需显式开启）
- Worker 通过 HTTPS 连接远程 Hub
- 需要 TLS + 认证（API Key 或 OAuth）
- SSE 推送通过 HTTPS 长连接

### 9.3 混合部署

- 本地 Hub + 远程 LLM API（当前默认模式）
- 远程 Hub + 本地 Worker（团队协作场景）
- 多个 Hub 级联（跨组织协作场景，后续扩展）

## 十、实现计划

### 10.1 分阶段实现

| 阶段 | 内容 | 依赖 | 优先级 |
|---|---|---|---|
| P1 | conversation_id 绑定 + 群聊会话管理 | 无 | 高 |
| P2 | Hub 侧消息持久化（JSONL） | P1 | 高 |
| P3 | 游标恢复 + 消息确认 | P2 | 高 |
| P4 | 订阅直通机制 | P1 | 中 |
| P5 | 经验提取/继承（Harness²） | P2 | 中 |
| P6 | Watchdog 巡检 | P2 | 低 |
| P7 | 网络部署（TLS + 认证） | P1-P3 | 低 |

### 10.2 P1 详细：conversation_id 绑定

**改动范围**：

1. `HubDtos.cs`：新增 `ConversationCreateRequest`、`ConversationCreateResponse`、`ConversationMessageRequest` 等 DTO
2. `AgentHubServer.cs`：新增 `/conversation/create`、`/conversation/{id}/message`、`/conversation/{id}/messages` 路由
3. `AgentRegistry.cs`：新增 `ConversationRegistry`（会话注册表，管理 conversation_id → participants 映射）
4. `HubServerJsonContext.cs`：新增 DTO 的 JSON 序列化上下文

**关键接口**：

```csharp
public sealed class ConversationRegistry
{
    private readonly ImmutableHamT<string, ConversationState> _conversations = ImmutableHamT<string, ConversationState>.Empty;
    
    public string CreateConversation(string creatorAgentId, string? title, IReadOnlyList<string> participants);
    public bool JoinConversation(string conversationId, string agentId);
    public bool LeaveConversation(string conversationId, string agentId);
    public IReadOnlyList<string> GetParticipants(string conversationId);
    public IReadOnlyList<ConversationSummary> ListConversations(string agentId);
}

public sealed record ConversationState
{
    public required string ConversationId { get; init; }
    public string? Title { get; init; }
    public required ImmutableHashSet<string> Participants { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? LastMessageAt { get; init; }
}
```

### 10.3 P2 详细：Hub 侧消息持久化

**改动范围**：

1. 新增 `ConversationStore`（消息持久化，JSONL 格式）
2. `AgentHubServer.cs`：消息发送时调用 `ConversationStore.AppendMessage`
3. 消息拉取时调用 `ConversationStore.ReadMessages(cursor)`

**关键接口**：

```csharp
public sealed class ConversationStore
{
    public string AppendMessage(string conversationId, ConversationMessage message);
    public IReadOnlyList<ConversationMessage> ReadMessages(string conversationId, long? cursor, int limit);
    public long GetLatestCursor(string conversationId);
    public ConversationMeta? GetMeta(string conversationId);
}

public sealed record ConversationMessage
{
    public required string MessageId { get; init; }
    public required string ConversationId { get; init; }
    public required string FromAgentId { get; init; }
    public required string MessageType { get; init; }
    public required string Content { get; init; }
    public string? ReplyToMessageId { get; init; }
    public required DateTime Timestamp { get; init; }
    public required long Cursor { get; init; }
}
```

## 十一、验收标准

| 验收项 | 方法 | 状态 |
|---|---|---|
| 创建群聊会话，多 Agent 加入 | 手动 exe 测试（ADR 0080） | ❌ |
| 发送消息，所有参与者收到 | 手动 exe 测试 | ❌ |
| Hub 重启后，消息记录不丢失 | 手动 exe 测试 | ❌ |
| Worker 断线重连后，从游标位置恢复 | 手动 exe 测试 | ❌ |
| 不同模型（DeepSeek/OpenAI）通过 Hub 适配 | 手动 exe 测试 | ✅ 已有 |
| Worker 提交经验，新 Worker 拉取继承 | 手动 exe 测试 | ❌ |

## 十二、决策记录

### 12.1 为什么选择 Hub HTTP API 而非 MCP 作为聊天协议

- MCP 是 JSON-RPC 请求-响应模式，不适合异步消息流
- MCP 没有"订阅"机制（虽然有 `notifications/message`，但那是单向通知）
- Hub 的消息路由 API（邮箱模型 + SSE 推送）更适合群聊场景
- MCP 作为能力声明和工具发现层，与聊天协议正交

### 12.2 为什么选择 JSONL 而非 SQLite 作为持久化格式

- 与现有 TranscriptService 保持一致（JSONL 格式）
- JSONL 追加写入性能好，无锁（Actor 模型串行化）
- SQLite 需要额外依赖，且 NativeAOT 兼容性需验证
- JSONL 人类可读，便于调试和审计
- 后续如果需要复杂查询，可以再引入 SQLite 索引层

### 12.3 为什么选择游标恢复而非消息确认作为主要恢复机制

- 消息确认（ACK）需要额外往返，增加延迟
- 游标恢复更简单：每条消息有递增序号，断线后从最后位置继续
- 游标天然持久化在 JSONL 中（每条消息都有 cursor 字段）
- ACK 作为补充机制：对于关键消息（如任务分派）可以要求 ACK

### 12.4 为什么 Router/Worker 角色不固定

- DSH Super-agent 中 Router 和 Worker 是固定角色
- 本架构中，任何 Agent 都可以是 Worker（加入群聊执行任务）
- Hub（Router）是唯一的固定角色，因为它是消息中枢
- 这简化了架构：不需要角色选举，Hub 就是 Router，其他都是 Worker

<!-- 🤖 Auto Decision: 2026-11-12 -->
<!-- 决策: 选择 Hub HTTP API 作为聊天协议，JSONL 作为持久化格式，游标恢复作为主要恢复机制 -->
<!-- 原因: MCP 不适合异步消息流；JSONL 与现有 TranscriptService 一致且 NativeAOT 友好；游标恢复比 ACK 更简单且天然持久化 -->
<!-- 替代方案: MCP 协议（不适合聊天）/ SQLite（需额外依赖）/ ACK 机制（增加延迟） -->
<!-- 验证: 尚未实现，待 P1-P3 完成后验收 -->
