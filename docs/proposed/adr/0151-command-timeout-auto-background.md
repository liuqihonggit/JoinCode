# ADR 0151: 命令超时自动转后台

> 📍 **导航**: [docs/](../../READMEB.md) › [adr/](../README.md)
> 🔗 **上游索引**: [adr/README.md](../README.md) — 修改本文档后须同步E同步更新此索引

> 状态: accepted（2026-10-28 落地验证：编译通过 + jcc.exe 运行确认）
> 创建: 2026-10-28
> 决策者: 用户主导（长时间命令避免主代理和子代理卡死等候）
> 影响: modules/apps/cli/core/services/CliSession.cs（命令执行主入口）
> 关联: [ADR 0106](../implemented/adr/data_core_selection/0106-subagent-stall-defense-in-depth.md)（子代理卡死防护）、[ADR 0150](0150-bridge-serve-protocol-reliability-guarantees.md)（协议可靠性）

## 背景

<Background>

斜杠命令（如 /code-index）可能长时间执行（构建索引、分析代码库），阻塞主代理和子代理等候。用户期望长时间命令自动切换到后台，避免卡死。

当前状态：
- /code-index 已改为后台执行（手动标记）
- 其他长时间命令（/analyze、/review、/security-review）仍阻塞执行
- 无通用"超时自动转后台"机制

## 决策

在命令执行主入口 `CliSession.HandleCommandAsync` 加 5s 超时竞赛：

1. **非交互模式（-p）**：同步执行（进程执行完即退出，后台任务来不及完成）
2. **交互模式（TUI）**：`Task.WhenAny(execTask, Task.Delay(5s))` 竞赛
   - 5s 内完成 → 正常返回
   - 5s 超时 → 输出"命令执行超过 5s，已转后台 (任务ID: xxx)"，命令在后台继续执行
   - 后台完成后通过 `originalOut.WriteLine` 输出结果到原始终端

3. **阈值**：5s（平衡用户感知与误判 — 大多数命令 <1s，5s 足时几乎一定是长时间操作）

## 替代方案

1. **特性标记（[LongRunning]）** — 需要逐个命令标记，维护成本高；弃
2. **基类继承（BackgroundCommandBase）** — 需要改命令继承链，侵入性大；弃
3. **不转后台（维持现状）** — 长时间命令阻塞终端，主代理卡死；弃

## 后果

### 正面
- 所有长时间命令自动转后台，无需逐个标记
- 主代理和子代理不再卡死等候
- 用户可以继续操作，后台完成后通知

### 负面
- 5s 内完成的命令不受影响（正常返回）
- 5s 超时的命令输出分两部分：立即返回"已转后台" + 后台完成后"完成"
- 后台任务用 `CancellationToken.None`（命令返回后 context.CancellationToken 可能取消）

### 边界
- 非交互模式（-p）不转后台（进程执行完即退出）
- 后台任务异常被捕获并输出到原始终端
- 后台任务输出用 `originalOut` 直接写入，不受 `TerminalHelper.SetOut` 重定向影响
