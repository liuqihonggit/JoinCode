# 0109. 窗口震动通知与子代理 bot 中文名

- 状态：accepted
- 日期：2026-09-16
- 决策者：用户 + AI
- 前置：ADR 0107（文件邮箱锁 + Actor 邮箱模型）、ADR 0108（统一邮箱基类 + 有名管道邮箱）

## 背景

### 需求

用户场景：用户问"bot小a你在什么电脑的什么进程,可以震动你的窗口给我看看吗?",AI 回答"我在电脑名:xx,进程:PID,我可以震动给你看哦"并实际震动窗口。

拆解为 5 个子问题：

1. **子代理 bot 前缀随机中文名** — 每个子代理需要一个简单中文名,以 `bot` 开头,随机生成
2. **聊天室抽象** — 现有团队模式（`ITeamManager`/`TeamManager`）可抽象为聊天室
3. **MCP 震动工具** — 震动进程窗口（找到 GUI 窗口 + 终端窗口）
4. **1s 去抖防高频** — 一个进程内多个子代理可能同时发出震动,合并为 1s 延迟单次震动
5. **配置开关** — 用户可在配置和界面关闭震动功能,关闭后直接文字回答

### 现有基础设施

| 能力 | 现状 | 位置 |
|------|------|------|
| 子代理命名 | `DisplayName`（可中文）+ `SubagentName`，**无 bot 前缀** | `llm/agents/Services/Spawn/Unified/ContextSetupMiddleware.cs:62` |
| 团队模式 | `ITeamManager`/`TeamManager` 完整，**无"聊天室"概念** | `llm/agents/Coordinator/Team/core/TeamManager.cs:9` |
| 跨进程邮箱 | `NamedPipeMailbox`（星型拓扑+广播）+ `InProcessMailbox` | `llm/agents/Coordinator/Core/Messaging/NamedPipeMailbox.cs:12` |
| 窗口 API | `User32NativeMethods`（21 个，含 `MoveWindow`/`GetWindowRect`/`EnumWindows`），**缺 `FlashWindowEx`** | `kit/hands/desktop/native/User32NativeMethods.cs` |
| 控制台窗口 | `GetConsoleWindow()` 已声明 | `kit/hands/desktop/pulse_overlay/PulseNativeMethods.cs:120` |
| 震动动画 | `PermissionDialog.StartShakeAnimation()` X 轴阻尼，**私有方法** | `app/gui/views/core/PermissionDialog.axaml.cs:70` |
| 配置热重载 | `SettingsJson.CurrentSettings` + `IConfigChangeNotifier` | `lib/guard/configuration/configuration2/core/mapping/SettingsJson.cs:89` |
| 通知服务 | `INotificationService` + `PushNotificationToolHandlers` | `lib/vault/notification/NotificationService.cs:8` |
| MCP 工具注册 | `[McpToolDispatch]`+`[McpTool]`+源码生成器 | `gen/mcp_tool_dispatch.generator/McpToolDispatchGenerator.cs` |

## 决策

### 决策1：改造为主，复用现有基础设施（方案 A）

选择**改造现有代码为主，新建必要组件为辅**，而非新建独立模块。

**改造点（5 个现有文件）：**

| 文件 | 改动 |
|------|------|
| `ContextSetupMiddleware.cs` | `DisplayName` 为空时调用 `BotNameGenerator.Generate()` 生成 `bot{随机中文名}` |
| `PermissionDialog.axaml.cs` | `StartShakeAnimation` 提取为 `ShakeAnimationHelper` 公共静态类（正向重构） |
| `User32NativeMethods.cs` | 添加 `FlashWindowEx` P/Invoke 声明 |
| `SettingsJson.cs` | `CurrentSettings` 添加 `WindowShakeEnabled`（默认 true）+ `ChatRoomEnabled`（默认 true） |
| `TeamManager.cs` | 添加 `ChatRoom` 语义（团队 = 聊天室，广播消息即聊天室消息） |

**新建点（3 个新组件）：**

| 组件 | 位置 | 职责 |
|------|------|------|
| `BotNameGenerator` | `lib/abstractions/abs_core/core_utils/core/` | 随机中文名生成器（bot小明/bot阿虎/bot小薇） |
| `ShakeWindowToolHandlers` | `kit/mcp/communication/` | MCP 工具（`shake_window`/`flash_taskbar`） |
| `WindowShakeCoordinator` | `kit/hands/desktop/services/` | 1s 去抖协调器（进程内多子代理合并震动） |

### 决策2：子代理命名 — bot 前缀 + 随机中文名

- 格式：`bot{中文名}`，如 `bot小明`、`bot阿虎`、`bot小薇`
- 中文名池：30+ 个常见中文小名（小明/小红/小虎/小薇/阿杰/阿宝/小雪/小雷/小风/小云...）
- 随机选择 + 进程内去重（同进程不重名）
- 仅当 `DisplayName` 为空时生成，用户指定名称时保留用户名称
- 生成后写入 `SubAgentOptions.DisplayName`，不修改 `SubagentName`（内部标识保持不变）

### 决策3：聊天室 = 团队 + 广播语义

- 不新建 `ChatRoom` 类，直接复用 `TeamManager`
- 聊天室消息 = `TeamMessage`（已有 `Broadcast` 命令）
- 震动消息通过邮箱广播：`CoordinatorMessage { MessageType="shake", Content="warning" }`
- `TeamManager` 添加 `GetChatRoomInfoAsync()` 便捷方法（返回成员列表+最近消息）

### 决策4：震动实现 — 双路径（GUI + CLI）

| 路径 | 实现 | 触发方式 |
|------|------|----------|
| GUI（Avalonia） | `ShakeAnimationHelper.Shake(window)` X 轴阻尼动画 | 提取自 `PermissionDialog.StartShakeAnimation` |
| CLI（终端） | `MoveWindow` + `GetWindowRect` 移动控制台窗口 | `GetConsoleWindow()` 获取句柄 |
| 任务栏闪烁 | `FlashWindowEx` P/Invoke | 新增 API 声明 |

### 决策5：1s 去抖协调器

- `WindowShakeCoordinator`：进程内单例
- 收到震动请求 → 记录时间戳 → 若距上次震动 < 1s 则忽略 → 否则执行震动
- 用 `volatile DateTime _lastShakeTime` + `Interlocked.CompareExchange` 无锁实现
- 多个子代理同时请求 → 只有第一个触发，后续 1s 内的请求被合并

### 决策6：配置开关 + 优雅降级

- `WindowShakeEnabled`（默认 true）：关闭后 MCP 工具直接返回文字"震动已关闭"
- `ChatRoomEnabled`（默认 true）：关闭后不广播震动消息
- 热重载：通过 `IConfigChangeNotifier` 自动生效，无需重启
- GUI 界面：在设置页添加开关（文件驱动界面，ADR 0005）

## 替代方案

### 方案 B：新建独立模块（放弃）

新建 `kit/hands/notification/` 独立模块，不依赖现有 `PermissionDialog`/`User32NativeMethods`/`SettingsJson`。

**放弃原因：**
1. 重复造轮子 — 窗口 API、动画、配置热重载均已存在
2. 违反"八荣八耻"（以复用现有为荣）和"万物皆 node，万物皆插件"
3. 架构分裂 — 两套窗口操作代码、两套动画实现、两套配置读取
4. 改动反而更大 — 需重新实现 21 个 P/Invoke + 动画 + 配置绑定

## 验证标准

1. 子代理 spawn 时 `DisplayName` 为空 → 自动生成 `bot{中文名}`
2. MCP 工具 `shake_window` 调用 → 窗口实际震动（GUI 路径 + CLI 路径）
3. 1s 内多次调用 `shake_window` → 只震动一次
4. `WindowShakeEnabled=false` → 工具返回文字，不震动
5. 跨进程子代理调用 `shake_window` → 主进程窗口震动（通过邮箱广播）

---

## 聊天室改造 — 模仿 QQ（2026-09-16 追加）

### 改造动机：验收不及格的三大缺陷

原决策3"聊天室 = 团队 + 广播语义"落地后，用户验收发现三大缺陷：

| 缺陷 | 现状 | QQ 对标 |
|------|------|---------|
| ❌ 无聊天室 ID | `ChatRoomInfo` 只有 `RoomName`，无 `ChatRoomId` | QQ 群有唯一群号 |
| ❌ 无消息去重 | `TeamManager._teamMessages` 是 `List<TeamMessage>`，重复广播重复入列表 | 同一消息 ID 只显示一次 |
| ❌ 无系统通知可见性 | 无 `SystemMessage` 概念，系统通知广播给所有人 | "xxx 加入群聊"对所有人可见，"xxx 被禁言"仅管理员可见 |

### 决策7：ChatRoomInfo 补 ChatRoomId + 元数据

```csharp
public sealed record ChatRoomInfo
{
    public required string ChatRoomId { get; init; }      // 新增 — 唯一聊天室 ID（等同 TeamId）
    public required string RoomName { get; init; }
    public required IReadOnlyList<ChatRoomMember> Members { get; init; }  // 升级 — 含角色/状态
    public ChatRoomRole? MyRole { get; init; }             // 新增 — 当前查询者的角色
    public int OnlineCount { get; init; }                  // 新增 — 在线成员数
    public int MemberCount => Members.Count;
    public DateTime? LastMessageAt { get; init; }          // 新增 — 最后消息时间
}

public sealed record ChatRoomMember
{
    public required string AgentId { get; init; }
    public required string DisplayName { get; init; }
    public ChatRoomRole Role { get; init; } = ChatRoomRole.Member;
    public ChatRoomMemberStatus Status { get; init; } = ChatRoomMemberStatus.Online;
    public DateTime JoinedAt { get; init; }
}

public enum ChatRoomRole { Owner, Admin, Member }
public enum ChatRoomMemberStatus { Online, Offline, Muted }
```

**`ChatRoomId` 等同 `TeamId`**：不新建 ID 体系，复用 `TeamInfo.TeamId`，`ChatRoomInfo.ChatRoomId = team.TeamId`。

### 决策8：消息可见性 — MessageVisibility 枚举

```csharp
public enum MessageVisibility
{
    Public,      // 所有人可见（普通聊天消息）
    System,      // 系统通知（"xxx 加入"/"xxx 退出"），所有人可见但样式区分
    AdminOnly,   // 仅管理员/群主可见（"xxx 被禁言"/"xxx 踢出"操作日志）
    Private,     // 私信（仅发送者+接收者可见）
    Hidden,      // 隐藏（撤回的消息、被过滤的消息）
}
```

`TeamMessage` 新增字段：
```csharp
public sealed record TeamMessage
{
    public required string MessageId { get; init; }
    public required string TeamId { get; init; }
    public required string SenderId { get; init; }
    public required string Content { get; init; }
    public string MessageType { get; init; } = "text";
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public bool IsRead { get; set; }
    // 新增字段
    public MessageVisibility Visibility { get; init; } = MessageVisibility.Public;
    public string? ToAgentId { get; init; }          // 私信目标（Visibility=Private 时）
    public string? RevokeReason { get; init; }       // 撤回原因（Visibility=Hidden 时）
    public IReadOnlyList<string>? Mentions { get; init; }  // @提及的 AgentId
}
```

### 决策9：系统通知消息 — SystemNoticeKind 枚举 + 工厂方法

模仿 QQ 系统消息，定义系统通知类型 + 工厂方法（集中构造，禁止散落）：

```csharp
public enum SystemNoticeKind
{
    MemberJoined,        // "xxx 加入聊天室" — Public
    MemberLeft,          // "xxx 退出聊天室" — Public
    MemberMuted,         // "xxx 被禁言" — AdminOnly
    MemberUnmuted,       // "xxx 被解除禁言" — AdminOnly
    MemberKicked,        // "xxx 被踢出" — AdminOnly
    RolePromoted,        // "xxx 被设为管理员" — AdminOnly
    RoleDemoted,         // "xxx 被取消管理员" — AdminOnly
    HostChanged,         // "主机切换：xxx → yyy" — System（跨进程选举通知）
    BuildQueueBusy,      // "全局编译队列繁忙，排队中" — System
    MessageRevoked,      // "xxx 撤回了一条消息" — Public
}

public static class SystemNoticeFactory
{
    public static TeamMessage Create(SystemNoticeKind kind, string teamId, string actorAgentId, string? extra = null)
    {
        var (content, visibility) = kind switch
        {
            SystemNoticeKind.MemberJoined   => ($"{actorAgentId} 加入聊天室", MessageVisibility.System),
            SystemNoticeKind.MemberLeft     => ($"{actorAgentId} 退出聊天室", MessageVisibility.System),
            SystemNoticeKind.MemberMuted    => ($"{actorAgentId} 被禁言", MessageVisibility.AdminOnly),
            SystemNoticeKind.MemberKicked   => ($"{actorAgentId} 被踢出", MessageVisibility.AdminOnly),
            SystemNoticeKind.HostChanged    => ($"主机切换：{actorAgentId} → {extra}", MessageVisibility.System),
            SystemNoticeKind.MessageRevoked => ($"{actorAgentId} 撤回了一条消息", MessageVisibility.System),
            // ... 其余同理
        };
        return new TeamMessage
        {
            MessageId = Guid.NewGuid().ToString("N"),
            TeamId = teamId,
            SenderId = "system",
            Content = content,
            MessageType = "system_notice",
            Visibility = visibility,
            Timestamp = DateTime.UtcNow
        };
    }
}
```

**系统通知投递规则**：
- `Public`/`System` → 广播到所有成员（含跨通道）
- `AdminOnly` → 仅投递给 `Role >= Admin` 的成员
- `Private` → 仅投递给 `ToAgentId` + `SenderId`
- `Hidden` → 不投递，仅持久化（审计/撤回记录）

### 决策10：消息去重 — TeamManager 用 MessageId 去重

`TeamManager._teamMessages` 从 `List<TeamMessage>` 改为 `ConcurrentDictionary<string, TeamMessage>`（MessageId → Message）：

```csharp
// 旧
private readonly ConcurrentDictionary<string, List<TeamMessage>> _teamMessages = new();

// 新
private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, TeamMessage>> _teamMessages = new();
```

`BroadcastMessageAsync`/`SendMessageAsync` 入列表前用 `MessageId` 去重：
```csharp
var msgDict = _teamMessages.GetOrAdd(teamId, _ => new ConcurrentDictionary<string, TeamMessage>());
if (!msgDict.TryAdd(message.MessageId, message))
{
    _logger?.LogDebug("Duplicate team message {MessageId} skipped", message.MessageId);
    return OperationResult<TeamInfo?>.Ok(_teams[teamId]);  // 幂等返回
}
```

**跨通道去重联动**：`MailboxHub.BroadcastAsync` 投递到 InProcess/File/NamedPipe/Network 时，各通道邮箱已用 `CoordinatorMessage.MessageId` 去重（ADR 0107/0108 已实现），`TeamManager` 层去重是第二道防线，防止同一消息重复入 `_teamMessages` 列表。

### 决策11：消息撤回 — RevokeMessageAsync

模仿 QQ 撤回（2 分钟内可撤回）：

```csharp
public async Task<OperationResult<TeamInfo?>> RevokeMessageAsync(
    string teamId, string messageId, string revokerId, string? reason = null,
    CancellationToken ct = default)
{
    // 1. 校验：消息存在 + 发送者是 revoker 或 revoker 是管理员
    // 2. 校验：2 分钟内（QQ 规则）
    // 3. 标记：消息 Visibility = Hidden, RevokeReason = reason
    // 4. 广播系统通知：SystemNoticeFactory.Create(MessageRevoked, ...)
}
```

### 决策12：@提及 — Mentions 字段 + 投递优化

`TeamMessage.Mentions` 指定 @的 AgentId 列表。投递时：
- 普通消息 → 广播所有成员
- @全体 → 广播所有成员 + 被@成员高亮通知
- @特定 → 广播所有成员 + 被@成员强提醒（类似 QQ @提醒）

### 实现路线（开心路径优先）

1. `ChatRoomInfo` 补 `ChatRoomId` + `ChatRoomMember` + `ChatRoomRole` + `ChatRoomMemberStatus`
2. `TeamMessage` 补 `Visibility`/`ToAgentId`/`RevokeReason`/`Mentions`
3. `MessageVisibility` + `SystemNoticeKind` + `SystemNoticeFactory`
4. `TeamManager._teamMessages` 改 `ConcurrentDictionary<string, ConcurrentDictionary<string, TeamMessage>>` + 去重
5. `TeamManager.BroadcastMessageAsync` 按可见性过滤投递
6. `TeamManager.RevokeMessageAsync` 撤回
7. `TeamManager.GetChatRoomInfo` 升级返回新 `ChatRoomInfo`
8. 单元测试 + E2E 测试

### 验证标准（追加）

6. `ChatRoomInfo.ChatRoomId` 非空且等同 `TeamId`
7. 同一 `MessageId` 广播两次 → `_teamMessages` 只有一条
8. `SystemNoticeKind.MemberMuted` → 仅管理员收到
9. `SystemNoticeKind.MemberJoined` → 所有成员收到
10. `RevokeMessageAsync` → 消息 `Visibility=Hidden` + 撤回通知广播
11. `@全体` → 所有成员收到 + 被@成员高亮

### 决策13：ChatRoomState 数据合并 + 内存控制 + 按需加载（2026-09-16 追加）

#### 问题：TeamManager 6 个字典 key 相同，数据分散

```
TeamManager 内部 6 个 ConcurrentDictionary<string, ...>，key 都是 teamId：
  _teams            : teamId → TeamInfo
  _teamMembers      : teamId → HashSet<string>
  _teamMessages     : teamId → ConcurrentDictionary<string, TeamMessage>
  _teamSessions     : teamId → string
  _teamAllowedPaths : teamId → Dictionary<string, TeamAllowedPath>
  _teamMemberDetails: teamId → Dictionary<string, TeamMemberInfo>
```

数据分散导致：
- 同一团队的 6 份数据可能不一致（部分字典有 teamId，部分没有）
- 查询需要跨 6 个字典，代码冗余
- 持久化/恢复需要同步 6 个字典
- 无法实现按需加载（必须一次性载入全部字典）

#### 决策：合并为 ChatRoomState 类

```csharp
public sealed class ChatRoomState
{
    public required TeamInfo Info { get; init; }
    public HashSet<string> Members { get; init; } = new();
    public ConcurrentDictionary<string, TeamMessage> Messages { get; init; } = new();
    public string? SessionId { get; set; }
    public Dictionary<string, TeamAllowedPath> AllowedPaths { get; init; } = new();
    public Dictionary<string, TeamMemberInfo> MemberDetails { get; init; } = new();
    public int MaxMessageCount { get; init; } = 1000;  // 内存控制
    public bool NeedsCleanup => Messages.Count > MaxMessageCount;
}
```

`TeamManager` 用 `ConcurrentDictionary<string, ChatRoomState>` 替代 6 个字典，`_agentToTeam` 保留（反向映射，key 是 agentId）。

#### 内存控制：数量限制 + 提示清理

- `MaxMessageCount`（默认 1000）：每个聊天室最多保留 1000 条消息
- 超过限制时**不强制删除**，仅标记 `NeedsCleanup = true`
- 用户主动调 `CleanupOldMessages()` 清理旧消息
- 系统通过 `IChatRoomStore.GetRoomsNeedingCleanupAsync()` 获取需要清理的房间列表，提示用户

对标 QQ：本地缓存有限（最近 1000 条），历史消息云端分页加载，群消息有保留期限。

#### 按需加载：IChatRoomStore 接口

```csharp
public interface IChatRoomStore
{
    Task<ChatRoomState?> LoadAsync(string teamId, CancellationToken ct);  // 按需加载
    Task SaveAsync(string teamId, ChatRoomState state, CancellationToken ct);
    Task<IReadOnlyList<string>> ListRoomIdsAsync(CancellationToken ct);  // 仅 ID 列表
    Task DeleteAsync(string teamId, CancellationToken ct);
    Task<IReadOnlyList<(string, int, int)>> GetRoomsNeedingCleanupAsync(CancellationToken ct);
}
```

默认不载入全部房间，仅当访问特定房间时调 `LoadAsync(teamId)` 按需加载。对标 QQ 云端存档：本地只缓存活跃房间，历史房间按需从存储加载。

#### 实现路线（渐进式迁移）

1. 创建 `ChatRoomState` 类 + `IChatRoomStore` 接口
2. 逐步迁移 TeamManager 的 6 个字典到 `ConcurrentDictionary<string, ChatRoomState>`
3. 实现 `IChatRoomStore`（基于现有 `TeamManager.Persistence.cs`）
4. 添加 `NeedsCleanup` 提示 + `CleanupOldMessages` 方法
5. 单元测试 + 集成测试

#### 验证标准（追加）

12. `ChatRoomState` 合并 6 个字典，单一 `ConcurrentDictionary<string, ChatRoomState>` 替代
13. 消息数超过 `MaxMessageCount` → `NeedsCleanup = true`
14. `CleanupOldMessages()` → 删除最旧消息，返回清理数
15. `IChatRoomStore.LoadAsync` → 按需加载，不一次性载入全部房间
16. `GetRoomsNeedingCleanupAsync` → 返回需要清理的房间列表
