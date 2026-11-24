# TASK131 — 剩余事项执行规划（TASK130 + TASK128）

> 基线：`main` / `w4` = `74bb6047f`（TASK130-A6 已由上游完整落地），工作区仅保留本轮补强（2026-11-24）。
> 来源：[TASK130 剩余事项清单](TASK130-剩余事项清单与验收步骤.md)、[TASK128 查询/会话/门禁热路径](TASK128-查询会话与门禁热路径优化.md)。
> 纪律：先规划再动代码；每闭合一项回写 TASK130 证据行；一次只推进一个可独立验收的切片。

## 1. 范围与不在范围

**本轮范围**

- TASK130 的 A 类 OPEN 项：A3、A6（A4 已于 2026-11-24 闭合；A6 已由 HEAD `74bb6047f` 完整落地，本批只保留 rebase 后的两项补强验证）。
- TASK130 的 C2：低限额进程级验证。
- 上次遗留的 Query 进程级 exe 验收（与 C2 共用 MockServer harness）。
- B1 的人工矩阵准备与记录模板（执行仍由用户完成）。
- B2 的产品决策设计稿。
- TASK128 的五个后续独立切片只登记为 backlog，不在 P0/P1 强行插入。

**不在范围**

- 已闭合的 A1、A2、A5、D1-D3 不重做。
- C1（真实 vendor key）和 B3（macOS Seatbelt）受外部条件阻塞，只保留验收步骤。
- 其他历史任务文档中的旧欠账；以 TASK130 为唯一剩余事项入口。

## 2. 剩余事项总表

| 批次 | 项 | 现状 | 可本机自证 | 前置决策/条件 | 交付物 |
|---|---|---|---|---|---|
| P0 | A6 SessionStart `AdditionalConfig` 零消费 | 代码确认零读取 | 是 | D1：`additionalContext` / `initialUserMessage` 的注入通道 | 红测试 + 消费链 + 回归 |
| P0 | A4 flag settings 策略锁 | **已施工**（2026-11-24） | 是 | D2 已闭合 | 代码 + 单测 + 真机日志 |
| P0 | A3 斜杠命令取参审计 | **已施工**（2026-11-24，随 `c28d6412d`/PR #734 合入） | 是 | 无 | 审计表（TASK130 A3）+ `TryTakeSubCommand` 词边界修复 + `SubCommandBoundaryTest` 13 条 |
| P1 | C2 低限额进程级验证 | **已施工**（2026-11-24，`Wq01` E2E 常驻回归 + `bridge_serve --profile` 真 bug 修复） | 是（MockServer） | 无 | 单进程双回合：第 2 回合被拦 + MockServer 增量 == 1 |
| P1 | Query 进程级 exe 验收 | **已施工**（2026-11-24，Streaming/Vd02/Ve06/Wq01 4/4，与 C2 共用 harness） | 是（MockServer） | 无 | 流式输出/异常/取消的进程证据 |
| P1 | B1 双终端矩阵 | 探针 4/4，矩阵 10×2 未测 | 否（人工） | 真实 Windows Terminal + conhost | 矩阵结论 + 产物哈希 |
| P2 | B2 Ctrl+B 主会话后台化 | 取证完成，产品决策未做 | 否（需裁决） | D3：并发/transcript/UI 状态机 | 一页设计稿，不直接照搬 |
| P2 | C1 回合级真机探针 | 接线已修，缺真实 key | 否 | vendor API key | `[DREAM-AUTO]` 证据 |
| P2 | B3 macOS Seatbelt | 显式未实现 | 否 | macOS 真机 + 独立 ADR | 外部条件满足后再立项 |
| Backlog | TASK128 五个后续切片 | 已登记 | 视切片 | 见 §6 | 各自独立立项 |

## 3. 执行顺序

### P0-1：A6 — SessionStart 上下文消费 — **已由 HEAD 完整落地（2026-11-24）**

**rebase 后的事实**

`74bb6047f`（`fix(hooks): SessionStart additionalContext 真机落地（A6）+ 修复 stdin 重定向`）已经用一套比本地旧方案更完整的架构实现了 A6：

| 层 | HEAD 实现 |
|---|---|
| 解析 | `HookExecutorBase.ParseHookSpecificOutput` 读 `additionalContext` / `initialUserMessage`（`TryGetString` 非字符串不抛）；`IsContextCarryingEvent` 只让 `SessionStart` / `UserPromptSubmit` 把 exit-0 纯文本 stdout 映射为 `AdditionalContext` |
| 契约 | `SessionStartHookResult` 类型化 `AdditionalContext` / `InitialUserMessage`；`updatedInput` 仍留 `AdditionalConfig` |
| 聚合 | `SessionStartHookManager` 多 hook 的空行拼接 |
| 承接 | `ISessionStartContextStore` / `SessionStartContextStore` + `SessionStartHookRunner`；chatinit 链与 `SessionController.EnsureSessionStartHookAsync` 共用 Runner |
| 消费 | `SessionStartContextMiddleware`（`preprocess-prepare-pipeline`，priority 110）每回合按 sessionId 取 store 并 `AddDynamicSystemMessageAsync` 补写；不进 transcript，不被 `SystemPromptMiddleware` 每回合清空 |
| 附带修 | `ProcessStartInfoBuilder.Build` 补 `RedirectStandardInput`，否则所有 bash 型 hook 真机跑不通 |

**`initialUserMessage` 判定：解析但不消费**

HEAD 明确不插 user turn：本仓没有「无输入自动开轮」模式，自动插入会凭空产生用户回合和模型调用；`SessionStartHookRunner` 只记 `LogDebug` 并忽略。本地旧实现中的 `ChatInitContext` 字段、`ChatInitializer` 写 system、`NonInteractivePromptStep` 前插、`SessionInitStep` / `IChatService.InitializeAsync` 均已整体丢弃，**禁止恢复**。

**本地补强（HEAD 未覆盖，保留）**

- `ProcessStartInfoBuilder`：在 HEAD 的 `RedirectStandardInput` 基础上显式设置 `StandardInputEncoding = _encodingProvider.Input`（无 BOM UTF-8），避免 stdin JSON 被 BOM 污染。
- `AppendOnlyLog`：`Role == default` 会把枚举零值 `MessageRole.System` 误判为非法角色；改为显式角色白名单，并补 `Append_SystemMessage_IsValid`。

**验收（本轮已跑）**

```powershell
dotnet test test\unit\infra.tests\io\Infra.IO.Tests.csproj
dotnet test modules\cognition\brain_context_tests\Brain.Context.Tests.csproj
dotnet test modules\guard\guard_hooks_tests\Guard.Hooks.Tests.csproj
dotnet test test\unit\host.tests\Host.Tests.csproj
```

- `Infra.IO.Tests` 331/331（需把 `C:\Program Files\Git\cmd`、`Git\bin`、`Git\usr\bin` 加进 PATH，否则 5 条真实 git/bash 进程用例因找不到可执行文件而失败）
- `Brain.Context.Tests` 964/964、`Guard.Hooks.Tests` 615/615、`Host.Tests` 1369/1369
- `NativeTui.Host.Tests` 174/174、`NativeGui.Host.Tests` 50/50
- `Build-AllDomains.ps1` 全仓编译：solution / native_tui / native_gui 全通过
- 真机冒烟（隔离 `JCC_APP_DATA_FOLDER=.xxx\a6-home`，SessionStart hook 返回 `additionalContext`）：日志 `[SESSION-CONTEXT] 收到 additionalContext 21 字符` → `[SESSION-CONTEXT] 注入 21 字符`，`preprocess-prepare-pipeline` 顺序为 `SystemPromptMiddleware > SessionStartContextMiddleware > ...`

**结论**：A6 不再需要本地施工；本批只提交上述两项补强与验证记录。

---

### P0-2：A4 — flag settings 策略锁 — **已施工（2026-11-24）**

**判定**：不是边界说明。基准 `sandbox-adapter.ts:647-664` 明确把 `flagSettings` 与 `policySettings` 同为
`overridingSources`；本仓此前没有 `--settings` 入口、`ConfigLoader` 也从未传 flag 来源，属于真安全缺口。

**落地**

1. CLI：`JccCliArg.Settings` + `CliArg.Settings`；`jcc --settings <file|inline-json>`。
2. 加载：`SettingsLoader.LoadFlagSettingsAsync` 支持文件/内联 JSON，显式来源缺失/非法 JSON/解析失败 fail-fast。
3. 投影：`SandboxPolicyResolver.Apply(config, policy, flag)` 合并三个锁定键并去重；managed 清单仍只认 policy。
4. 来源提示：`SandboxPolicyConfig.FlagLockedKeys` / `PolicyLockedKeys`，`/sandbox` 按来源分别说明下一步。
5. Hub：`ConfigLoader.ApplySandboxConfigAsync` 仅投影 sandbox，`LoadHubConfigAsync` 调用，避免 Hub 默认路径绕过策略锁。
6. `config-print --settings` 已接线。

**证据**

- `Guard.Config.Tests` 1358/1358；`Host.Tests` 1374/1374；`Composition.Tests` 1173/1173（需 Git PATH）；`Guard.Hooks.Tests` 615/615。
- 全仓 `Build-AllDomains.ps1` 全部通过。
- 真实 `jcc --settings`（文件/内联 JSON）日志均为 `[SANDBOX-POLICY] locked=True keys=[sandbox.enabled]`；
  缺失文件 exit=2（`ConfigurationError`），输出 `Settings file not found for --settings`。
- 真实 Hub 进程未跑：运行 `jcc` 时 A1 环境反复掉线；Hub 投影由 `ApplySandboxConfig_FlagSettings_AppliesPolicyLockForHubPath` 单测覆盖，不能冒充进程验收。

**决策 D2 已闭合**：选择施工，而不是关闭为边界。详细验收表见 TASK130 A4。

---

### P0-3：A3 — 斜杠命令取参审计 — **已施工（2026-11-24，随 `c28d6412d` 合入）**

**结论（详见 [TASK130](TASK130-剩余事项清单与验收步骤.md) A3 段逐条审计结果）**

- 46 处 `GetNormalizedArgs` 逐条三分类后，真 bug 不在「多字段 JSON 拼接」，而在「前缀命中」：
  `args.StartsWith("add")` 让 `/tag address` 静默写成标签 `ress`、`/workflows runner` 变 `ner`、`/plugin installer.exe` 变 `er.exe`。
- 修法：`ChatCommandBase.TryTakeSubCommand`（子命令后必须是串尾或空白），Plugin(4)/Tag(2)/Workflows(2)/Hooks(3) 共 11 处分支改用；
  全仓只剩这一份前缀判定。自由文本整串类（`Btw`/`Rename`/`Compact` 等）按 ① 类保留。
- 用例：`SubCommandBoundaryTest` 13 条（11 组词边界理论值 + 2 条 `/tag` 行为，含 `AddTag` 零调用断言）；
  负向验证删词边界 3 行 → 5 条转红；`Composition.Tests` 全量绿（A4 轮复跑 1173/1173）。

**验收**

```powershell
rg -n "TryTakeSubCommand" modules --glob "*.cs"
dotnet test modules\composition\composition_tests\Composition.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~Command"
```

---

### P1-1：C2 + Query 进程级（共用 MockServer）— **已施工（2026-11-24）**

**结果**（实施与原步骤的差异已在 [TASK130 §C2](TASK130-剩余事项清单与验收步骤.md) 说明）：

- 原步骤「连续两次 `jcc -p hi`」不成立：`-p` 每次新进程，限流计数器随 QueryEngine
  管道单例重置，独立进程永远打不满限额。改为 **bridge_serve 单进程双回合**（与 CI E2E 同 harness）。
- 新增常驻回归：`WqQueryRateLimitScripts` + `Wq01Tests`（外部 profile `c2.yaml` 覆写
  `query-rate-limit-mw.max-requests=1` 经 `JCC_PROFILE_DIR` 注入）—— 第 1 回合消耗唯一限额
  进 MockServer，第 2 回合报「速率限制」，`/__control__/requests` 增量断言 **== 1**。
- **顺带修出真 bug**：`bridge_serve --profile X` 的 `--profile` 被忽略（子命令路由优先于全局
  ParseArgs），引擎静默回退 base。修复：BridgeServeCommand 与 `--vendor`/`--model` 同构解析带入 options.Profile。
- Query 进程级：Streaming 超时验收 + Vd02 异常路径 + Ve06 多轮 + Wq01（含流式事件与回合异常
  → error 事件）**4/4 通过**，证据 `.xxx/logs/p1-1-e2e-evidence.txt`；TASK130 §C2、TASK128 未验项均已回写。

**验收**

```powershell
$env:JCC_ALLOW_DIRECT_FALLBACK="1"  # 与 ci-e2e.yml 一致（E2E 无 Hub 环境）
dotnet test artifacts\bin\MockServer.E2E.Tests\Debug\net10.0\JoinCode.MockServer.E2E.Tests.dll --no-build `
  --filter "FullyQualifiedName~Wq01Tests|FullyQualifiedName~Ve06Tests|FullyQualifiedName~NonInteractiveMode_ShouldCompleteWithoutTimeout|FullyQualifiedName~Vd02Tests"
```

---

### P1-2：B1 双终端矩阵

**目标**：把 `TASK129` 的 10×2 矩阵从「待测」变成有窗口身份、按键、产物哈希的结论。

**步骤**

1. 由 AI 生成空白矩阵和一次性采集命令；用户分别在 Windows Terminal 与经典 Console Host 执行。
2. 每个单元格记录：应用已消费 / 仅解码通过 / 宿主截获未上报 / 未测。
3. 结果回填 TASK129 表，并同步 TASK066 B6、TASK121。

**边界**：这是人工验收，AI 不伪造按键结果；没有窗口证据就保持「未测」。

---

### P2：B2 / C1 / B3

- B2：先写一页设计稿，明确 Ctrl+B 主会话后台化的并发查询、transcript 归属、Esc/Ctrl+B 返回状态机、失败回滚；用户裁决前不写实现。
- C1：等 vendor key；按 TASK126 §5.3 跑 `[DREAM-AUTO]`，无 key 不伪造。
- B3：等 macOS 真机；先独立 ADR，不在 Windows 上假实现 Seatbelt。

## 4. 提交与验收纪律

- 每项一个独立提交/PR；红测试 → 实现 → 定向绿 → 文档证据 → commit。
- 每个 PR 后守 CI；失败先拉日志，能本地复现就复现，不能复现的 flaky 才重跑。
- 不把 `TASK128` 五个后续切片混入 A6/A3/A4 的 PR。
- 每闭合一项，在 TASK130 对应段落加一行「证据 + 命令 + 结果」，并把总表状态改掉。
- 未达到「真实进程/真实终端/真实 key」的项，明确写「未测」，不以单元测试冒充。

## 5. 本批第一个动作

A6 已由 HEAD `74bb6047f` 完整落地；A4 已于 2026-11-24 施工并通过定向/全量回归（PR #738，merge `138a98912`）；A3 已随 `c28d6412d`（PR #734）合入。**P0 批次全部闭合**；P1-1（C2 + Query 进程级）已于 2026-11-24 施工闭合。下一项从 **P1-2 B1 双终端矩阵**（需人工双终端）或 P2 项开始。

## 6. Backlog（TASK128 后续独立切片，不在本批）

| 切片 | 入口 | 启动条件 |
|---|---|---|
| CI 制品缓存输入/输出闭合 | TASK128 §后续 | 当前 PR 流程稳定后 |
| SSE 快照 | TASK128 §后续 | 独立立项 |
| TUI 首帧及布局 | TASK128 §后续 | 独立立项 |
| GraphRAG | TASK128 §后续 | 独立立项 |
| Pithos 范围扫描 | TASK128 §后续 | 独立立项 |

## 7. 未决决策

| 编号 | 决策 | 推荐 | 阻塞项 |
|---|---|---|---|
| D1 | A6 的 `additionalContext` / `initialUserMessage` 通道 | HEAD 已定：`additionalContext` 走 session store + 每回合动态系统消息；`initialUserMessage` 只解析不消费 | 已闭合（HEAD `74bb6047f`） |
| D2 | A4 施工还是关闭为边界 | 已施工：flag + policy 双源锁，Hub 路径补投影 | 已闭合（2026-11-24） |
| D3 | B2 Ctrl+B 产品语义 | 先设计稿后裁决 | B2 |

