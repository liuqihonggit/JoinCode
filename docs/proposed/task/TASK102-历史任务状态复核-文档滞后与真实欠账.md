# TASK102 — 历史任务状态复核（文档滞后 vs 真实欠账）

> 状态：accepted（复核完成；结论已落档，真实欠账保留在原文档待办）
> 复核日期：2026-11-21
> **回填勾选（2026-11-21）**：三份文档滞后文档（TASK077/081/049）已全部勾上；TASK072 七项真欠账于同日施工修复并全勾（构造注入，零锁原语，31/31 回归绿）；TASK023 四项经二次深查后于同日全部结案并全勾——见 §1.4 二次修订。
> 起因：用户问「还有什么东西没有完成」。扫全部任务文档的 `- [ ]` 后发现 6 份有未勾项，
> 但 TASK068 的先例证明**文档开头的复选框会撒谎** —— 必须代码符号 + commit 双验证，不能只读文档。
> 方法：每份挑最有辨识度的符号 `grep` 生产代码，再用 `git log --grep` / `git cat-file -t` 核实 commit。

---

## 0. 一句话结论

**5 份复核文档里，3 份是文档滞后（实际做完忘了勾），1 份部分完成，1 份真未完成。**
「未勾复选框」不等于「欠账」——这个仓的文档在施工后长期不回头更新状态，
所以判据必须是**代码消费方 + commit**，不是文档开头的清单。

| 文档 | 文档开头说 | 代码 + commit 实况 | 判定 |
|---|---|---|---|
| TASK077 ReadConsoleInput pump | 6 项未勾 | `Win32ConsoleInputPump : IInputPump` + `Drain()` + 鼠标武装 `EnableMouseInput` + `Win32ConsoleInputPumpTests.cs` 全在 | ❌ 文档滞后，**实际完成** |
| TASK081 CS 闭环动态测试 | 21 项未勾 | `e76f4949a` = "TASK081 全部完成 40/40"；`test/mock/mock_server.core/` 全套 MockServer 源码 + 校验脚本归档 | ❌ 文档滞后，**实际完成** |
| TASK049 基建确定性测试 | 状态「待执行」 | `modules/collections/structura_tests/DagTests.cs` 里 `TopologicalSort`/`HasCycle`/`FindAllCycles`/`WouldCreateCycle` 测试齐全；commit `07db686cd`/`012dadad2`/`fc8bc65fd` 三批 | ❌ 文档滞后，**实际完成** |
| TASK023 总测试任务清单 | 7 项未勾 | 二次深查纠正：脚本早已存在且 CI 全量跑（原判「脚本不存在」是只看了巡检脚本的误判）；16/20 降级命令无参数属合理；真欠账仅 `--debuglog` 步骤追踪（已改用 JCC_DEBUGLOG=1）+ 副作用验证列 | ✅ **全部结案**（2026-11-21 二次修订，见 §1.4） |
| TASK072 巡检行为验证升级 | 7 项未勾 | 复核时确为真欠账：`02cc5a9d2` 只做 `ToolPatrolEngine`、git_*/mcp_connect 全跳过 | ✅ **2026-11-21 已施工完成**：构造注入（零全局环境变量/零锁）+ 七项全勾，全量并行回归 31/31 绿（详见 TASK072 施工记录） |

---

## 1. 逐份取证

### 1.1 TASK077（文档滞后，实际完成）

- `native_tui/src/NativeTui.Core/repl/input/Win32ConsoleInputPump.cs:12` — `public sealed class Win32ConsoleInputPump : IInputPump, IDisposable`
- 同文件 `:35` — `public bool Drain(Action<KeyPress> dispatch)`，非阻塞排空已实现
- 同文件 `:96-99` — `MOUSE_EVENT` → `KeyPress(KeyKind.Mouse, ...)` 包装
- `native_tui/src/NativeTui.Core/repl/input/Win32InputPump.cs:16,38` — `EnableMouseInput = 0x0010`，`SetConsoleMode(... | EnableMouseInput ...)`
- `native_tui/apps/NativeTui.Host/Program.cs` — `IInputPump` / `RawStdinPump` 装配；Linux 仍走 `RawStdinPump`
- 测试：`native_tui/tests/NativeTui.Tests/repl/input/Win32ConsoleInputPumpTests.cs`、`terminal/Win32ConsoleInputTests.cs`
- 全仓编译与测试绿：TASK097/TASK100 各轮 `Build-AllDomains.ps1 -Changed` 与 native_tui 测试均覆盖

### 1.2 TASK081（文档滞后，实际完成）

- commit `e76f4949a`：**"test: TASK081 全部完成 — 40/40 测试通过 + 校验脚本归档 (#607)"**
- 另有 `304165d9c`（4 个客户端行为 bug）、`e263bc337`（result usage / 多 tool_use 合并）、`cf1ab85fd`（QueryEngine 三 bug）
- `test/mock/mock_server.core/`：`KestrelMockServer.cs`、`HttpListenerMockServer.cs`、`MockServerController.cs`、`MockServerJsonContext.cs`
- `test/mock/mcp.mock_server/engine/McpMockServerEngine.cs`
- 说明：0.x 脚手架与 1.x–5.x 的 40 项测试均已落地，文档 21 个复选框是施工前的原计划，未回填

### 1.3 TASK049（文档滞后，实际完成）

- `modules/collections/structura_tests/DagTests.cs:190` 起：`TopologicalSort_Empty/SingleNode/IsolatedNodes/Chain/Diamond`
- `DagAlgorithmsTests.cs`、`ConcurrentDagTests.cs`、`ImmutableDagTests.cs` 等配套
- commit 三批：`07db686cd`（基建确定性测试补充 1379 测试 + 12 bug）、`012dadad2`（SwissTable/DAG/HamT 拆分）、`fc8bc65fd`（阶段3，9 模块 227 测试 + 1 bug）
- 文档状态「待执行」是 2026-09-29 创建时的快照，施工后从未更新

### 1.4 TASK023（2026-11-21 二次深查后全部结案 ✅）

> **二次修订**：下方「未做 4 项」是初判，经代码 + 真机二次取证后纠正——其中 2 项是误判/伪目标、
> 2 项是真欠账且已修复。以本段修订为准，原初判保留作历史。

**二次取证结论（取代下方初判）**：
- 真机数字：368 MCP 工具 / 85 斜杠命令（原 382/97 是旧快照）。
- ① **非缺陷**：85 命令中 65 有结构化 schema、19 本就无参数（`ExecuteAsync` 不读 Arguments，降级合理）、
  仅 `profile` 用位置参数串 `GetNormalizedArgs`（强加 `[ChatCommandArg]` 会破坏行为，ArgumentHint 属有意设计）。
  无「该结构化却降级」的欠账；改完成标准措辞后结案。
- ② **初判误判**：全参数脚本 `Invoke-AllCommandTests.ps1` 早已存在（commit `5ede42b26`），CI pwsh 每天全量跑；
  初判只看了 `e2e_tool_patrol.py`（TASK072 巡检脚本）才判「脚本不存在」。真机 `mcp_schema read` 返回 4 参数，链路可达。
- ③ **真欠账，已修**：`--debuglog` flag 在解析层缺失（真机任意位置 stderr 零诊断），脚本改用
  `JCC_DEBUGLOG=1` 环境变量 + `Extract-Steps` 抓真实诊断前缀（`[STEP]` 在直调路径永不产出）。旧逻辑步骤列永远 no steps。
- ④ **部分完成，已补齐**：报告原有工具/参数/状态/耗时/步骤/退出原因，缺副作用验证列；已补
  `Get-SideEffectVerdict`（读=状态验证、写=如实标注未验证副作用，不假绿）。

原初判（保留作历史，已被上方取代）：

已做：
- commit `5ede42b26` —「命令全参数填充测试体系（slash_call 改造 + 76 命令 schema + 每日 CI 脚本）」
- `ChatCommandArg` / `SlashCommandSchemaCatalog` 已存在
- `slash_call` 的 JSON/参数归一化（`GetNormalizedArgs` / `GetSplitArgs`）已做

已做（2026-11-21 回填勾选 3 项）：多参数传递 / 全参数填充 / 前置交付（前置 1+2 已补齐）。

未做（真欠账 4 项，保留在原文档）：
- 97 命令**全部**结构化 `[ChatCommandArg]`、`ArgumentHint` 降级清零 —— `CmdToolDef.cs:49-51` 与 `ChatCommandAttribute.cs:23` 的降级逻辑仍在
- 脚本覆盖全部 382 MCP 工具 + 97 斜杠命令 —— 现有 `e2e_tool_patrol.py:91-102` 只写巡检统计 OK/ERROR/CRASH/TIMEOUT，不是全参数覆盖
- `--debuglog` 步骤追踪输出到报告（定位「条件不满足提前退出」）—— 脚本目录未找到对应实现
- Markdown 全参数总报告（工具/参数/ok/步骤/退出原因/副作用验证）—— 依赖上一项，尚无对应实现

### 1.5 TASK072（真未完成，7 项全部保留）

- commit `02cc5a9d2` 只交付了**巡检引擎本体**（`ToolPatrolEngine.cs` / `ToolPatrolService.cs` / `e2e_tool_patrol.py`），
  与文档里的「按工具名+参数语义构造」是两回事
- `McpToolSmokeTests.cs:65` 仍是 `BuildMinimalArguments(ToolInfo tool)` —— 未替换为语义构造器
- `McpToolSmokeTests.cs:28` 临时目录是 `jcc-test-{Guid}`，不是文档要求的 `patrol-{Guid:N}`；无 try-finally 清理
- `ToolsWithGitSideEffects` / `ToolsWithNetworkSideEffects` / `gh_` / `git_` 全跳过（`:131-166`），14 个 git_/worktree 与 mcp_connect 未用临时仓库/mock 服务器验证
- 无 `ArgumentBuilder`、无 `MockMcp` 符号

---

## 2. 处置

- **文档滞后（TASK077/081/049）**：在原文档顶部补状态澄清框，按证据勾选/标注「已完成」，避免后来人只读开头误判。
- **TASK023**：2026-11-21 二次深查后全部结案、7 项全勾——2 项初判（全结构化/脚本不存在）经取证为伪目标与误判，2 项真欠账（debuglog 步骤追踪、副作用验证列）已改脚本并真机验证；详见 §1.4 二次修订。
- **真未完成（TASK072）**：复核时 7 项保留；**2026-11-21 已施工完成并全勾**（构造注入 + 替身注入，零锁/零邮箱改动），详见 TASK072「施工记录」与全量回归 31/31。

## 3. 可复用的判据

1. 看到 `- [ ]` 先别信 → `grep -rln <最有辨识度的符号> modules/ test/ native_tui/ --include=*.cs`（生产代码，不看 artifacts）
2. `git log --all --grep="<TASK号\|关键字>"` 找完工 commit；`git cat-file -t <hash>` 确认是真 commit
3. 配置类还要追「消费方」：DTO 字段存在 ≠ 行为接上（TASK068 的 actor 就是配置已映射但全仓零读 `config.Actor.Backpressure`）
4. 巡检/测试类要追「跳过表」：脚本存在 ≠ 覆盖全量（`ToolsWithGitSideEffects` 全跳过就是反例）
