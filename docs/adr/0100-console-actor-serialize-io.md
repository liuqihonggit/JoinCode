# ADR 0100: ConsoleActor 串行化 Console I/O 消除并发竞态

**状态**: accepted
**日期**: 2026-09-11
**决策者**: AI 助手 + 用户确认

## 上下文

### 问题现象

JoinCode CLI 启动时出现 Console 交互卡死：

```
warn: JoinCode.App.Builder.EngineSessionFactory[0]
      未找到 PowerShell(pwsh)...
warn: Core.Configuration.ModelFetch.ModelListFetcher[0]
      [ModelListFetcher] 跳过 openai：未配置 API Key
...
是否信任此目录? (y/N): a    ← 用户输入 'a' 后卡死
```

### 根因分析

1. `EngineSessionFactory.CreateCoreAsync` 在第 125-126 行启动 **fire-and-forget 后台 Task**（`_ = Task.Run(...)`）拉取模型列表
2. 后台 Task 通过 `ILogger.LogWarning` 输出警告（`ModelListFetcher.cs:42` 等）
3. `ApplicationBuilder.cs:89` 配置了 `logging.AddConsole()`，logger 输出直接写到 `Console.Out`
4. 主线程在 `StartupWorkflow.CheckWorkspaceTrustAsync`（第 259 行）调用 `Console.ReadLine()` 阻塞等待用户输入
5. **后台 `Console.Out.WriteLine` 与主线程 `Console.ReadLine` 争用共享 Console 句柄** → 输出交错、光标跳转、严重时卡死

### 影响范围

所有交互式输入点（19 处 `Console.ReadLine` / `Console.ReadKey` 调用）都存在被后台异步输出打断的风险：
- 信任目录确认（`StartupWorkflow.cs:259`）
- Onboarding 流程（`StartupWorkflow.cs:75,148,164`）
- 供应商配置（`ProviderSetupStep.cs:67,100,111`）
- REPL 循环（`ReplLoopStep.cs:76`）
- ConsoleOutput / PhysicalConsoleOutput 的 Prompt/Confirm/ReadPassword

## 决策

**采用 Actor 模型将 Console I/O 串行化**，复用项目已有的 `ActorBase<TCommand, TOut>`（`foundation/AsyncLock`）。

### 架构：ConsoleActor

```
┌─────────────────────────────────────────────────────┐
│                    ConsoleActor                      │
│       (Channel<ConsoleCommand> + await foreach)      │
│                                                       │
│  所有命令 FIFO 串行执行，无竞态，无死锁               │
│  ┌───────────┐ ┌───────────┐ ┌───────────────────┐ │
│  │ WriteCmd  │ │ ReadLineCmd│ │ ReadKeyCmd        │ │
│  │→_realOut  │ │→Console    │ │→Console.ReadKey   │ │
│  │  .Write   │ │  .In.Read  │ │                   │ │
│  └───────────┘ └───────────┘ └───────────────────┘ │
└─────────────────────────────────────────────────────┘
      ↑                  ↑                ↑
      │                  │                │
  Console.Out        TerminalHelper    TerminalHelper
  (重定向)            .ReadLine()       .ReadKey()
      ↑
      │
  ILogger→AddConsole→Console.Out (自动经过 Actor)
```

### 核心机制

1. **ConsoleActor 继承 `ActorBase<ConsoleCommand, Unit>`** — 无界 Channel，FIFO 串行消费
2. **Console.Out 重定向到 ConsoleActorTextWriter** — 所有经过 `Console.Out` 的输出（包括 `ILogger→AddConsole`）自动经过 Actor 串行化
3. **ReadLine/ReadKey 显式经过 ConsoleActor** — `Console.In` 无法重定向，需显式发送命令到 Actor
4. **Actor 消费循环在独立线程** — `Task.Run(ConsumeLoopAsync)`，主线程 `tcs.Task.GetAwaiter().GetResult()` 阻塞等待，不死锁

### 串行化效果

```
时间轴 →

无 Actor（竞态）:
  主线程:  Write("提示") → ReadLine() [阻塞]
  后台:              Write("警告") ← 打断！交错！卡死！

有 Actor（串行）:
  Channel: [WriteCmd("提示")] → [ReadLineCmd] → [WriteCmd("警告")]
  Actor:   Write("提示") → ReadLine() [阻塞] → 用户输入完成 → Write("警告")
  ↑ 提示先输出，ReadLine 期间无输出交错，警告在 ReadLine 完成后才输出
```

## 考虑的替代方案

### 方案B: 锁（lock/SemaphoreSlim）保护 Console I/O

```csharp
private static readonly object _consoleLock = new();
public static string ReadLine() { lock (_consoleLock) return Console.ReadLine(); }
public static void WriteLine(string s) { lock (_consoleLock) Console.WriteLine(s); }
```

**否决理由**：
- 后台 logger 持锁输出时，主线程 Write 提示也阻塞，用户体验差
- 锁忘记加一处就漏防，无法强制
- 死锁风险（嵌套锁、锁+await）
- 不符合项目 Actor 模型架构方向（ADR 0074）

### 方案C: 推迟后台任务启动到信任确认之后

```csharp
// 在 ReplLoopStep 中启动 StartModelFetchBackground，而非 EngineSessionFactory
```

**否决理由**：
- 只解决信任确认一个点，其他 19 处 ReadLine 仍暴露
- 后台任务启动时机与业务逻辑耦合，违反关注点分离
- 后续新增后台任务仍可能踩坑

### 方案D: Logger 改为写文件/内存缓冲

**否决理由**：
- 丢失 Console 实时诊断能力
- 用户看不到启动警告
- 治标不治本，其他后台输出（如 REPL outputDisplayTask）仍可能竞态

## 实现细节

### 文件清单

| 文件 | 说明 |
|------|------|
| `app/JoinCode/Cli/Display/ConsoleActor.cs` | Actor 封装 Console I/O 命令 |
| `app/JoinCode/Cli/Display/ConsoleActorTextWriter.cs` | TextWriter 适配器，重定向 Console.Out |
| `app/JoinCode/Cli/Display/TerminalHelper.cs` | 接入 ConsoleActor，ReadLine/ReadKey 改为经过 Actor |

### 命令类型

```csharp
public abstract record ConsoleCommand;
public sealed record WriteLineCmd(string? Text) : ConsoleCommand;
public sealed record WriteRawCmd(string Text) : ConsoleCommand;
public sealed record ReadLineCmd(TaskCompletionSource<string> Reply) : ConsoleCommand;
public sealed record ReadKeyCmd(bool Intercept, TaskCompletionSource<ConsoleKeyInfo> Reply) : ConsoleCommand;
// ... ClearScreen / SetCursorPosition / Flush 等
```

### E2E 测试兼容

`Console.IsOutputRedirected` 时（E2E 管道场景）**不启用 Actor**，直接走原始 Console：
- 管道场景 In/Out 是不同句柄，无竞态
- 避免干扰 E2E 输出捕获

## 后果

### 正面
- **彻底消除竞态** — 所有 Console I/O 串行化，无论多少后台任务并发输出
- **架构统一** — 复用 ActorBase，与项目 Actor 模型方向一致（ADR 0074）
- **零侵入** — Console.Out 重定向对调用方透明，ILogger 自动受益
- **FIFO 保证** — Channel 保证输出顺序，不会乱序

### 负面
- **输出延迟** — Actor 消费循环在独立线程，输出有微小延迟（微秒级，可忽略）
- **ReadLine 期间输出排队** — 用户输入期间的后台输出在 Channel 排队，输入完成后才显示（比交错好）
- **同步阻塞** — ReadLine 用 `tcs.Task.GetAwaiter().GetResult()`，CLI 场景可接受（无 SynchronizationContext）

## 验证

- [x] 编译通过
- [x] 手动测试：启动 → 信任确认 → 后台警告不交错 → 用户输入正常
- [x] E2E 兼容：输出重定向场景不启用 Actor
