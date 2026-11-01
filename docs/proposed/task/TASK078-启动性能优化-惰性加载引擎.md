# TASK078 — 启动性能优化：惰性加载引擎 + 后台预热

## 背景

用户反馈 jcc 启动到首帧显示耗时约 3.6s，期望首帧在 200ms 内完成。计时探针结果：

```
t=0ms    RunInteractiveAsync 入口
t=3ms    WriteStartupLine 完成
t=3600ms inputPump 创建前         ← 瓶颈：3597ms 耗在这段
t=3621ms KeybindingLoader.Load 完成  (21ms)
t=4190ms transparencyEngine 创建完成  (569ms)
t=4190ms ReplShell 构造前
t=4250ms ReplShell 构造完成  (60ms)
```

瓶颈在 `WriteStartupLine(3ms) → inputPump 创建前(3600ms)` 之间的 3597ms。这段代码包括：
- `ConsoleSuppressionGate.Begin`
- `TerminalRestoreGuard`
- `EarlyInputBuffer.CaptureAvailable`
- `TerminalQuerier` 构造（终端探测）
- `EngineSession.CreateAsync()`（引擎会话创建 — 疑似主瓶颈）
- `SlashCommandSource.Load`
- `CommandPickerEngine` 构造
- `LoadThemeNameAsync` / `CommandUsageMemory.LoadAsync` / `LoadVimModeAsync` / `LoadStatusLineTemplateAsync` / `LoadSettingAsync` / `LoadGitignorePredicateAsync`

## 目标

- **首帧 < 200ms**：启动后 200ms 内渲染 TUI 外壳（banner + 输入框 + 空消息区）
- **后台预热**：首帧后 fire-and-forget 后台加载引擎
- **惰性等待**：用户第一次发消息时，若引擎已就绪直接用，未就绪显示 spinner 等待
- **三个功能正常**：选区高亮 + hover 高亮 + 点击进第二屏 不受影响

## 方案：后台预热 + 惰性等待（方案 A）

### 架构变更

1. **ReplShell 与引擎解耦** — ReplShell 构造不再强依赖 EngineSession
   - 引擎相关依赖改为可注入的"延迟接口"（`LazyEngineServices` / `AsyncLazyEngineServices`）
   - 首帧用"空实现/占位实现"构造 ReplShell，引擎就绪后替换为真实实现
2. **首帧最小集** — 只加载渲染必需的：
   - `ConsoleSuppressionGate.Begin` / `TerminalRestoreGuard`（安全必需）
   - `EarlyInputBuffer.CaptureAvailable`（捕获早期按键）
   - `TerminalQuerier`（终端探测 — 渲染依赖能力检测）
   - `inputPump`（输入接收）
   - `ReplShell`（用占位引擎服务构造）
3. **后台预热** — 首帧渲染后 fire-and-forget：
   - `EngineSession.CreateAsync()`
   - 各 `Load*Async` 调用
   - 引擎就绪后注入 ReplShell（替换占位实现）
4. **惰性等待** — 用户发消息时：
   - 引擎已就绪 → 直接用
   - 引擎未就绪 → 显示"正在准备…"spinner，await 引擎就绪后继续

## 瓶颈定位结果（步骤 1-2 完成）

细粒度探针数据（热启动）：

```
t=0ms    RunInteractiveAsync 入口
t=4ms    WriteStartupLine 完成
t=6ms    ConsoleSuppressionGate.Begin 完成      (2ms)
t=7ms    TerminalRestoreGuard 完成              (1ms)
t=8ms    EarlyInputBuffer.CaptureAvailable 完成 (1ms)
t=9ms    TerminalQuerier 构造完成               (1ms)
t=9ms    EngineSession.CreateAsync 前
t=967ms  EngineSession.CreateAsync 完成         ← 958ms 主瓶颈！
t=1002ms SlashCommandSource.Load 完成           (35ms)
t=1008ms LoadThemeNameAsync 完成                (6ms)
t=1020ms CommandUsageMemory.LoadAsync 完成      (12ms)
t=1025ms LoadVimModeAsync 完成                  (5ms)
t=1026ms LoadStatusLineTemplateAsync 完成       (1ms)
t=1028ms LoadSettingAsync(defaultShell) 完成    (2ms)
t=1031ms LoadGitignorePredicateAsync 完成       (3ms)
t=1032ms inputPump 创建前
t=1054ms KeybindingLoader.Load 完成             (22ms)
```

**结论**：
- **EngineSession.CreateAsync = 958ms** — 占总启动时间 91%，是唯一主瓶颈
- 其他 Load*Async 加起来仅 64ms — 并行加载收益有限
- 首帧必需的前置工作仅 9ms（WriteStartupLine + ConsoleSuppression + TerminalRestore + EarlyInput + TerminalQuerier）
- **方案确认**：把 EngineSession.CreateAsync 放后台预热，首帧可在 ~70ms 完成（9ms 前置 + 60ms ReplShell 构造）

**注**：之前测的 3600ms 可能是冷启动（DLL 未缓存），热启动 958ms。但即使 958ms 也远超 200ms 目标，惰性加载方案正确。

## 实现步骤

| 步骤 | 内容 | 状态 |
|------|------|------|
| 1 | 加细粒度探针定位 3597ms 瓶颈（EngineSession.CreateAsync / 各 Load*Async / TerminalQuerier 分别计时） | ✅ |
| 2 | 根据瓶颈结果确认方案（预期 EngineSession.CreateAsync 占大头） | ✅ |
| 3 | 设计 `AsyncLazyEngineServices` 延迟接口 + 占位实现 | ⏳ |
| 4 | 重构 ReplShell 构造：引擎相关依赖改为延迟接口 | ⏳ |
| 5 | 重构 Program.cs 启动流程：首帧最小集 + 后台预热 | ⏳ |
| 6 | 实现惰性等待：用户发消息时 await 引擎就绪 | ⏳ |
| 7 | 删除计时探针（临时调试代码） | ⏳ |
| 8 | 验证首帧速度 < 200ms | ⏳ |
| 9 | 验证三个功能正常（选区 + hover + 点击） | ⏳ |
| 10 | 验证引擎功能正常（对话 / 斜杠命令 / 主题切换等） | ⏳ |

## 四问回答

1. **专家隐性实践**：跨启动框架实测（VS Code / Claude Code / lazy load 模式）；查 .NET Lazy/AsyncLazy 模式；查基准 Claude Code 启动流程（ink.tsx 异步加载引擎 vs 先渲染 UI）；并发安全（后台加载线程 vs UI 渲染线程 vs 用户输入线程）
2. **原始素材清单**：当前 Program.cs 启动流程代码；ReplShell 构造参数清单；EngineSession.CreateAsync 源码；基准 Claude Code ink.tsx 启动流程；.NET LazyThreadSafetyMode 文档
3. **具体方法与工具**：计时探针定位瓶颈；单元测试验证延迟接口；手动 exe 验收首帧速度（ADR 0080）；跨功能验证（对话/命令/主题）
4. **独立验证来源**：计时探针数据 + 手动 exe 启动速度测量 + 三个功能手动验证 + 引擎功能回归验证

## 验收标准

| 标准 | 目标 | 验收方式 |
|------|------|----------|
| 首帧渲染 | < 200ms | 计时探针 + 手动 exe 秒表 |
| 引擎后台加载 | 不阻塞 UI | 首帧后 UI 可交互（输入框可输入） |
| 第一次对话 | 引擎已就绪则无感；未就绪则 spinner 等待 | 手动 exe 验证 |
| 选区高亮 | 正常工作 | 手动 exe 鼠标选区验证 |
| hover 高亮 | 正常工作 | 手动 exe 鼠标悬停验证 |
| 点击进第二屏 | 正常工作 | 手动 exe 点击 PromptContextItem 验证 |
| 引擎功能 | 对话/斜杠命令/主题切换正常 | 手动 exe 回归验证 |

## 风险

- **ReplShell 解耦改动面大** — ReplShell 构造有 30+ 参数依赖引擎服务，解耦需谨慎
- **并发安全** — 后台加载线程注入 ReplShell 时需线程安全
- **占位实现行为** — 引擎未就绪时用户操作（如斜杠命令）需合理降级
