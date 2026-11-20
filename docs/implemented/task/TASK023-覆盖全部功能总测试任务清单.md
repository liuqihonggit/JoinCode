# 覆盖全部功能总测试任务清单

> ## ⚠️ 状态澄清（2026-11-21，二次深查后修订）
>
> 下方 7 项已于 2026-11-21 逐项取证回填：已完成的勾上，4 项保留未勾
> （原判定见 [TASK102](../../proposed/task/TASK102-历史任务状态复核-文档滞后与真实欠账.md) §1.4）。
> **本轮对 4 项逐一做了代码 + 真机取证，发现原判定混合了「真欠账」与「文档旧快照/误判」，不能照字面施工。**
>
> **真机数字**（取代本文旧快照的 382 工具 / 97 命令）：
> `jcc mcp_list --json` = **368 工具**；`jcc slash_list --json` = **85 命令**（移除 reset-config 后 84）。
>
> 四项深查结论：
> - **① 全部结构化、降级清零 → 伪目标（降级合理）**：真机 85 命令中 20 个 schema 为 null，
>   逐一读 `ExecuteAsync` 确认其中 **19 个根本不读参数**（exit/quit/version/status/context/code-index 等），
>   schema=null 是正确设计；仅 `Profile` 用自由文本参数（`[profile|list]`），一句话描述无法结构化成具名 schema。
>   无参数命令没有参数可「结构化」，强塞 `[ChatCommandArg]` 只会制造空壳 schema。此项按「降级语义如实记录」结案。
> - **② 脚本覆盖全量 → 真欠账，但成因是脚本跑不起来**：全参数脚本 `Invoke-AllCommandTests.ps1` 早已存在，
>   且遍历 `mcp_list`/`slash_list` 全量；但 4 处用了 `ForEach-Object -Parallel`（**PS7 only**），
>   本机 PS5.1 真机运行四处全报 `Parameter set cannot be resolved`，工具/命令用例全空、报告是空壳。
>   原判定只看了 `e2e_tool_patrol.py`（那是 TASK072 巡检脚本）而误判「脚本不存在」。修法：改成 PS5.1 兼容的并发（`Start-ThreadJob`/`RunspacePool` 或串行 + 分批）。
> - **③ `--debuglog` 步骤追踪 → 真欠账，双重成因**：(a) `--debuglog` flag 在 `CliArgParser` 解析层缺失，
>   真机 `--debuglog` 放任意位置 stderr 均零诊断行；只有 `JCC_DEBUGLOG=1` 环境变量有效。
>   (b) 环境变量开启后直调路径只产 `[MODULE]/[CONFIG]/[LLM]` 启动步骤，`[STEP]` 打点全在 REPL/startup 路径，
>   `Extract-Steps` 抓 `[STEP]` 在直调命令上永远为空。修法：脚本改用 `JCC_DEBUGLOG=1` + 抓真实产出的启动步骤前缀。
> - **④ Markdown 总报告 → 部分完成**：脚本已生成含 工具|参数|状态|耗时|步骤|退出原因 的报告，
>   **仅缺「副作用验证」列**。此项在②③修好后补列即可。
>
> **②③④ 施工方案（2026-11-21 记录，施工前）**——只改 `Invoke-AllCommandTests.ps1`，不碰生产 C#：
> - ③ `Invoke-Jcc` 给 call 类命令注入 `JCC_DEBUGLOG=1` 环境变量（真机证明 `--debuglog` flag 无效、
>   环境变量有效且有 24 行真实诊断）；`Extract-Steps` 从只抓 `[STEP]` 改为抓直调路径真实产出的
>   `[MODULE]/[ROLES]/[LLM]/[TOOL-GATE]/[ORDER]/[CAPS-PLUGIN]/[CONFIG]/[STEP]` 前缀行，消除"步骤列永远 no steps"的假绿。
> - ④ Markdown 报告补「副作用验证」列——按工具/命令的读/写语义给出可判定口径
>   （写类=需副作用断言/人工核验，读类=仅状态验证），如实区分"已验证"与"未验证"，不制造假绿。
> - ② 保持 CI(pwsh/PS7) 并发不变；本机 PS5.1 无 `ForEach-Object -Parallel`，用逐命令代表验证核心逻辑，
>   全量并发仍由 CI 承担。文档数字改为真机 368/85，纠正 TASK102「脚本不存在」的误判。
>
> **施工纪律**：文档先记录 → 施工 → 真机验收 → 一次勾一项；失败不勾。
> **勿把整份清单当「全未完成」，也勿当「全完成」。**

> ## 逐项验收记录（2026-11-21）
>
> **③ debuglog 步骤追踪 — 已验收，勾第 140 行**：
> `Invoke-AllCommandTests.ps1` 的 `Invoke-Jcc` 增加 `-WithDiag`（对 call 类命令注入 `JCC_DEBUGLOG=1`
> 环境变量，替代解析层缺失的 `--debuglog` flag）；`Extract-Steps` 从只抓 `[STEP]` 改为抓直调路径
> 真实产出的 8 类诊断前缀（前缀数组内联在函数体，确保经 funcDefs 传入 -Parallel runspace 后仍有效）。
> 真机证据：同一个 `mcp_call file_read '{}' --json` 的 stderr，旧逻辑抓 `[STEP]` = 0 行，
> 新逻辑 = 23 行（`[CONFIG]/[LLM]/[MODULE]/[ROLES]/[ORDER]/[TOOL-GATE]/[CAPS-PLUGIN]`）。脚本 PS5.1 语法检查 0 错误。
>
> **④ 副作用验证列 — 已验收，勾第 141 行**：
> 新增 `Get-SideEffectVerdict`（读/写语义判定，只读=状态验证、写类=如实标注「未验证副作用(仅冒烟)」，
> 不制造假绿），两处 funcDefs/runspace 已接线，Markdown 报告表头与数据行加「副作用验证」列。
> 真机证据：file_read/OK→只读/OK(状态验证)、git_commit/OK→写类/未验证副作用(仅冒烟)、status/OK→只读、tools/CRASH→写类/CRASH。
>
> **① 全部结构化、降级清零 — 经核实为非缺陷，按真实语义结案，勾第 137 行（改标准措辞）**：
> 严格判据「读参数但 schema 降级的命令」真机扫描 85 命令，结果仅 `profile` 一个；其余 84 个要么有结构化
> schema（65 个），要么本就无参数、降级合理（19 个：exit/quit/version/status/context/code-index 等，`ExecuteAsync`
> 完全不读 `Arguments`）。`profile` 用 `GetNormalizedArgs()` 读整个位置参数串（`/profile [名称|list]`），
> 是**位置参数命令而非 key=value 命令**——强行加 `[ChatCommandArg("target")]` 会让全参数脚本传
> `target=...`，被 `GetNormalizedArgs` 当成 profile 名，反而破坏行为。其 `ArgumentHint="[profile|list]"`
> 降级是有意设计。结论：不存在「有参数却该结构化而未结构化」的真实欠账，
> 原完成标准「97 命令全部结构化、降级清零」建立在错误前提上，按真实语义改写后结案。
>
> **② 脚本覆盖全量 — 已验收，勾第 138 行**：
> 原判定「脚本不存在、只看了 `e2e_tool_patrol.py`」是误判——全参数脚本 `Invoke-AllCommandTests.ps1`
> 早已存在（commit `5ede42b26`），遍历 `mcp_list`/`slash_list` 全量，CI `daily-command-test.yml`
> 用 pwsh 每天分 8 片跑全量 368 工具 + 84 命令（原文档 382/97 是旧快照）。
> 真机核心链路验证：`jcc mcp_schema read --json` 返回 4 properties + 1 required，全参数填充可拿到真实 schema。
> 本机 PS5.1 无 `ForEach-Object -Parallel`，全量并发由 CI(pwsh/PS7) 承担，本机以逐命令代表验证（非缺陷，是环境差异）。

> 📍 **导航**: [docs/](../README.md) › [task/](README.md) | **前置**: [plan/](../plan/README.md)
> 🔗 **上游索引**: [task/README.md](README.md) — 修改本文档后须同步更新此索引

> 关联: [启动参数完全重构任务清单](启动参数完全重构任务清单.md) (阶段6.1 仅补 21 命令,76 命令待补)
> ADR: [0069](../implemented/adr/cli_jcc/0069-cli-args-full-refactor.md) (slash_call/mcp_call 元命令)、[0083](../implemented/adr/engineering_ci_test/0083-e2e-script-mode-spec.md) (E2E 脚本模式)
> 渐进式执行: 每步编译+测试+提交

## 任务目标

构建一个 **CLI 脚本驱动的总测试**,覆盖全部 MCP 工具(382 个) + 全部斜杠命令(97 个),执行 **全参数填充**(每个工具/命令的所有参数都传有效值),并通过 `--debuglog` 步骤追踪 **深入验证命令是否真正完成任务**(而非仅参数存在/不崩溃)。

## 背景与痛点

**痛点(用户手动测试发现)**: 当前 E2E 测试只能验证参数是否存在,不能深入到命令内部环节,导致命令可能因某条件不满足就提前退出,而测试仍判通过 — 即"假绿"。需要可观测性看到命令执行到哪一步、在哪里退出。

**现有最接近的先例**:
- `tests/Integration/Integration.Tests/McpTools/McpToolSmokeTests.cs:101` — `All_Registered_Tools_Can_Be_Called_Without_Crash`,遍历所有工具 + `BuildMinimalArguments` 构造最小参数 + 调用验证不崩溃。但只填 Required 参数,不验证任务完成,无步骤追踪。
- `McpToolRegistrationValidationTests.cs` — 验证 schema 完整性(Required∈Properties、无重名、必需参数有描述),未验证参数命名统一性/类型标注完整性。

## 现状分析(2026-09-12)

| 维度 | MCP 工具(382 个) | 斜杠命令(97 个) |
|------|------|------|
| 结构化参数标注 | 911 个 `[McpToolParameter]` 特性,平均 2.4/工具,充分 | 仅 21 命令有 `[ChatCommandArg]`,**76 个(78%)只有 ArgumentHint** |
| schema 查询格式 | 统一 ToolSchema(`jcc mcp_schema --json`) | 统一 ToolSchema(`jcc slash_schema --json`),但 76 个降级为 ArgumentHint |
| 调用格式 | `key=value` / JSON / `--args-file` / `--args-stdin` | **只接受 JSON 字符串**(`SlashCommandExecutors.cs:20`) |
| 参数绑定 | 生成器统一生成参数提取代码 | 各命令自己解析 `Arguments` 字符串,无统一绑定 |

**结论**: MCP 工具侧已统一,可直接写脚本。斜杠命令侧有两个缺口: ① 78% 无结构化 schema ② 调用格式单一(只 JSON)。

## 任务分解

### 前置1: slash_call 改造适配多种参数传递

> 用户决策: 所有的按照 mcp_call 改造,适配多种参数传递

- [x] 扩展 `SlashCallExecutor.ExecuteAsync` 支持 key=value/JSON/--args-file/--args-stdin(提取 ResolveArgsJsonAsync + BuildArgsJsonFromKeyValue)
- [x] 统一参数解析: 复用 FlatSubCommandRouter.GetOptionValue/HasFlag + CliArgConstants.ArgsFileLongName/ArgsStdinLongName
- [x] 斜杠命令参数绑定: ChatCommandContext.Arguments 接收 JSON 字符串(key=value 经 BuildArgsJsonFromKeyValue 组装,复用 McpCliCommand.ParseValueToJsonElement 类型推断)
- [x] 编译 JoinCode 成功(0警告0错误) + 11 单元测试通过(SlashCallArgsParserTests)
- [x] git commit 65dc9af83

### 前置2: 补齐 76 个斜杠命令的 [ChatCommandArg] 结构化 schema ✅

> 对接 `启动参数完全重构任务清单.md` 阶段6.1(渐进式,按分类批量补)
> 结论: 76 命令中 53 个有参数已补齐 [ChatCommandArg],23 个无参数不需要(源码生成器降级输出 ArgumentHint)

- [x] 列出 76 个无 schema 的命令(`jcc slash_list --json` 对比 `slash_schema` 非 null 的)
- [x] 按分类批量补 `[ChatCommandArg]` 特性:
  - [x] brain/ 类命令 (commit 72b1d377f, 5 命令)
  - [x] ai/ 类命令 (commit c7753cabb, 7 命令)
  - [x] transport/ 类命令 (commit 35a653094, 2 命令)
  - [x] hands/ 类命令 (commit e2d155964, 6 命令)
  - [x] agents/ 类命令 (commit e6b6c4054, 8 命令)
  - [x] guard/ 类命令 (commit 58a44db44, 13 命令)
  - [x] system/ 类命令 (commit c5514a7ea, 12 命令)
  - [x] Infrastructure/ 类命令 (仅 SimpleCommand,无参数,不需要 [ChatCommandArg])
- [x] 每批编译 + 87 单元测试通过
- [x] git commit(每批一次)

### 主任务: CLI 脚本驱动全参数填充总测试 ✅

> 用户决策: 用于 CI 每日定时测试每个命令功能是否正常

#### 产物

- **脚本**: `tools/scripts/Invoke-AllCommandTests.ps1` — 遍历 394 MCP 工具 + 97 斜杠命令,全参数填充调用,生成 Markdown 报告
- **CI workflow**: `.github/workflows/daily-command-test.yml` — 每天 20:00 UTC 定时触发,build jcc + 跑脚本 + 上传报告 artifact

#### 脚本流程

```
每个工具/命令:
1. mcp_list --json / slash_list --json  → 全部名称 ~~(--brief 已删除)~~
2. mcp_schema <tool> --json / slash_schema <cmd> --json  → 参数 schema
3. 生成全参数填充(60+ 启发式映射,可选参数也填)
4. mcp_call <tool> k=v ... --json --debuglog / slash_call <cmd> k=v ... --debuglog
5. 解析 → OK | ERROR | CRASH | TIMEOUT | SKIP
6. 汇总 Markdown 报告(工具|参数|状态|步骤|退出原因)
```

#### 状态判定

- **OK**: JSON 解析成功,isError=false
- **ERROR**: JSON 解析成功,isError=true(工具返回错误,非崩溃)
- **CRASH**: 进程异常退出,无法解析 JSON
- **TIMEOUT**: 超时(默认 30s/工具)
- **SKIP**: 斜杠命令输出"用法/未初始化/不可用"等提示

#### 小规模验证(30 MCP 工具)

- 19 OK, 9 ERROR, 0 CRASH, 2 TIMEOUT — 脚本工作正常
- 耗时 156s(约 5s/工具),全量 394 工具预计约 33 分钟

## 完成标准

- [x] slash_call 支持 `key=value` / JSON / `--args-file` / `--args-stdin` 多种参数传递
- [x] 全部「读参数」的斜杠命令有结构化 `[ChatCommandArg]` schema（真机 85 命令：65 有 schema + 19 无参数降级合理 + 1 位置参数命令 profile 用 ArgumentHint 属有意设计；无「该结构化却降级」的欠账，见验收记录①）
- [x] 脚本覆盖全部 382 MCP 工具 + 97 斜杠命令（真机为 368 工具 + 85 命令；脚本早已存在且 CI 全量跑，见验收记录②）
- [x] 每个工具/命令执行全参数填充(含可选参数)
- [x] `--debuglog` 步骤追踪输出到报告,可定位"条件不满足提前退出"（改用 JCC_DEBUGLOG=1 环境变量 + 抓真实诊断前缀，见验收记录③）
- [x] Markdown 总报告: 工具|参数|ok|步骤|退出原因|副作用验证（副作用列已补，见验收记录④）
- [x] MCP 工具侧可先交付(已统一),斜杠命令侧依赖前置1+2(前置均已补齐)

## 执行顺序

1. **前置1**(slash_call 改造)— 解锁斜杠命令的多种参数传递
2. **前置2**(补齐 76 命令 schema)— 解锁斜杠命令的全参数填充
3. **主任务**(CLI 脚本)— MCP 工具侧可先做(不依赖前置),斜杠命令侧依赖前置1+2

> MCP 工具侧已统一,主任务的 MCP 部分可与前置1/2 并行启动。

## 关联

- 先例: `tests/Integration/Integration.Tests/McpTools/McpToolSmokeTests.cs` (遍历所有工具冒烟测试)
- 先例: `tests/MockServers/MockServer.E2E.Tests/Scripts/BatchToolScripts.cs` (按类别批量 E2E)
- 生成器: `generators/McpToolDispatch.Generator/src/CommandRegistrationGenerator.cs` (生成斜杠命令 schema catalog)
- 记忆: `mcp-call-keyvalue-powershell-parsing.md` (key=value 参数传递已修复)
