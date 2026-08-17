# TUI 验收缺陷修复计划

> 状态: 草案 v1  
> 日期: 2026-08-17  
> 范围: JoinCodeTui（Terminal.Gui v2.4.17）验收检验发现的 P0-P2 缺陷  
> 来源: 对照《类 ClaudeCode TUI 工具验收报告模板》八大模块检验  
> 关联: `TUI架构与消息管道重构设计.md`（架构设计）、`AgentTUI交互规格.md`（交互规格）

---

## 1. 检验结论

**总体判定: ⚠️ 有条件通过** — 渲染抽象优秀，但业务衔接断裂、测试空心、组件未接通。

当前 TUI 是"渲染骨架已搭好、业务衔接断裂"的半成品：
- 渲染层抽象（`ITuiComponent` + `TerminalPainter`）设计优秀，线程安全通过 `Invoke` 投递
- 但 `TuiModeRunner` 只接通流式输出的 4 种 chunk，权限/多 Agent/预览组件全悬空
- 测试仅覆盖纯逻辑（Pipe/Resize/CardManager），渲染/集成/交互测试空心
- 实际跑起来会"能聊不能干活、遇权限卡死"

### 1.1 八大模块覆盖现状

| 模块 | TUI 现状 | 证据 | 严重度 |
|---|---|---|---|
| 1. TUI 交互 | 🟡 部分 | 5层布局✅、F1-F5✅、鼠标✅、滚动⚠️(Label全量重绘无滚动条)、resize❌(Monitor未接入) | P2 |
| 2. AI 对话 | 🔴 断裂 | `TuiModeRunner.cs:227-234` 仅处理 4 种 chunk，缺 ToolCallEnd/ToolProgress/Complete | **P0** |
| 3. 代码理解 | ❌ 未实现 | TUI 无 grep/diff/符号浏览入口（CLI/GUI 有） | P1 |
| 4. 代码编辑 | ❌ 未实现 | TUI 无 patch 预览/应用/撤销 | P1 |
| 5. 命令执行 | ❌ 未实现 | TUI 无 shell/build/test 回显（CLI 有完整链路） | P1 |
| 6. 工具调用 | 🔴 断裂 | PermissionDialogView 已写但未接入 TuiModeRunner | **P0** |
| 7. 会话管理 | ❌ 未实现 | TUI 无历史/恢复/导出/多会话切换 | P1 |
| 8. 配置与安全 | 🟡 部分 | API Key 红线✅(CLI层)、权限弹窗已写未接、沙箱未体现 | P1 |

---

## 2. 缺陷清单（按优先级）

### 2.1 🔴 P0 — 链路断裂，TUI 实际不可用

#### P0-1: chunk 处理不全，工具结果丢失
- **位置**: `app/JoinCodeTui/TuiModeRunner.cs:227-234`
- **现象**: `ProcessQueueAsync` 内 `await foreach` 消费 `IQueryEngine.QueryAsync`，switch 仅处理 Content/Thinking/ToolCallStart/Error 四种，缺 ToolCallEnd/ToolProgress/Complete
- **后果**: 用户看不到工具执行结果、进度、完成状态，TUI 只能显示"开始调用工具"然后黑屏
- **对比**: GUI `MainViewModel.cs:1238-1299` 完整处理 7 种事件；CLI `CliEventConsumer.cs:52-193` 处理 8 种

#### P0-2: PermissionDialogView 未接入，权限请求卡死
- **位置**: `app/JoinCodeTui/Tui/Views/PermissionDialogView.cs:7`（已实现）、`app/JoinCodeTui/TuiModeRunner.cs`（未引用）
- **现象**: 权限确认弹窗组件已实现（显示工具名+描述+允许/拒绝按钮，`ShowAsync` 返回 `Task<bool>`），但 TuiModeRunner 未引用
- **后果**: 工具调用遇到权限请求时无 UI 闭环，要么卡死等待、要么默认拒绝
- **对比**: GUI `JccChatSession.cs:346-417` 有完整 `StreamWithPermissionRetryAsync` 撤回重发机制（最多 3 次重试）

#### P0-3: AgentPanesView / QueuedCommandsView 未接入
- **位置**: `app/JoinCodeTui/Tui/Views/AgentPanesView.cs`、`app/JoinCodeTui/Tui/Views/QueuedCommandsView.cs`（均已实现）、`TuiModeRunner.cs`（未组装）
- **现象**: 多 Agent 面板和投递预览组件已写但未加入 RootView 或 TuiModeRunner
- **后果**: 多 Agent 场景无可视化，投递队列无预览，组件代码闲置

### 2.2 🟠 P1 — 功能缺失，TUI 是半成品

#### P1-1: 跨模式抽象断裂
- **位置**: `foundation/Abstractions/09-composition/Presentation/IPresentationAdapter.cs:6`（注释"TUI 模式: 待引入"）、`IEventConsumer.cs:6`（注释"TUI 模式: TuiEventConsumer"未实现）
- **现象**: TUI 走了自己一套 `ITuiComponent`，未实现 `IPresentationAdapter`/`IEventConsumer`，三套 UI 无统一抽象
- **后果**: 维护成本三倍，事件处理逻辑在 CLI/GUI/TUI 各写一份

#### P1-2: PresentationAdapterFactory 硬编码 CLI
- **位置**: `app/JoinCode/Adapters/PresentationAdapterFactory.cs:22`
- **现象**: `CreateForCurrentEnvironment` 永远返回 `PresentationMode.Cli`
- **后果**: 工厂模式名存实亡，无法按环境自动选择表示层

#### P1-3: 命令执行/代码编辑/会话管理全缺
- **现象**: TUI 目前只能"聊天"，不能"干活"
  - 无 shell/build/test 命令执行回显（CLI 有完整 `SessionController` 链路）
  - 无 patch 预览/应用/撤销
  - 无历史/恢复/导出/多会话切换
- **后果**: TUI 无法作为开发工具使用，仅是聊天 demo

#### P1-4: 集成测试空文件
- **位置**: `tests/Integration/Integration.Tests/Host/AgentApp/TuiSessionTests.cs` — 0 字节
- **现象**: TUI 集成测试完全缺失
- **后果**: TUI 端到端链路无任何验证

### 2.3 🟡 P2 — 体验缺陷

#### P2-1: TerminalResizeMonitor 未接入
- **位置**: `app/JoinCodeTui/Tui/Rendering/TerminalResizeMonitor.cs:7`（已实现钳制+防抖）、`TuiModeRunner.cs`（未调用 `CheckAndNotify`）
- **现象**: 尺寸监控器已实现（钳制 80x24~500x200，200ms 防抖），但 TuiModeRunner 未接入
- **后果**: resize 事件驱动不生效，依赖 Terminal.Gui v2 自动布局

#### P2-2: OutputView 无真正滚动
- **位置**: `app/JoinCodeTui/Tui/Views/OutputView.cs:43`
- **现象**: `AppendLine` 自动追加，上限 10000 行（`:12`），但用 Label 全量重绘（`:91`），无滚动条
- **后果**: 大输出全量重绘卡顿，无滚动定位

#### P2-3: 设计文档与实现脱节
- **位置**: `docs/plans/TUI架构与消息管道重构设计.md` 描述的 `OnAgentOutput(AgentOutputChunk)` 在 `ITuiComponent` 中未实现（仅有 `OnQueueChanged`/`OnResize`）
- **后果**: 设计契约未落地，后续维护者误以为已实现

---

## 3. 修复方案

### 3.1 P0-1: 补全 chunk 处理

**涉及文件**:
- `app/JoinCodeTui/TuiModeRunner.cs`（主改）
- `app/JoinCodeTui/Tui/Views/OutputView.cs`（可能补渲染方法）

**修复思路**:
1. 对照 `AgentStreamChunkType` 枚举，补全 switch case：
   - `ToolCallEnd` → 渲染工具结果（✅/❌ + 结果摘要）
   - `ToolProgress` → 渲染进度（可选，可复用 OutputView 追加）
   - `Complete` → 渲染完成分隔线/统计
2. 参考 GUI `MainViewModel.cs:1264-1299` 的事件映射逻辑
3. 通过 `painter.Invoke` 投递到 MainLoop，保持线程安全

**验证方式**:
- 单元测试：Mock `IQueryEngine` 产出各 chunk 类型，断言 OutputView 收到对应文本
- E2E：MockServer + jcctui.exe + `--await 10`，跑一次工具调用，断言退出码 0

### 3.2 P0-2: 接入 PermissionDialogView

**涉及文件**:
- `app/JoinCodeTui/TuiModeRunner.cs`（主改）
- `app/JoinCodeTui/Tui/Views/PermissionDialogView.cs`（可能微调）

**修复思路**:
1. 在 TuiModeRunner 持有 `PermissionDialogView` 引用，加入 RootView
2. 引擎权限请求回调 → `permissionDialog.ShowAsync(toolName, description)` → 返回 bool 决策
3. 参考 GUI `JccChatSession.cs:346-417` 的撤回重发机制，TUI 简化为单次决策（允许/拒绝）
4. 权限决策结果回传引擎 `PermissionConfirmationHandler`

**验证方式**:
- 单元测试：Mock 权限请求，断言弹窗显示工具名、按钮点击返回正确 bool
- E2E：MockServer 触发需权限的工具调用，jcctui 自动选"允许"，断言工具执行

### 3.3 P0-3: 接入悬空组件

**涉及文件**:
- `app/JoinCodeTui/TuiModeRunner.cs`（组装）
- `app/JoinCodeTui/Tui/Views/RootView.cs`（可能补区域）

**修复思路**:
1. `QueuedCommandsView` 加入 RootView（PromptView 上方），订阅 `CommandQueue` 变化
2. `AgentPanesView` 加入 ContentArea，订阅多 Agent 状态
3. `TerminalResizeMonitor` 在 TuiModeRunner 启动时调用 `CheckAndNotify`，订阅 `SizeChanged` 广播给组件

**验证方式**:
- 单元测试：QueuedCommandsView 已有测试，验证接入后队列变化触发渲染
- 手工：启动 jcctui，输入多条命令观察投递预览

### 3.4 P1-1: 跨模式抽象统一（需架构决策）

**决策点**: TUI 实现 `IPresentationAdapter`/`IEventConsumer` 统一抽象 vs 保持 `ITuiComponent` 独立

**方案A（统一）**: TUI 实现 `IEventConsumer`，复用 CLI 的事件分发逻辑
- 优点：事件处理逻辑收敛，三套 UI 统一
- 缺点：`IEventConsumer` 偏 CLI 语义（NDJSON/彩色文本），TUI 需适配 View 树

**方案B（独立）**: 保持 `ITuiComponent`，TUI 自治
- 优点：TUI 渲染抽象纯净，不受 CLI 语义污染
- 缺点：事件映射逻辑重复，维护成本高

**建议**: 方案B + 提取共享 chunk→消息映射函数（纯函数，三套 UI 共用），避免抽象强行统一

### 3.5 P1-4: 补集成测试

**涉及文件**:
- `tests/Integration/Integration.Tests/Host/AgentApp/TuiSessionTests.cs`（空文件填充）

**修复思路**:
1. 参照 CLI E2E 框架（`ConversationScript` + `DualRoleConversationRunner`）
2. 启动 jcctui.exe + MockServer，通过 stdin 喂命令，`--await N` 超时
3. 断言退出码、stdout 关键内容、MockServer 请求记录

### 3.6 P2-1: 接入 TerminalResizeMonitor

**涉及文件**: `app/JoinCodeTui/TuiModeRunner.cs`

**修复思路**: 启动时 `resizeMonitor.CheckAndNotify()`，订阅 `SizeChanged` → `painter.NotifyResize(cols, rows)`，订阅 `SizeTooSmall` → 显示"终端太小"提示

---

## 4. 修复顺序与里程碑

| 阶段 | 任务 | 优先级 | 预期产物 | 验收标准 |
|---|---|---|---|---|
| M1 | P0-1 补全 chunk 处理 | P0 | TuiModeRunner switch 完整 | 单测覆盖 7 种 chunk + E2E 工具调用可见结果 |
| M2 | P0-2 接入 PermissionDialogView | P0 | 权限闭环生效 | E2E 权限工具可允许/拒绝 |
| M3 | P0-3 接入悬空组件 + P2-1 接入 ResizeMonitor | P0+P2 | 组件全组装 | 启动 jcctui 可见投递预览/多 Agent |
| M4 | 引入 FakeDriver + 渲染快照测试 | P1 | TUI 渲染可验证 | 快照断言 View 树/文本/颜色 |
| M5 | 补 TuiSessionTests 集成测试 | P1 | E2E 链路验证 | MockServer + jcctui 全绿 |
| M6 | 架构决策 P1-1（统一 vs 独立抽象） | P1 | 决策记录 | 用户确认方案 |
| M7 | P1-3 命令执行/代码编辑/会话管理 | P1 | TUI 可干活 | 能跑 build/test/编辑文件 |
| M8 | P2-2 OutputView 滚动优化 | P2 | 大输出不卡 | 10000 行滚动流畅 |

**渐进式要求**: 每个里程碑独立编译 + 单测 + git 提交，可随时中断恢复。

---

## 5. 可自动化验收 vs 必须人工验收

### 5.1 ✅ AI/自动化可验收（当前缺失，应补）

| 层级 | 工具 | 门槛 | 当前状态 |
|---|---|---|---|
| 纯逻辑单元测试 | xUnit | 100% 通过 | 🟢 已有 8 文件 ~76 Fact |
| 渲染快照测试 | FakeDriver + ApprovalTests | 人工审核 diff | 🔴 **缺失** |
| 集成测试 | xUnit + MockServer + jcctui.exe | 必须通过 | 🔴 **空文件** |
| E2E 任务集 | ConversationScript + --await | 通过率 ≥ 阈值 | 🔴 **缺失** |

### 5.2 ❌ 必须人工验收（不可替代）

| 验收项 | 原因 | 责任人 |
|---|---|---|
| Terminal.Gui 多终端渲染（Windows Terminal / VSCode 集成终端 / tmux） | Unicode、emoji、中文宽字符对齐只能人眼判 | 待定 |
| 流式输出跟手度 | chunk 到达时输出是否顺滑、输入是否卡顿 | 待定 |
| 权限弹窗真实交互 | 允许/拒绝按钮是否真的拦住危险工具 | 待定 |
| resize 到极小（80x24） | 组件是否错位、钳制是否生效 | 待定 |
| 真实开发任务（3-5 个） | 开放性长尾问题只有人能判 | 待定 |
| 安全红队测试 | Prompt 注入、危险命令、凭证泄露 | 待定 |

---

## 6. 验收标准

### 6.1 自动化验收（CI 硬门槛）
- [ ] TUI 单元测试 100% 通过
- [ ] TUI 渲染快照测试通过（新增）
- [ ] TuiSessionTests 集成测试通过（填充空文件）
- [ ] jcctui.exe AOT 发布成功

### 6.2 人工验收（发布前必做）
- [ ] TUI 多终端渲染正确（至少 Windows Terminal + VSCode 集成终端）
- [ ] 完整工具调用链路：用户输入 → AI → 工具调用 → 权限弹窗 → 工具结果渲染
- [ ] 3 个真实开发任务完成（加功能/重构/修 bug）
- [ ] 安全红队：恶意 README 注入、危险命令拦截、API Key 不泄露

### 6.3 验收结论模板
- □ 同意发布：自动化全通过 + 人工验收无 P0/P1
- □ 有条件发布：P2 可遗留，附清单
- □ 不通过：存在 P0/P1，修复后重验

---

## 7. 关键文件索引

| 文件 | 职责 | 修改频率 |
|---|---|---|
| `app/JoinCodeTui/TuiModeRunner.cs` | 衔接核心，P0 主战场 | 高 |
| `app/JoinCodeTui/Tui/Views/PermissionDialogView.cs` | 权限弹窗（已写待接） | 中 |
| `app/JoinCodeTui/Tui/Views/OutputView.cs` | 输出渲染（滚动优化） | 中 |
| `app/JoinCodeTui/Tui/Rendering/TerminalPainter.cs` | 唯一绘制入口 | 低 |
| `app/JoinCodeTui/Tui/Rendering/ITuiComponent.cs` | 组件接口 | 低 |
| `tests/Integration/Integration.Tests/Host/AgentApp/TuiSessionTests.cs` | 集成测试（空待填） | 高 |
| `tests/Unit/Host.Tests/Tui/` | 单元测试目录 | 中 |

---

<!-- 🤖 Auto Decision: 2026-08-17 -->
<!-- 决策: 先写修复计划文档而非直接改代码 -->
<!-- 原因: P0-P2 缺陷较多，需可跟踪的修复计划避免遗漏，且用户明确选择"先写修复计划文档" -->
<!-- 替代方案: 直接修 P0 chunk 衔接（更快但缺全局视图）-->
<!-- 验证: 文档落盘成功，格式与现有 docs/plans/ 一致 ✅ -->
