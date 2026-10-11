# 0111. 统一消息通道 — MailboxHub 升级为四通道路由 + 聊天室统一表达

- 状态：accepted
- 日期：2026-09-16
- 决策者：用户 + AI
- 前置：ADR 0108（MailboxBase 统一基类）、ADR 0110（IPlatformBotAdapter 适配器）、ADR 0109（聊天室 = 团队 + 广播）

## 背景

### 问题1：MailboxHub 只路由 2/4 通道

`MailboxHub` 是消息传递统一入口，但当前只路由 `InProcess` 和 `File` 两种：

| 通道 | MailboxBase 子类 | MailboxHub 路由 | MailboxKind 枚举 |
|------|-----------------|----------------|-----------------|
| 进程内 | `InProcessMailbox` | ✅ | ✅ `InProcess` |
| 文件 | `FileMailbox` | ✅ | ✅ `File` |
| 有名管道 | `NamedPipeMailbox` | ❌ | ❌ 缺失 |
| 网络(QQ/飞书) | `NetworkMailbox` | ❌ | ❌ 缺失 |

`NamedPipeMailbox` 和 `NetworkMailbox` 已实现（ADR 0108/0110），但未接入 `MailboxHub`，调用方无法通过统一入口使用。

### 问题2：MailboxHub 用旧接口 IMailbox，非 MailboxBase

`MailboxHub` 持有 `IMailbox`（旧接口）+ `ITeammateMailboxService`，而非 `MailboxBase<CoordinatorMessage>`（新基类）。`NamedPipeMailbox`/`NetworkMailbox` 不实现 `IMailbox`，无法接入。

### 问题3：聊天室与邮箱割裂

ADR 0109 定义"聊天室 = 团队 + 广播语义"，`TeamManager.BroadcastMessageAsync` 直接调 `_mailboxService.SendAsync`（文件邮箱），不走 `MailboxHub`。聊天室成员若分布在不同通道（部分进程内、部分跨进程管道、部分在 QQ 群），无法统一广播。

### 问题4：TeamManager 直接依赖 ITeammateMailboxService

`TeamManager` 持有 `ITeammateMailboxService?`（文件邮箱），而非 `MailboxHub`。新增通道时需改 `TeamManager`，违反开闭原则。

## 决策

### 决策1：MailboxHub 升级为 MailboxBase 注册表

```
MailboxHub (升级后)
├── _mailboxes: IReadOnlyDictionary<MailboxKind, MailboxBase<CoordinatorMessage>>
│   ├── InProcess  → InProcessMailbox        （必需）
│   ├── File       → FileMailbox             （可选）
│   ├── NamedPipe  → NamedPipeMailbox        （可选）
│   └── Network    → NetworkMailbox          （可选，QQ/飞书）
├── _agentChannels: ConcurrentDictionary<string, MailboxKind>  — agent → 通道偏好
│
├── SendAsync(agentId, message)              — 自动路由（按 agent 注册的通道）
├── SendAsync(agentId, message, kind)        — 显式指定通道
├── BroadcastAsync(message)                  — 跨所有通道广播
├── BroadcastAsync(message, kind)            — 指定通道广播
├── RegisterAgent(agentId, kind, sessionId?) — 注册 agent 到指定通道
└── ReceiveAsync(agentId)                    — 从 agent 所在通道接收
```

**核心变化**：
- `IMailbox` + `ITeammateMailboxService` → `MailboxBase<CoordinatorMessage>`（统一基类）
- 构造函数接收 `IReadOnlyDictionary<MailboxKind, MailboxBase<CoordinatorMessage>>`，DI 注入可用通道
- 缺失通道时 `SendAsync(kind)` 记日志 + 返回 false，不抛异常（优雅降级）

### 决策2：MailboxKind 枚举扩展

```csharp
public enum MailboxKind
{
    [EnumValue("in_process")]  InProcess,   // 已有
    [EnumValue("file")]        File,        // 已有
    [EnumValue("named_pipe")]  NamedPipe,   // 新增 — 跨进程管道
    [EnumValue("network")]     Network,     // 新增 — QQ/飞书/Discord
}
```

### 决策3：TeamManager 改用 MailboxHub

```
TeamManager
  旧: _mailboxService: ITeammateMailboxService?  — 直接依赖文件邮箱
  新: _mailboxHub: MailboxHub?                   — 依赖统一入口
```

`BroadcastMessageAsync` 改调 `_mailboxHub.BroadcastAsync(message)`，自动跨所有通道广播。`GetChatRoomInfoAsync` 不变（仍返回 `ChatRoomInfo` 视图）。

### 决策4：聊天室 = MailboxHub.BroadcastAsync 的语义投影

聊天室不新建类（遵循 ADR 0109），其语义由 `MailboxHub.BroadcastAsync` 统一表达：
- 聊天室消息 = `CoordinatorMessage { MessageType="chat", ... }`
- 广播到所有通道 = 聊天室全体成员收到消息
- 成员分布在不同通道（进程内/管道/QQ/飞书）→ `MailboxHub` 逐通道路由，调用方无感

### 决策5：自动路由 — agent 注册时绑定通道

`RegisterAgent(agentId, kind, sessionId?)` 将 agent 绑定到通道。`SendAsync(agentId, message)` 查 `_agentChannels[agentId]` 获取通道，路由到对应 `MailboxBase`。未注册的 agent 默认走 `InProcess`。

## 替代方案

### 方案A：URI 地址路由（否决）

用 URI 地址（`local://agentId`、`pipe://pid/agentId`、`qq://groupId/userId`）统一路由，`SendAsync(address, message)` 解析 URI 分发。

**否决理由**：
- 字符串解析运行时错误，无编译期安全
- agentId 已是唯一标识，URI scheme 冗余
- 与现有 `MailboxKind` 枚举体系不统一，引入第二套路由机制
- 过度设计 — 当前 4 通道用枚举足够，URI 适合 10+ 通道场景

### 方案B：所有 MailboxBase 实现 IMailbox（否决）

让 `NamedPipeMailbox`/`NetworkMailbox` 也实现 `IMailbox`，`MailboxHub` 继续用 `IMailbox`。

**否决理由**：
- `IMailbox` 是旧接口（`Task<bool>` 返回），`MailboxBase` 用 `ValueTask`（更现代）
- 向后兼容旧接口违反"无后向兼容"原则（AGENTS.md 基础规范1）
- `MailboxBase` 已是更强大的统一基类，`IMailbox` 应被取代而非迁就

### 方案C：新建 UnifiedMessagingService 不改 MailboxHub（否决）

新建 `UnifiedMessagingService` 持有 4 个 `MailboxBase`，`MailboxHub` 保留不动。

**否决理由**：
- 两套统一入口（`MailboxHub` + `UnifiedMessagingService`），职责重叠
- 违反 DRY 和"消除两套实现"原则
- `MailboxHub` 已有 `[Register(Singleton)]` DI 注册和测试，应升级而非新建

## 后果

- **正面**：
  - 四通道统一入口，调用方只需 `MailboxHub.SendAsync` / `BroadcastAsync`
  - 新增通道 = 加 `MailboxKind` 枚举值 + 注册 `MailboxBase` 实例，不改 `MailboxHub` 核心逻辑
  - `TeamManager` 解耦具体邮箱，只依赖 `MailboxHub`
  - 聊天室广播自动覆盖所有通道，成员可跨通道分布
  - `IMailbox` 旧接口可逐步废弃（`InProcessMailbox`/`FileMailbox` 的 `IMailbox` 实现保留但不再被 `MailboxHub` 使用）

- **负面**：
  - `MailboxHub` 构造函数签名变更，现有 DI 注册需更新
  - `MailboxHubTests` 需重写（从 mock `IMailbox` 改为 mock `MailboxBase`）
  - `TeamManager` 构造函数变更（`ITeammateMailboxService?` → `MailboxHub?`）

- **中性**：
  - `IMailbox` 接口暂保留（其他消费方可能直接用），后续逐步废弃
  - 自动路由默认 `InProcess`，显式 `kind` 参数覆盖

## 实现路线（开心路径优先）

1. 扩展 `MailboxKind` 枚举（+`NamedPipe` +`Network`）
2. 升级 `MailboxHub`：`Dictionary<MailboxKind, MailboxBase>` + 自动路由 + 跨通道广播
3. 改 `TeamManager`：`_mailboxService` → `_mailboxHub`
4. DI 注册：`MailboxHub` 注入可用通道字典
5. 重写 `MailboxHubTests`
6. E2E：跨通道广播（InProcess + NamedPipe 混合成员）

## 验证标准

1. `MailboxHub.SendAsync(agentId, msg, MailboxKind.NamedPipe)` → 路由到 `NamedPipeMailbox`
2. `MailboxHub.SendAsync(agentId, msg, MailboxKind.Network)` → 路由到 `NetworkMailbox`（QQ/飞书）
3. `MailboxHub.BroadcastAsync(msg)` → 所有已注册通道都收到
4. `TeamManager.BroadcastMessageAsync` → 通过 `MailboxHub` 广播，覆盖所有通道
5. `MailboxHub.SendAsync(agentId, msg)` → 自动路由到 agent 注册的通道
6. 缺失通道 → 计日志 + 返回 false，不抛异常

---

## 聊天室消息去重 + 系统消息可见性（2026-09-16 追加）

### 问题5：TeamManager 层无去重，跨通道广播重复

`MailboxHub.BroadcastAsync` 逐通道广播（InProcess + File + NamedPipe + Network），各通道邮箱内部已用 `CoordinatorMessage.MessageId` 去重（ADR 0107/0108），但 `TeamManager._teamMessages` 是 `List<TeamMessage>`，同一消息广播多次会重复入列表，导致：
- 聊天室历史消息重复显示
- 持久化到文件邮箱的 `MailboxSendRequest` 重复（虽然 `FileMailbox` 去重，但 `TeamManager` 层已重复）

### 问题6：系统通知无可见性控制

`TeamManager.BroadcastMessageAsync` 广播所有消息给所有成员，无区分：
- "xxx 加入聊天室" → 应所有人可见（系统通知样式）
- "xxx 被禁言" → 应仅管理员可见（操作日志）
- "主机切换" → 应系统通知样式，但当前混在普通消息里

### 决策6：TeamManager 层去重 — ConcurrentDictionary 替代 List

`TeamManager._teamMessages` 从 `ConcurrentDictionary<string, List<TeamMessage>>` 改为 `ConcurrentDictionary<string, ConcurrentDictionary<string, TeamMessage>>`（外层 teamId → 内层 messageId → message）。

**双层去重**：
- 第一层：`TeamManager` 用 `MessageId` 去重（防止重复入历史列表）
- 第二层：各通道邮箱用 `CoordinatorMessage.MessageId` 去重（防止重复投递到 Agent Channel）

两层独立去重，互不依赖，任一层失效另一层兜底。

### 决策7：MailboxHub 按可见性路由

`MailboxHub` 新增 `SendAsync`/`BroadcastAsync` 重载，接受 `MessageVisibility` 参数：

```csharp
public ValueTask<bool> SendAsync(string agentId, CoordinatorMessage message, MessageVisibility visibility, CancellationToken ct = default);
public ValueTask BroadcastAsync(CoordinatorMessage message, MessageVisibility visibility, CancellationToken ct = default);
```

**路由规则**：
- `Public`/`System` → 广播所有已注册通道
- `AdminOnly` → 仅投递给 `Role >= Admin` 的 agent（`MailboxHub` 查 `_agentRoles` 表）
- `Private` → 仅投递给 `ToAgentId`（单通道路由，不广播）
- `Hidden` → 不投递（仅 `TeamManager` 持久化）

`MailboxHub` 新增 `_agentRoles: ConcurrentDictionary<string, ChatRoomRole>`（agentId → 角色），`RegisterAgentAsync` 时绑定角色。

### 决策8：CoordinatorMessage 补 Visibility 字段

```csharp
public sealed class CoordinatorMessage
{
    // ... 现有字段
    public MessageVisibility Visibility { get; init; } = MessageVisibility.Public;
    public string? ToAgentId { get; init; }  // 已有，Private 时使用
}
```

跨进程传输时 `Visibility` 随消息序列化，接收方按可见性过滤投递。

### 实现路线（开心路径优先）

1. `CoordinatorMessage` 补 `Visibility` 字段
2. `MailboxHub` 补 `_agentRoles` + 按可见性路由重载
3. `TeamManager._teamMessages` 改 `ConcurrentDictionary<string, ConcurrentDictionary<string, TeamMessage>>`
4. `TeamManager.BroadcastMessageAsync` 按可见性调 `MailboxHub` 重载
5. 单元测试 + E2E 测试

### 验证标准（追加）

7. 同一 `MessageId` 跨通道广播两次 → `TeamManager._teamMessages` 只有一条 + 各通道邮箱只投递一次
8. `Visibility=AdminOnly` → 仅管理员 agent 收到
9. `Visibility=Private` → 仅 `ToAgentId` 收到
10. `Visibility=Hidden` → 无人收到，但 `TeamManager._teamMessages` 有记录
11. `Visibility=System` → 所有人收到，前端可按 `MessageType="system_notice"` 区分样式
