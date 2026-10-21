# 0147. DSH 终端子系统（ConPTY 集成）

- 状态：proposed
- 日期：2026-10-22
- 决策者：项目架构组

## 背景

DSH 前端 sidebar-terminal 十方法（create/list/write/resize/rename/close/follow/retain/environment/shells）需要宿主提供真实 PTY。引擎现状：**无 ConPTY/PTY 任何能力**（2026-10-21 调研）。这是 P2 唯一全新子系统。

前端契约要点（typert 挖掘）：
- `WebTerminalInfo`：id/title/shell{path,args,name}/cwd/cols/rows/state(failed|running|exited)/exitCode/controllerId
- `terminal/follow` 流：snapshot{screen,sequence} + output{data} + state 帧（xterm 类渲染）
- `terminal/retain`：后台保留流
- shells/environment：探测可用 shell 与上限（maxInputBytes/maxCols/maxRows/scrollback）

## 决策（草案）

新建 `modules/perception/terminal/` 功能域（对齐 ADR 0137 就近隔离）：

1. **PTY 核心**：Windows ConPTY P/Invoke（`CreatePseudoConsole` API，AOT 安全无反射）；Linux 后续 unixpty（本仓库 Windows-only GUI 消费，Linux 为深化项）
2. **TerminalService**：终端生命周期（create/close/list/rename）+ 会话注册表（`~/.jcc/dsh_terminals.json` 元数据 + 运行态内存表）
3. **IO 泵**：ConPTY 双管道（input/output）→ Channel<byte[]>；follow 流消费 output Channel 推帧；resize 透传 `ResizePseudoConsole`
4. **宿主桥**：NativeGui.Core 加 `DshTerminalService`（RPC 十方法 + WS 流两端点），帧投影按前端契约
5. **安全**：shell 白名单（powershell/pwsh/cmd/git-bash 探测复用 `JCC_POWERSHELL_PATH` 先例）；命令执行走 guard 权限管道（首次 create 需 approval）

## 替代方案（考虑过但放弃）

| 方案 | 放弃原因 |
|------|----------|
| pipe 假终端（stdin/stdout 无 PTY） | 无屏幕缓冲/resize 语义，xterm 渲染失真——调研已判"语义差距大" |
| 集成第三方库（winpty/Pty.Net） | nuget AI 包禁令外仍有 AOT 兼容风险；ConPTY P/Invoke 面小可控 |
| 塞进 NativeGui.Core | 终端是引擎域能力（CLI/TUI 亦可消费），非 GUI 私有 |

## 开放问题（实施前须拍板）

1. scrollback 上限与内存策略（前端期望值域未定标）
2. 多终端并发上限（每会话 N 个？）
3. 退出态 terminal 的 follow 回放（retain 语义）是否持久化 output 流

## 关联

- 上游：ADR 0143（DSH 托管）、ADR 0137（功能域就近隔离）
- 消费：DSH sidebar-terminal 面板
