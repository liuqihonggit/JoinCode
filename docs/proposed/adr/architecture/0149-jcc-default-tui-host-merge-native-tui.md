# ADR 0149: jcc 默认 TUI 宿主 — 吞并 NativeTui.Host + 删 CLI REPL

- 状态：proposed
- 日期：2026-10-25
- 关联：supersede ADR 0143（多宿主平等）、回到 ADR 0142 方向（jcc 宿主内嵌 TUI）、ADR 0140（native_tui 独立性，将破坏）

## 背景与问题

ADR 0143（2026-10-20 accepted）确立"多宿主平等"架构：jcc.exe（CLI）+ NativeTui.Host.exe（TUI）+ NativeGui.Host.exe（GUI）三个 Exe 平等引用 JoinCode.Engine。当时明确放弃 ADR 0142 方案 C（jcc 内嵌 `--tui`），理由是"用户意图是多宿主平等共享协议层，非单宿主内嵌"。

5 天后（2026-10-25）用户改变需求：

> jcc 吞并 tui，但是我要求是默认就是 tui，不要 cli，为什么呢？因为这样用户友好，
> 直接走入 LLM 的话有 -p 模式。

核心诉求：
1. **减少 exe 数量** — 不要搞那么多 exe
2. **默认 TUI** — 无参数启动即进 TUI（用户友好）
3. **`-p` 非交互** — 管道/脚本用途已由 `-p` 满足（`CliArg.cs:21`，`jcc -p "解释这段代码"`）
4. **删 CLI 交互 REPL** — ReplLoopStep 纯 Console REPL 不再需要
5. **保留 13 个子命令** — `jcc rg`/`gh`/`mcp_call`/`schema` 等工具型子命令保留

## 决策

**jcc.exe 成为唯一 CLI/TUI 宿主，默认启动 TUI**。

### 入口分流（jcc Program.cs）

| 输入 | 路由 | 说明 |
|------|------|------|
| `jcc`（无参数） | TUI | 默认进 TUI（搬自 NativeTui.Host.RunInteractiveAsync） |
| `jcc -p "..."` | 非交互 | NonInteractiveModeRunner（已存在，保留） |
| `jcc <子命令>` | 子命令路由 | FlatSubCommandRouter（13 个子命令，保留） |
| `jcc --selftest*` | TUI selftest | 搬自 NativeTui.Host 的 selftest 系列 |

### 删除项

- `NativeTui.Host.exe`（csproj 改为 lib 或移除，TUI 逻辑搬进 jcc）
- `InteractiveModeRunner` + `ReplLoopStep` + CLI REPL 相关中间件（纯 Console REPL 删除）
- `native_tui/apps/NativeTui.Host/` 的入口 Program.cs（TUI 启动逻辑搬进 jcc）

### 保留项

- `JoinCode.Engine`（引擎库，jcc 已引用）
- `NativeTui.Core` + `NativeTui.Markdown`（TUI 渲染框架，jcc 新增 ProjectReference）
- `NativeTui.Host` 的 `EngineSession`/`EngineTurnClient`/`ReplShell` 等 TUI 组装逻辑（搬进 jcc 或转为 jcc 引用）
- 13 个子命令 + `NonInteractiveModeRunner`（`-p` 非交互）
- `NativeGui.Host.exe`（GUI 宿主，WinExe + Avalonia + WebView2，无法合并，保持独立）

### Console.Out 冲突处理

NativeTui.Host 启动时把 `Console.Out` 重定向到 stderr（`Program.cs:283`，防引擎污染 TUI 渲染流）。jcc CLI 用 `Console.Out` 输出。合并后：TUI 分支在启动 TUI 前重定向，非交互/子命令分支不重定向。

## 理由

1. **用户友好** — 默认 TUI 是主交互入口，符合"打开即用"心智
2. **减少 exe** — 从 3 exe 降到 2 exe（jcc + NativeGui.Host），GUI 因 WinExe + net10.0-windows + Avalonia + WebView2 技术上无法合并
3. **`-p` 已满足非交互** — 管道/脚本场景已有成熟方案，无需 CLI REPL
4. **子命令是工具型** — `jcc rg`/`gh`/`mcp_call` 不依赖 CLI REPL，独立可用

## 代价与风险

| 代价 | 说明 |
|------|------|
| 推翻 ADR 0143 | 多宿主平等 → 单宿主内嵌，ADR 0143 状态改 superseded by 0149 |
| 破坏 native_tui 独立性（ADR 0140） | native_tui 不再可整体搬离仓库独立编译（jcc 引用 NativeTui.Core/Markdown） |
| NativeGui.Host 仍独立 | 合并后 2 exe，非 1 exe；GUI 宿主失去与 TUI 平等地位 |
| TUI 组装逻辑搬移 | EngineSession 等 14 个文件搬进 jcc 或转引用，需处理命名空间 + InternalsVisibleTo |
| Console.Out 重定向分流 | 需在入口分流前处理，避免 TUI 渲染污染 |

## 替代方案（考虑过但放弃）

- **保持 ADR 0143** — 多宿主平等，3 exe。放弃原因：用户明确要求合并 + 默认 TUI
- **NativeTui.Host 吞并 jcc** — 放弃原因：jcc 的 13 个子命令依赖 CLI 专属设施（FlatSubCommandRouter/管道/4 源码生成器），搬移量大且破坏 native_tui 独立性更甚
- **新建第三个 exe 吞并两者** — 放弃原因：不解决"不要搞那么多 exe"诉求

## 实施路径（渐进式）

1. jcc csproj 新增 ProjectReference NativeTui.Core/Markdown
2. 搬 NativeTui.Host 的 TUI 启动逻辑（EngineSession/ReplShell 等）进 jcc 可达位置
3. jcc Program.cs 入口分流：默认 → TUI，`-p` → 非交互，子命令 → 路由
4. 处理 Console.Out 重定向分流
5. 删 InteractiveModeRunner + ReplLoopStep + CLI REPL 中间件
6. NativeTui.Host.csproj 改为 lib 或移除（selftest 测试迁至 jcc 或 host.tests）
7. 编译 + 测试 + 手动 exe 验收（jcc 默认进 TUI）
8. ADR 0143 状态改 superseded by 0149，本 ADR 状态改 accepted
