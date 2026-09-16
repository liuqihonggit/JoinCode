# 聊天室改造任务清单 — 模仿 QQ

> 📍 **导航**: [docs/task/](README.md) › 本文件
> 🔗 **上游 ADR**: [0109](../adr/0109-window-shake-notification-and-bot-name.md)（聊天室 = 团队 + 广播 + 系统消息可见性）、[0111](../adr/0111-unified-messaging-channel-mailboxhub-upgrade.md)（MailboxHub 按可见性路由 + 跨通道去重）
> 📅 **创建**: 2026-09-16
> 🎯 **目标**: 修复聊天室验收三大缺陷（无 ID / 无去重 / 无系统通知可见性），对标 QQ 聊天室

## 缺陷清单

| # | 缺陷 | 现状 | QQ 对标 | 修复 ADR |
|---|------|------|---------|---------|
| 1 | 无聊天室 ID | `ChatRoomInfo` 只有 `RoomName` | QQ 群有唯一群号 | ADR 0109 决策7 |
| 2 | 无消息去重 | `_teamMessages` 是 `List`，重复广播重复入列表 | 同一消息 ID 只显示一次 | ADR 0109 决策10 + ADR 0111 决策6 |
| 3 | 无系统通知可见性 | 系统通知广播给所有人 | "xxx 被禁言"仅管理员可见 | ADR 0109 决策8/9 + ADR 0111 决策7 |

## 任务分解（TDD 循环：🔴红 → 🟢绿 → 🔵重构）

> ✅ 阶段1-4 全部完成（2026-09-16），43 个测试通过

### 阶段1：模型层（Foundation）✅

#### 任务1.1：ChatRoomInfo 补 ChatRoomId + ChatRoomMember ✅
- **文件**: `lib/abstractions/abs_core/models/models_agent/agent/ChatRoomInfo.cs`
- **改动**:
  - 新增 `ChatRoomId`（required string，等同 TeamId）
  - `Members` 类型从 `IReadOnlyList<string>` 改为 `IReadOnlyList<ChatRoomMember>`
  - 新增 `MyRole`/`OnlineCount`/`LastMessageAt`
- **新增枚举**: `ChatRoomRole`（Owner/Admin/Member）、`ChatRoomMemberStatus`（Online/Offline/Muted）
- **新增 record**: `ChatRoomMember`（AgentId/DisplayName/Role/Status/JoinedAt）
- **测试**: `ChatRoomInfoTests` — 构造+序列化+AOT 兼容
- **状态**: ✅ 完成

#### 任务1.2：TeamMessage 补可见性 + 撤回 + @提及字段 ✅
- **文件**: `lib/abstractions/abs_core/models/models_agent/agent/TeamModels.cs`
- **改动**: `TeamMessage` 新增 `Visibility`/`ToAgentId`/`RevokeReason`/`Mentions`
- **新增枚举**: `MessageVisibility`（Public/System/AdminOnly/Private/Hidden）
- **测试**: `TeamMessageTests` — 默认 Visibility=Public + 私信字段 + 撤回字段
- **状态**: ✅ 完成

#### 任务1.3：SystemNoticeFactory + SystemNoticeKind ✅
- **文件**: `lib/abstractions/abs_core/models/models_agent/agent/SystemNoticeFactory.cs`（新建）
- **改动**: 系统通知工厂方法 + `SystemNoticeKind` 枚举（MemberJoined/MemberLeft/MemberMuted/...）
- **测试**: `SystemNoticeFactoryTests` — 16 个测试，每种 kind 的 content + visibility 正确
- **状态**: ✅ 完成

#### 任务1.4：CoordinatorMessage 补 Visibility 字段 ✅
- **文件**: `lib/abstractions/abs_core/models/models_agent/agent/CoordinatorMessage.cs`
- **改动**: 新增 `Visibility`（默认 Public）
- **测试**: 现有 `CoordinatorMessage` 测试 + 新增 Visibility 默认值测试
- **状态**: ✅ 完成

### 阶段2：TeamManager 改造（Services）✅

#### 任务2.1：_teamMessages 改 ConcurrentDictionary 去重 ✅
- **文件**: `llm/agents/Coordinator/Team/core/TeamManager.cs`
- **改动**:
  - `_teamMessages` 从 `ConcurrentDictionary<string, List<TeamMessage>>` 改为 `ConcurrentDictionary<string, ConcurrentDictionary<string, TeamMessage>>`
  - `BroadcastMessageAsync`/`SendMessageAsync`/`SendMessageToAgentAsync` 入列表前用 `MessageId` 去重
  - `GetTeamMessagesAsync` 等读取方法适配新结构
- **测试**: `TeamManagerTests` — 同一 MessageId 广播两次只入一条
- **状态**: ✅ 完成

#### 任务2.2：BroadcastMessageAsync 按可见性过滤投递 ✅
- **文件**: `llm/agents/Coordinator/Team/core/TeamManager.cs`
- **改动**:
  - `PersistTeamMessageToMailboxAsync` 按 `Visibility` 过滤投递目标
  - `Public`/`System` → 广播所有成员
  - `AdminOnly` → 仅 `Role >= Admin`
  - `Private` → 仅 `ToAgentId`
  - `Hidden` → 不投递
- **测试**: AdminOnly 仅管理员收到 + Private 仅目标收到 + Hidden 无人收到
- **状态**: ✅ 完成

#### 任务2.3：RevokeMessageAsync 消息撤回 ✅
- **文件**: `llm/agents/Coordinator/Team/core/TeamManager.cs`
- **改动**: 新增 `RevokeMessageAsync(teamId, messageId, revokerId, reason?)`
  - 校验：消息存在 + 发送者是 revoker 或 revoker 是管理员 + 2 分钟内
  - 标记：`Visibility=Hidden` + `RevokeReason=reason`
  - 广播：`SystemNoticeFactory.Create(MessageRevoked, ...)`
- **测试**: 撤回成功 + 超时失败 + 权限失败
- **状态**: ✅ 完成

#### 任务2.4：GetChatRoomInfo 升级返回新 ChatRoomInfo ✅
- **文件**: `llm/agents/Coordinator/Team/core/TeamManager.cs`
- **改动**: `GetChatRoomInfo` 填充 `ChatRoomId`/`MyRole`/`OnlineCount`/`LastMessageAt` + `Members` 改为 `ChatRoomMember` 列表
- **测试**: 返回的 ChatRoomInfo 字段完整
- **状态**: ✅ 完成

### 阶段3：MailboxHub 按可见性路由（Composition）✅

#### 任务3.1：MailboxHub 补 _agentRoles + 按可见性路由重载 ✅
- **文件**: `llm/agents/Coordinator/Core/Messaging/MailboxHub.cs`
- **改动**:
  - 新增 `_agentRoles: ConcurrentDictionary<string, ChatRoomRole>`
  - `RegisterAgentAsync` 补 `role` 参数
  - 新增 `SendAsync`/`BroadcastAsync` 重载接受 `MessageVisibility`
  - `AdminOnly` → 查 `_agentRoles` 过滤
  - `Private` → 单通道路由到 `ToAgentId`
  - `Hidden` → 不投递
- **测试**: `MailboxHubTests` — 27 个测试，AdminOnly 仅管理员 + Private 仅目标 + Hidden 无人收到
- **状态**: ✅ 完成

#### 任务3.2：TeamManager.BroadcastMessageAsync 调 MailboxHub 可见性重载 ✅
- **文件**: `llm/agents/Coordinator/Team/core/TeamManager.cs`
- **改动**: `PersistTeamMessageToMailboxAsync` 调 `_mailboxHub.BroadcastAsync(msg, visibility, ct)`
- **测试**: 跨通道按可见性投递
- **状态**: ✅ 完成

### 阶段4：集成测试 ✅

#### 任务4.1：集成测试 — 可见性投递过滤 ✅
- **测试**: `TeamManagerChatRoomTests`
  - AdminOnly 系统通知仅投递给管理员（不投递给普通成员）
  - Private 私信仅投递给 ToAgentId
  - Hidden 消息不投递给任何人
- **状态**: ✅ 完成（12 个测试通过）

#### 任务4.2：E2E — 跨通道可见性 ✅
- **场景**:
  1. InProcess + NamedPipe 混合成员
  2. AdminOnly 消息 → 跨通道仅管理员收到
  3. Private 消息 → 跨通道仅目标收到
- **测试**: `CrossChannelVisibilityE2ETests` — 5 个 E2E 测试（AdminOnly/Private/Hidden/Public/System）
- **状态**: ✅ 完成（2026-09-17）

### 阶段5：ChatRoomState 合并 + InProcessMailbox 修复 ✅

#### 任务5.1：ChatRoomState 属性 init→set ✅
- **文件**: `lib/abstractions/abs_core/models/models_agent/agent/ChatRoomState.cs`
- **改动**: 5 个属性从 init 改为 set，使 TeamManager 可赋值
- **状态**: ✅ 完成（commit 16630ed43）

#### 任务5.2：TeamManager 6 字典迁移到 ChatRoomState ✅
- **文件**: `llm/agents/Coordinator/Team/core/TeamManager.cs` + `TeamManager.Persistence.cs`
- **改动**: 6 个字典合并为 `ConcurrentDictionary<string, ChatRoomState> _rooms`
- **测试**: 88 个 TeamManager 测试通过
- **状态**: ✅ 完成（commit b392f0524）

#### 任务5.3：FileChatRoomStore 实现 IChatRoomStore ✅
- **文件**: `llm/agents/Coordinator/Team/core/FileChatRoomStore.cs`（新建）+ `ChatRoomStateData.cs`（新建）
- **改动**: 按需加载/保存到 `~/.jcc/teams/rooms/{teamId}.json`
- **测试**: `FileChatRoomStoreTests` — 7 个测试通过
- **状态**: ✅ 完成（commit 71593a0a8）

#### 任务5.4：InProcessMailbox 双重去重 bug 修复 ✅
- **文件**: `llm/agents/Coordinator/Core/Messaging/InProcessMailbox.cs`
- **根因**: SendAsync→IsDuplicate(TryAdd 标记)→TellAsync→HandleSendAsync→IsDuplicate 二次检查→跳过投递
- **修复**: SendAsync 保留 IsDuplicate 但直接 DeliverToAgent 投递，绕过 HandleSendAsync
- **测试**: 594 个全量测试通过，含 5 个 E2E 可见性测试
- **状态**: ✅ 完成（commit 66a5d8d5a）

## 依赖关系

```
任务1.1 (ChatRoomInfo) ─┐
任务1.2 (TeamMessage)  ─┼─→ 任务2.4 (GetChatRoomInfo)
任务1.3 (SystemNotice) ─┘
                         │
任务1.4 (CoordinatorMsg)─┼─→ 任务3.1 (MailboxHub 可见性) ─→ 任务3.2 (TeamManager 调 Hub)
                         │
任务1.2 (TeamMessage)  ──┼─→ 任务2.1 (去重) ─→ 任务2.2 (可见性过滤) ─→ 任务2.3 (撤回)
                         │
                         └─→ 任务4.1 (E2E 完整) + 任务4.2 (E2E 跨通道)
```

## 验收标准

| # | 标准 | 对应缺陷 |
|---|------|---------|
| 1 | `ChatRoomInfo.ChatRoomId` 非空且等同 `TeamId` | 缺陷1 |
| 2 | 同一 `MessageId` 广播两次 → `_teamMessages` 只有一条 | 缺陷2 |
| 3 | `SystemNoticeKind.MemberMuted` → 仅管理员收到 | 缺陷3 |
| 4 | `SystemNoticeKind.MemberJoined` → 所有成员收到 | 缺陷3 |
| 5 | `RevokeMessageAsync` → 消息 `Visibility=Hidden` + 撤回通知广播 | 新增 |
| 6 | `Visibility=Private` → 仅 `ToAgentId` 收到 | 缺陷3 |
| 7 | `Visibility=Hidden` → 无人收到，但 `_teamMessages` 有记录 | 缺陷3 |
| 8 | 跨通道（InProcess+NamedPipe）按可见性正确投递 | 缺陷2+3 |

## QQ 对标特性矩阵

| QQ 特性 | 本次实现 | 后续 |
|---------|---------|------|
| 唯一群号 | ✅ ChatRoomId | — |
| 消息去重 | ✅ MessageId 双层去重 | — |
| 系统通知 | ✅ SystemNoticeFactory + 可见性 | — |
| 消息撤回 | ✅ RevokeMessageAsync（2分钟内） | — |
| @提及 | ✅ Mentions 字段 | ⬜ 强提醒推送 |
| 禁言/踢人 | ✅ MemberMuted/Kicked 系统通知 | ⬜ 实际禁言逻辑 |
| 管理员角色 | ✅ ChatRoomRole（Owner/Admin/Member） | — |
| 消息已读 | ⬜ | 后续（群已读回执） |
| 消息转发 | ⬜ | 后续 |
| 文件/图片 | ⬜ | 后续（MessageType 扩展） |

## 风险

| 风险 | 缓解 |
|------|------|
| `_teamMessages` 结构变更影响持久化 | `TeamManager.Persistence.cs` 适配 + 版本迁移 |
| `MailboxHub` 构造函数变更影响 DI | `RegisterAgentAsync` 补 `role` 参数有默认值 |
| 跨进程 `Visibility` 序列化兼容 | `CoordinatorMessage` 已用 `JsonContext`，新增字段自动包含 |
