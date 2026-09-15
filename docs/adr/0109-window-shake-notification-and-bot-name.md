# 0109. 窗口震动通知与子代理 bot 中文名

- 状态：proposed
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
