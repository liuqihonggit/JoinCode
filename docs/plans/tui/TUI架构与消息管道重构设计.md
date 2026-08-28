# TUI 架构与消息管道重构设计

> 状态: 草案 v1  
> 日期: 2026-08-16  
> 范围: 议题 1-6（TUI 线程亲和性 / 统一绘制入口 / 输入队列 / 组件自适应 / 多 Agent 对齐 / 邮箱收敛）  
> 技术选型: Terminal.Gui v2.4.17（已标记 `IsAotCompatible=true` + `IsTrimmable=true`）

---

## 1. 背景与目标

### 1.1 六个议题

| # | 议题 | 核心诉求 |
|---|------|----------|
| 1 | TUI 线程亲和性 | 仅由一个主控消息循环负责界面绘制，从每个注册成员拉取消息渲染，不跨线程更新 |
| 2 | 统一"终端"绘制入口 | 所有绘制仅从"终端"名称的函数进行，防止组件错误引用和发送 |
| 3 | 输入不打断投递队列 | 主代理输出期间用户输入不打断，投递消息队列等待；需引入"投递中"TUI 组件 |
| 4 | TUI 组件自适应 | 自适应宽高、校验方式、可视化分析、尺寸事件驱动 |
| 5 | 多 Agent 设计对齐 | 把 plan agent 等加回设计，参考 TS 原版 的 fork+过滤工具 |
| 6 | @xxAgent 邮箱传递 | 改为邮箱传递消息，收敛当前乱飞的消息管道 |

### 1.2 设计原则

1. **单一渲染线程**：所有 UI 变更经 `Terminal.Gui.Application.MainLoop` 调度，业务线程通过 `Application.Invoke` 投递到主循环
2. **唯一绘制出口**：封装 `TerminalPainter`（终端命名），所有绘制经此入口，禁止业务代码直接 `Console.Write`
3. **队列驱动**：用户输入 → 优先级队列 → 主循环 drain → 处理；队列状态驱动"投递中"组件渲染
4. **渐进式迁移**：保留 CLI 模式（`--no-tui`）作为降级路径，TUI 模式逐步替换默认路径

---

## 2. 现状分析（问题清单）

### 2.1 议题 1：无统一 TUI 渲染层

- **CLI 模式**：直接 `System.Console.Write`，线程安全靠 Console 内部同步，无渲染层
- **GUI 模式**：Avalonia，`Dispatcher.UIThread.Post` 散落 8 处（`MainViewModel.cs`×4、`MainWindow.axaml.cs`×1、`AvaloniaInteractiveService.cs`×1、`App.axaml.cs`×2），无统一封装
- **无统一消息循环**：`ReplLoopStep` 用 3 个并行 Task + 2 个 Channel 模拟，非真正的事件循环
- **死注释**：`IPresentationAdapter.cs:6` 引用不存在的 `TuiPresentationAdapter` 和 `AgentTuiApp`

### 2.2 议题 2：多写入入口

| 入口 | 路径 | 问题 |
|------|------|------|
| `TerminalHelper.WriteRaw` | `app/JoinCode/Cli/Display/TerminalHelper.cs` | 底层直写 `System.Console` |
| `ConsoleOutput.WriteLine` | `app/JoinCode/Services/Core/ConsoleOutput.cs` | 另一个直写入口 |
| `CliCommandConsole` | `app/JoinCode/Cli/Display/CliCommandConsole.cs` | 包装 TerminalHelper |
| `CliEventConsumer` | `app/JoinCode/Adapters/CliEventConsumer.cs` | DX 模式直写 TerminalHelper，AX 模式写 NDJSON |

无约束机制阻止业务代码直接 `Console.Write`。

### 2.3 议题 3：输入队列缺陷

- **多子代理输入丢弃 bug**：`ReplLoopStep.cs:128-134`，当 `isProcessing==1` 且多个子代理运行时，用户输入仅打印提示后 `continue` 丢弃，不入队
- **无优先级队列**：`inputChannel` 是无界无优先级 Channel，任务通知和用户输入同优先级
- **无队列预览组件**：用户投递的输入无可视化，不知道队列里有什么
- **ConfirmationGate 静态状态串扰**：`ConfirmationGate.cs` 用 `static volatile` 字段，多 CliSession 实例共享同一状态

### 2.4 议题 4：CLI 无自适应

- `TerminalHelper.GetWidth()` 调用时实时读 `Console.WindowWidth`，无 resize 事件驱动
- 终端 resize 后已渲染内容（如 `new string('─', GetWidth())`）不重排
- 无布局引擎，无视口检测，无性能追踪
- `TuiSymbols` 命名误导（应改 `CliSymbols`）

### 2.5 议题 5：工具过滤层数过多

当前 6 层工具过滤叠加（CLI 参数 / Agent 定义 / Fork / 权限模式 / AgentBase / 权限规则），认知负担重。TS 原版 仅 3 层（`ALL_AGENT_DISALLOWED_TOOLS` / `CUSTOM_AGENT_DISALLOWED_TOOLS` / `ASYNC_AGENT_ALLOWED_TOOLS`）。

### 2.6 议题 6：3 套消息机制循环

```
AgentMessageBroker ──写──→ TeammateMailboxService（文件）
        ↑                           │
        └─── MailboxPoller ──轮询───┘
```

存在 `Broker → Mailbox → Poller → Broker` 循环。加上 `AgentInputForwardQueue`、`AgentOutputChannelManager` 共 5 套机制，职责边界模糊。

---

## 3. 目标架构

### 3.1 整体分层

```
┌─────────────────────────────────────────────────────────┐
│  TUI 渲染层（Terminal.Gui Application.MainLoop）          │
│  ┌─────────────┐ ┌─────────────┐ ┌───────────────────┐  │
│  │ PromptView  │ │ OutputView  │ │ QueuedCommandsView│  │
│  │ (输入框)     │ │ (输出流)     │ │ (投递中预览)       │  │
│  └─────────────┘ └─────────────┘ └───────────────────┘  │
│  ┌─────────────┐ ┌─────────────┐ ┌───────────────────┐  │
│  │ AgentPanes  │ │ StatusBar   │ │ PermissionDialog  │  │
│  │ (多Agent面板)│ │ (状态栏)     │ │ (权限确认弹窗)     │  │
│  └─────────────┘ └─────────────┘ └───────────────────┘  │
└──────────────────────────┬──────────────────────────────┘
                           │ TerminalPainter（唯一绘制入口）
┌──────────────────────────┴──────────────────────────────┐
│  消息管道层                                               │
│  ┌──────────────────────────────────────────────────┐   │
│  │ CommandQueue（优先级队列 now>next>later）          │   │
│  │  ├─ 用户输入 (priority=next)                      │   │
│  │  ├─ 任务通知 (priority=later)                     │   │
│  │  └─ 权限响应 (priority=now)                       │   │
│  └──────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────┐   │
│  │ MailboxHub（邮箱中枢，收敛 3 套机制）              │   │
│  │  ├─ InProcessMailbox（进程内 Channel，同步 subagent）│   │
│  │  └─ FileMailbox（文件邮箱，teammate swarm 跨进程）  │   │
│  └──────────────────────────────────────────────────┘   │
└──────────────────────────┬──────────────────────────────┘
                           │
┌──────────────────────────┴──────────────────────────────┐
│  Agent 执行层（Fork + 工具过滤）                          │
│  ┌────────────┐ ┌────────────┐ ┌────────────────────┐  │
│  │ PlanAgent  │ │ ExploreAgent│ │ VerificationAgent │  │
│  └────────────┘ └────────────┘ └───────────────────┘  │
│  工具过滤: DisallowedTools(黑名单) + AllowedTools(白名单)│
└─────────────────────────────────────────────────────────┘
```

### 3.2 线程模型

| 线程 | 职责 | 与 UI 交互方式 |
|------|------|----------------|
| **MainLoop 线程**（Terminal.Gui） | 渲染 + 输入事件分发 + 队列 drain | 唯一可直接操作 View 的线程 |
| **Agent 工作线程**（ThreadPool） | LLM 调用 + 工具执行 | 通过 `Application.Invoke` 投递 UI 更新 |
| **Mailbox 轮询线程**（Timer） | 文件邮箱轮询 | 通过 `Application.Invoke` 投递新消息通知 |
| **stdin 读取线程** | 异步读 stdin | 解析后入 `CommandQueue`，不直接操作 UI |

**铁律**：任何非 MainLoop 线程操作 View 必须经 `Application.Invoke`，违反即 bug。

---

## 4. 各议题详细设计

### 4.1 议题 1+2：TUI 渲染层 + 统一绘制入口

#### 4.1.1 TerminalPainter（唯一绘制入口）

```
namespace JoinCode.Tui.Rendering;

/// <summary>
/// 终端唯一绘制入口。所有 UI 变更必须经此入口，禁止直接 Console.Write。
/// </summary>
public sealed class TerminalPainter
{
    private readonly Application _app;          // Terminal.Gui Application
    private readonly RootView _root;            // 根 View 树

    /// <summary>投递渲染请求到 MainLoop（线程安全）。</summary>
    public void Invoke(Action drawAction) => _app.Invoke(drawAction);

    /// <summary>投递异步渲染请求到 MainLoop。</summary>
    public Task InvokeAsync(Func<Task> drawAction) => _app.InvokeAsync(drawAction);

    /// <summary>注册 TUI 组件到渲染树。</summary>
    public void Register(ITuiComponent component);

    /// <summary>注销 TUI 组件。</summary>
    public void Unregister(ITuiComponent component);
}
```

**约束机制**：
1. 编译时：Roslyn 分析器 `JCC_TUI001` 禁止 `System.Console.Write*` 出现在 `JoinCode.Tui.*` 命名空间外
2. 运行时：`TerminalHelper.WriteRaw` 标记 `[Obsolete("Use TerminalPainter instead")]`，调用时重定向到 `TerminalPainter`
3. 代码审查：PR 模板检查项新增"无直接 Console.Write"

#### 4.1.2 ITuiComponent 接口

```
public interface ITuiComponent
{
    View TerminalView { get; }              // Terminal.Gui View
    void OnQueueChanged(QueueSnapshot s);   // 队列状态驱动
    void OnResize(int cols, int rows);      // 尺寸事件驱动
    // 注: OnAgentOutput 已移除 — Agent 输出改用 TuiModeRunner.ChunkToText 映射 + OutputView.AppendLine 显示
}
```

#### 4.1.3 MainLoop 消息循环

```
// 启动入口（替换 ReplLoopStep 的 3-Task 模型）
using IApplication app = Application.Create();
app.Init();

var painter = new TerminalPainter(app);
var queue = new CommandQueue();
var root = new RootView(painter, queue);

// 注册组件
painter.Register(new PromptView(queue));
painter.Register(new OutputView());
painter.Register(new QueuedCommandsView(queue));
painter.Register(new AgentPanesView());
painter.Register(new StatusBarView());
painter.Register(new PermissionDialogView());

// 启动 MainLoop（阻塞，直到退出）
app.Run(root.TerminalView);

// MainLoop 内部驱动：
// 1. stdin 事件 → 解析 → CommandQueue.Enqueue
// 2. Timer 事件 → Mailbox 轮询 → CommandQueue.Enqueue
// 3. CommandQueue 变化 → QueuedCommandsView.OnQueueChanged
// 4. Agent 输出 → TuiModeRunner.ChunkToText 映射 → OutputView.AppendLine（经 painter.Invoke）
```

### 4.2 议题 3：优先级队列 + 投递中组件

#### 4.2.1 CommandQueue（优先级队列）

```
public enum QueuePriority { Now, Next, Later }

public sealed class CommandQueue
{
    // 三级优先级，同优先级 FIFO
    private readonly ConcurrentQueue<QueuedCommand> _now = new();
    private readonly ConcurrentQueue<QueuedCommand> _next = new();
    private readonly ConcurrentQueue<QueuedCommand> _later = new();

    public void Enqueue(QueuedCommand cmd);       // 入队
    public QueuedCommand? Dequeue();               // 按优先级出队
    public QueueSnapshot GetSnapshot();            // 当前队列快照（驱动 UI）
}

public record QueuedCommand(string Content, CommandOrigin Origin, QueuePriority Priority);
public record QueueSnapshot(IReadOnlyList<QueuedCommand> Pending);
```

**优先级规则**（对齐 TS 原版）：
- `Now`：权限确认响应（立即处理）
- `Next`：用户输入（默认）
- `Later`：任务通知（不饥饿用户输入）

#### 4.2.2 QueuedCommandsView（投递中预览组件）

```
public sealed class QueuedCommandsView : View, ITuiComponent
{
    private readonly CommandQueue _queue;
    private readonly ListView _listView;   // Terminal.Gui ListView

    public void OnQueueChanged(QueueSnapshot s)
    {
        Application.Invoke(() =>
        {
            _listView.SetSource(s.Pending.Select(c => $"⏳ {c.Content}").ToList());
            _listView.Visible = s.Pending.Count > 0;
        });
    }
}
```

#### 4.2.3 输入处理流程（修复丢弃 bug）

```
// PromptView 接收到用户输入
void OnUserInput(string input)
{
    var parsed = SubAgentMentionParser.Parse(input);
    if (parsed.IsMention)
    {
        // @agentName 消息 → 写邮箱
        _mailboxHub.Send(parsed.AgentName, parsed.Message);
    }
    else
    {
        // 普通输入 → 入队（不再丢弃！）
        _queue.Enqueue(new QueuedCommand(input, CommandOrigin.User, QueuePriority.Next));
    }
}

// MainLoop drain（主代理空闲时处理）
async Task DrainQueueAsync()
{
    while (_queue.TryDequeue(out var cmd))
    {
        await _session.ProcessUserInputAsync(cmd.Content);
    }
}
```

**修复**：多子代理运行时不再 `continue` 丢弃，而是入队等待，或提示用户用 @指定后仍入队缓存。

### 4.3 议题 4：组件自适应（Terminal.Gui 自带）

Terminal.Gui v2 自带：
- **布局引擎**：`Pos`/`Dim` 声明式布局（`Pos.Center()`、`Dim.Fill()`），自动适应宽高
- **resize 事件**：`Application.SizeChanged` 事件，终端 resize 自动重排
- **视口检测**：`View.Visible` + `View.NeedsLayout` 驱动
- **双缓冲渲染**：`Application.Driver` 双缓冲，避免闪烁

#### 4.3.1 尺寸事件驱动

```
public sealed class RootView : View
{
    public RootView()
    {
        // Terminal.Gui 自动监听终端 resize
        Application.SizeChanged += OnResize;
    }

    private void OnResize(SizeChangedEventArgs e)
    {
        // 通知所有子组件
        foreach (var child in _components)
        {
            child.OnResize(e.Size.Width, e.Size.Height);
        }
    }
}
```

#### 4.3.2 校验方式 + 可视化分析

1. **布局校验**：`View.ToString()` 输出布局树，测试断言
2. **尺寸快照测试**：用 txt 模拟终端输出（`Terminal.Gui.FakeDriver`），断言渲染结果
3. **性能追踪**：`Application.MainLoop.Timeout` + `Stopwatch` 记录帧耗时，超 16ms 告警

```
// 测试示例（用 FakeDriver，不依赖真实终端）
[Test]
public async Task QueuedCommandsView_Resize_Narrow_HidesPreview()
{
    var driver = new FakeDriver { Cols = 40, Rows = 24 };  // 窄终端
    var view = new QueuedCommandsView(_queue);
    view.OnResize(40, 24);
    Assert.That(view.IsPreviewVisible, Is.False);  // 窄屏隐藏预览
}
```

### 4.4 议题 5：多 Agent 设计对齐

#### 4.4.1 Agent 定义对齐 TS 原版

当前已有 `Plan/Explore/Verification/General/Guide`（`BuiltInAgentToolHandlers.cs`），**无需新增**，仅需对齐工具过滤为 3 层：

| 层 | 职责 | 对应 TS 原版 |
|----|------|-----------------|
| `AllAgentDisallowedTools` | 所有 subagent 禁用（防递归） | `ALL_AGENT_DISALLOWED_TOOLS` |
| `AsyncAgentAllowedTools` | 后台 agent 白名单 | `ASYNC_AGENT_ALLOWED_TOOLS` |
| `AgentDefinition.DisallowedTools` | agent 定义级黑名单 | `disallowedTools` 字段 |

**删除冗余层**：权限模式层、AgentBase 应用层合并到上述 3 层。

#### 4.4.2 Fork + 过滤工具（已实现，保留）

当前 `ForkSubAgentManager` + 5 个 Fork 中间件已实现，对齐 TS 原版 的 `forkSubagent.ts`：
- `ForkSpawnMiddleware` 已支持 `UseExactTools`（精确继承父工具集）
- `CacheSafeParams` 已透传（保证 prompt cache 命中）

**无需改动**，仅需在文档中明确 3 层过滤优先级。

### 4.5 议题 6：邮箱传递消息收敛

#### 4.5.1 MailboxHub（收敛 3 套机制）

```
public sealed class MailboxHub
{
    private readonly InProcessMailbox _inProcess;   // 进程内（同步 subagent）
    private readonly FileMailbox _file;             // 文件（teammate swarm）
    private readonly MailboxPoller _poller;         // 文件邮箱轮询

    /// <summary>发送消息（自动选择通道）。</summary>
    public Task SendAsync(string recipient, AgentMessage msg, MailboxKind kind)
    {
        return kind switch
        {
            MailboxKind.InProcess => _inProcess.SendAsync(recipient, msg),
            MailboxKind.File => _file.SendAsync(recipient, msg),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}

public enum MailboxKind { InProcess, File }
```

#### 4.5.2 消除循环

**当前循环**：`AgentMessageBroker` 写 `TeammateMailboxService`，`MailboxPoller` 又把 `TeammateMailboxService` 消息注入回 `AgentMessageBroker`。

**修复**：
- `InProcessMailbox`：纯进程内 `Channel<AgentMessage>`，**不写文件**，用于同步 subagent
- `FileMailbox`：纯文件持久化，用于 teammate swarm 跨进程
- `MailboxPoller`：读 `FileMailbox` → 投递到 `CommandQueue`（而非 `InProcessMailbox`），**断开循环**

```
// 修复后流程
// 同步 subagent: InProcessMailbox.Send → InProcessMailbox.Receive（进程内直通）
// Teammate swarm: FileMailbox.Send → FileMailbox 文件 → Poller → CommandQueue.Enqueue
// @mention: PromptView 解析 → MailboxHub.Send(InProcess 或 File)
```

#### 4.5.3 @xxAgent 改邮箱传递

```
// SubAgentMentionParser 解析 @agentName 消息
// → MailboxHub.SendAsync(agentName, msg, kind)
// kind 判断：
//   - teammate（跨进程）→ File
//   - 同步 subagent（进程内）→ InProcess
```

**移除**：`AgentInputForwardQueue`（职责并入 `InProcessMailbox`）、`AgentMessageBroker`（职责拆分到 `InProcessMailbox` + `FileMailbox`）。

---

## 5. 迁移路径（渐进式）

### 阶段 0：前置验证（AOT 卫星项目）

**任务**：创建卫星项目验证 Terminal.Gui v2.4.17 的 NativeAOT 编译。

```
tools/TerminalGuiAotProbe/
├── TerminalGuiAotProbe.csproj   # PublishAot=true, TrimMode=full
└── Program.cs                    # 最小 Terminal.Gui app
```

**验证命令**：
```powershell
dotnet publish tools/TerminalGuiAotProbe -c Release
# 期望: 无 AOT 警告，生成 native exe
```

**风险点**：Terminal.Gui 依赖 `Markdig`、`TextMateSharp`、`Microsoft.Extensions.Configuration.Binder`，需验证这些依赖 AOT 兼容。若不兼容，考虑：
- 选项 A：禁用 Terminal.Gui 的 Markdown/语法高亮功能（排除 Markdig/TextMateSharp 依赖）
- 选项 B：回退到自研轻量 TUI（参考 TS 原版 Ink，用 C# 实现）
- 选项 C：Terminal.Gui 仅用于 Debug 模式，Release AOT 用 CLI 降级

### 阶段 1：引入 TUI 渲染层骨架（不破坏现有 CLI）

1. 新增 `app/JoinCode/Tui/` 目录
2. 实现 `TerminalPainter`、`ITuiComponent`、`RootView`
3. 实现 `PromptView`、`OutputView`、`StatusBarView`（最小可用）
4. 新增 `--tui` 启动参数，默认仍走 CLI（`ReplLoopStep`）
5. 编译 + 冒烟测试

### 阶段 2：优先级队列 + 投递中组件

1. 实现 `CommandQueue`（三级优先级）
2. 实现 `QueuedCommandsView`
3. 修复 `ReplLoopStep` 多子代理输入丢弃 bug（先在 CLI 模式修，入队缓存）
4. `ConfirmationGate` 改实例字段（消除静态串扰）
5. 编译 + 单元测试 + E2E

### 阶段 3：邮箱收敛

1. 实现 `InProcessMailbox`、`FileMailbox`、`MailboxHub`
2. `MailboxPoller` 改投递到 `CommandQueue`（断循环）
3. `@mention` 改走 `MailboxHub`
4. 移除 `AgentInputForwardQueue`、`AgentMessageBroker`（渐进式，先标记 obsolete）
5. 编译 + 单元测试 + E2E

### 阶段 4：TUI 模式接入主流程

1. `--tui` 模式接入 `CommandQueue` + `MailboxHub`
2. 实现 `AgentPanesView`、`PermissionDialogView`
3. 尺寸事件驱动 + 布局校验测试
4. 编译 + E2E（MockServer + jcc --tui）

### 阶段 5：工具过滤简化 + 清理

1. 工具过滤收敛为 3 层（`AllAgentDisallowedTools` / `AsyncAgentAllowedTools` / `AgentDefinition.DisallowedTools`）
2. 清理死注释（`IPresentationAdapter` 的 `TuiPresentationAdapter` 引用）
3. `TuiSymbols` 改名 `CliSymbols`
4. 编译 + 全量测试

### 阶段 6：TUI 设为默认（可选）

1. `--tui` 设为默认，`--no-tui` 降级到 CLI
2. 文档更新
3. 全量测试 + AOT 发布验证

---

## 6. 风险点与验证

| 风险 | 影响 | 缓解 |
|------|------|------|
| Terminal.Gui 依赖 AOT 不兼容 | Release 发布失败 | 阶段 0 卫星项目验证，不兼容则降级 |
| TUI 模式占用终端全屏，与 E2E 脚本冲突 | E2E 测试无法用 stdin/stdout 管道 | E2E 保留 `--no-tui` 模式；TUI 模式用 `FakeDriver` 测试 |
| Terminal.Gui MainLoop 阻塞，与现有 async 管道集成 | 死锁 | `Application.Invoke` 投递，MainLoop 内 `async void` 谨慎 |
| 邮箱收敛破坏现有 teammate swarm | 跨进程通信中断 | 渐进式，先并存后切换；`FileMailbox` 保留 `TeammateMailboxService` 实现 |
| 工具过滤层数减少导致权限松动 | 安全风险 | 3 层过滤覆盖原 6 层语义，测试断言权限不变 |

---

## 7. 前置任务清单

- [x] **阶段 0**：Terminal.Gui AOT 卫星项目验证（20MB native exe 生成成功）
- [x] **阶段 1**：TUI 渲染层骨架（TerminalPainter + ITuiComponent + RootView + --tui 参数）
- [x] **阶段 2**：Bug1 修复 + Bug2 修复 + CommandQueue（10 单元测试）+ CommandQueue 接入 ReplLoopStep
- [x] **阶段 2 剩余**：QueuedCommandsView 投递中预览组件
- [x] **阶段 3 邮箱命名**：IAgentMessageBroker→IMailbox + AgentMessageBroker→InProcessMailbox + SendAsync/ReceiveAsync
- [x] **阶段 4 部分**：PromptView + OutputView + StatusBarView 基础 TUI 视图
- [x] **阶段 5 部分**：死注释清理 + TuiSymbols 死代码移除
- [ ] **阶段 3 剩余**：MailboxHub + FileMailbox + MailboxPoller 断循环 + @mention 走邮箱
- [ ] **阶段 4 剩余**：AgentPanesView + PermissionDialogView + RootView resize 事件 + TUI 主循环接入
- [ ] **阶段 5 剩余**：工具过滤 6 层收敛为 3 层
- [ ] **阶段 6**：TUI 设为默认（可选）

### 阶段 3 调研结论（2026-08-16）

当前 `AgentMessageBroker`→`TeammateMailboxService`→`MailboxPoller`→`AgentMessageBroker` "循环"是**跨进程桥接的有意设计**：
- 同进程 subagent：Broker.Channel 直传（mailboxService 为 null 时不写文件）
- 跨进程 teammate：Broker 写文件邮箱 → Poller 轮询读回 → 注入 Broker.Channel（桥接）

**问题不是循环本身**，而是：
1. `Broker.SendMessageAsync` 对所有消息都调 `PersistToMailboxAsync`（同进程消息也写文件，多余）
2. 3 套机制并存认知负担重（Broker + MailboxService + Poller + InputForwardQueue + OutputChannelManager）

**重构方向**：分离职责而非消除桥接。`InProcessMailbox`（纯 Channel）+ `FileMailbox`（纯文件）+ `MailboxHub`（统一入口按 MailboxKind 路由）。改动涉及 core/ai/Agents 核心，需渐进式。

---

## 8. 与 TS 原版 对齐对照

| 议题 | TS 原版 实现 | 本设计实现 | 对齐度 |
|------|------------------|-----------|--------|
| 1. TUI 线程亲和 | 自研 Ink + React + Yoga，单例 `Ink` 类 | Terminal.Gui `Application.MainLoop` | ✅ 等价（MainLoop 即单例渲染控制器） |
| 2. 统一绘制入口 | `writeDiffToTerminal` 唯一 stdout 写入 | `TerminalPainter` 唯一入口 + 分析器约束 | ✅ 等价 |
| 3. 输入队列 | `commandQueue` 三级优先级 + `PromptInputQueuedCommands` | `CommandQueue` 三级 + `QueuedCommandsView` | ✅ 等价 |
| 4. 组件自适应 | Yoga 布局 + `handleResize` + `useTerminalViewport` | Terminal.Gui `Pos/Dim` + `SizeChanged` + `FakeDriver` 测试 | ✅ 等价 |
| 5. 多 Agent | `filterToolsForAgent` 3 层 | 3 层过滤（收敛 6 层） | ✅ 对齐 |
| 6. 邮箱传递 | `teammateMailbox`（文件）+ `AgentTool`（直调）双模式 | `MailboxHub`（`InProcessMailbox` + `FileMailbox`）双模式 | ✅ 对齐 |

---

<!-- 🤖 Auto Decision: 2026-08-16 -->
<!-- 决策: TUI 技术选型用 Terminal.Gui v2.4.17 -->
<!-- 原因: 用户选择；Terminal.Gui 自带布局引擎/resize事件/组件库，标记 IsAotCompatible=true -->
<!-- 替代方案 A: 自研轻量 TUI（参考 TS 原版 Ink，工作量大） -->
<!-- 替代方案 B: 保持 CLI+GUI 双路径仅统一抽象（改动小但未解决根本问题） -->
<!-- 风险: Terminal.Gui 依赖 Markdig/TextMateSharp/Microsoft.Extensions.Configuration.Binder，AOT 兼容性需阶段0卫星项目验证 -->
<!-- 验证: 待阶段0 AOT 卫星项目验证后确认 -->

---

## 9. 工具过滤 6 层→3 层收敛详细映射（2026-08-16 调研）

### 9.1 当前 6 层清单

| 层 | 文件 | 过滤逻辑 | 输入→输出 |
|----|------|----------|-----------|
| 1 CLI 参数 | `ApplicationBuilder.cs:58-84` | `--allowed-tools`/`--disallowed-tools` → `PermissionConfig` | CLI args → AutoApprovedTools/AutoRejectedTools |
| 2 Agent 定义 | `DefinitionResolutionMiddleware.cs:29-52` + `ContextSetupMiddleware.cs:41-42` | `AgentRoleProfile.AllowedTools/DisallowedTools` → `SubAgentOptions` | 角色注册表 → SubAgentOptions |
| 3 Fork | `ForkSpawnMiddleware.cs:71-74` | `UseExactTools` → `CacheSafeParams.ToolNames` 精确继承 | ForkOptions → SubAgentOptions |
| 4 权限模式 | `AgentToolRestrictions.cs` + 2× `AgentRestrictionMiddleware.cs` | `PermissionMode` → 静态 `ToolSecuritySets` 查表 | PermissionMode + ToolName → allow/deny |
| 5 AgentBase 应用 | `AgentBase.cs:546-569` + `QueryOptions.cs:19-35` + `QueryEngine.cs:461-471` | `SubAgentOptions` → `QueryOptions.IsToolAllowed` | QueryOptions + ToolName → allow/deny |
| 6 权限规则 | `ToolPermissionFilter.cs` + `AgentPermissionMode.cs:44-104` | 动态 `ToolDenyRule` + `AgentPermissionRule` | 规则集 + ToolName → allow/deny |

### 9.2 目标 3 层

| 目标层 | 职责 | 合并的当前层 | 对应 TS 原版 |
|--------|------|-------------|-----------------|
| `AllAgentDisallowedTools` | 所有 subagent 禁用（防递归） | 4（静态集）+ 6（deny 规则防递归部分） | `ALL_AGENT_DISALLOWED_TOOLS` |
| `AsyncAgentAllowedTools` | 后台 agent 白名单 | 3（Fork UseExactTools）+ 1（CLI --allowed-tools） | `ASYNC_AGENT_ALLOWED_TOOLS` |
| `AgentDefinition.DisallowedTools` | agent 定义级黑名单 | 2（Agent 定义）+ 5（AgentBase 应用） | `disallowedTools` 字段 |

### 9.3 冗余点

1. **两个同名 `AgentRestrictionMiddleware`**（层 4）在不同管道做相同检查：
   - `core/safety/Guard/.../Policy/AgentRestrictionMiddleware.cs`（权限检查管道，返回 Rejected 结果）
   - `core/execution/McpToolDispatch/.../AgentRestrictionMiddleware.cs`（工具执行管道，抛异常）
2. **层 4 与层 6 语义重叠**：`AgentToolRestrictions`（静态模式集）vs `ToolPermissionFilter`（动态 deny 规则）
3. **层 5 与层 4 职责重叠**：`QueryOptions.IsToolAllowed`（动态 QueryOptions）vs `AgentRestrictionMiddleware`（静态模式集）

### 9.4 收敛策略（渐进式）

1. **Step 1**：创建 `ToolFilterPolicy` 统一入口，聚合 3 层检查（不删除旧代码）
2. **Step 2**：切换 `QueryEngine.ExecuteToolAsync` 到 `ToolFilterPolicy`（替代层 5）
3. **Step 3**：合并两个 `AgentRestrictionMiddleware` 为一个（消除层 4 冗余）
4. **Step 4**：将 `ToolPermissionFilter` 动态规则合并到 `ToolFilterPolicy`（消除层 6 冗余）
5. **Step 5**：删除旧中间件，全量测试验证权限不变
