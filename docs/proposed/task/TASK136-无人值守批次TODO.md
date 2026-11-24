# TASK136 — 无人值守批次 TODO（剩余清单收口）

> 状态：**执行中（2026-11-25）** · 基线 `w4` = `b55550b24`（TASK135 已提交待 PR）
> 用户诉求：睡觉期间一次性推进剩余清单，先 TODO 表再施工，最后统一 PR；不要东读一个西读一个。
> 范围界定：**不是全系列修复**。只收口 TASK130/131/135 里 AI 可自证的剩余项；TASK128 backlog（SSE / TUI 首帧 / GraphRAG / Pithos / CI 缓存）、TASK071 模型目录等一律不做。

## 1. 已闭合（本批前置）

| 项 | 状态 | 证据 |
|---|---|---|
| TASK135 hook 旁白全事件投影 | 已施工、已验收、已推送 | 代码 `c11b9b02b`，证据 `b55550b24`，Guard 643 / Host 1375 / 真机两条 `[HOOK-NOTICE]` |

## 2. TODO 表

| # | 项 | 目标 | 验收 | 状态 |
|---|---|---|---|---|
| T1 | TASK131 C1 回合级真机探针 | 用本地 OpenAI MockServer 伪造 vendor（无需真实 key），跑真实 `jcc -p` 回合，打通 `StopHookMiddleware → QueryStopHookManager → AutoDreamStopHook → AutoDreamTrigger`，观察 `[DREAM-AUTO]` 三态 | MockServer 收到 ≥1 次请求；jcc 回合成功；日志出现 `[DREAM-AUTO] fire` 或明确的 `skip: *` 门；无 key、无真实凭据 | ✅ 通过（探针暴露 3 个真实缺陷并修复） |
| T2 | TASK130 A5 NDJSON/agentMode 旁白投影 | 把 `IEventConsumer.OnSystemNotice` 在 `--agent-mode` 下映射为 SDK `system` 事件，补齐 A5 最后缺口 | 单测锁定 JSON `type/subtype/content`；真机 `jcc -p --agent-mode` 输出该事件；交互模式 dim 行不回归 | 待施工 |
| T3 | B1 双终端矩阵（可自动部分） | 真实 WT / conhost 窗口，AI 驱动可自动键位采样；IME/鼠标/系统粘贴等不可自动项**如实记未测** | `TASK129 §3.4` 两窗判定卡回填；不可自动项明确标未测，不用探针冒充 | 待施工（best effort） |
| T4 | 批次收尾 | 台账回写、全仓编译、统一 PR、守 CI 到绿 | `Build-AllDomains` 全绿；PR 合并/或明确失败原因 | 待施工 |

## 3. 决策与边界

- **T1 不伪造 key**：MockServer 是仓库既有 E2E 假 LLM；`JCC_ENDPOINT + JCC_ALLOW_DIRECT_FALLBACK=1` 显式绕过 Hub。这是真实进程级回合，不是单测替代。
- **T2 协议不臆造**：优先对齐既有 SDK `system/<subtype>` 形态；若找不到基准对应消息，则只补本仓已文档化的 subtype，并在文档登记与基准的差异。
- **T3 不冒充人工**：能由 AI 在真实窗口驱动的按键才算；不能自动的项宁写「未测」。
- **统一 PR**：本批所有 commit 完成后一次 PR；PR 前不再单独征询（用户已明确授权本批）。

## 4. 施工顺序

T1（闭合外部条件阻塞项）→ T2（同域收口）→ T3（真实终端 best effort）→ T4（台账 + 编译 + PR）。

## 5. T1 探针结果（2026-11-25）

**环境**：本地 `JoinCode.OpenAI.MockServer`（`scripted_turns`：`bash sleep 3` + follow-up 文本），隔离 AppData `.xxx\t136-home`，`JCC_ALLOW_DIRECT_FALLBACK=1` + `JCC_ENDPOINT=http://localhost:51169`，无真实 key。MockServer 共收到 7 次 `/chat/completions`（Request #0–#6）。

**探针依次暴露 3 个真实缺陷（均已修复并锁定）**：

| # | 缺陷 | 根因 | 修复 |
|---|---|---|---|
| A | `[DREAM-AUTO] hook waiting` 永久停留 | Dream 服务注册在 `WorkflowPluginHost` 的独立 `ServiceProvider`，主容器 `sp.GetService` 恒 null | 主容器取不到时回落 `IPluginManager.GetWorkflowPlugin("Dream")` 的插件容器（`DataDrivenPipelineComposition.ResolveDreamService`） |
| B | 插件容器内 `IDreamFeature` / `ISessionScanner` 缺失 | `AddDreamPluginServices` 只注册 `AutoDreamConfig`；`[Register]` 生成器方法无人调用 | 在 `AddDreamPluginServices` 补齐 Dream 运行时最小集（fs / chat completion / scanner / persistence / registry / lock / feature） |
| C | 卸载 Dream 插件抛 `InvalidOperationException: ... Use DisposeAsync` | `WorkflowPluginHost.UnloadAsync` 对含 `IAsyncDisposable` 服务的容器调用同步 `Dispose()` | 新增 `DisposePluginServicesAsync()`，`UnloadAsync` 与 `DisposeAsync` 均走异步释放 |

**真机证据**：

- 修复 C 后第一次运行：`[DREAM-AUTO] fire sessions=10 prior=0`（`t136-c1-run7.txt`）。
- 紧接着第二次运行：`[DREAM-AUTO] skip: time-gate 0.1h < 24h`（`t136-c1-run8.txt`）—— 证明首次 fire 真的写了 consolidation 锁，时间门随后生效；两轮均 `EXIT=0`，无卸载异常。
- 红→绿：Pipelines 新增 `PluginHostContainer_ResolvesDreamAndFires` 修复前 5s 超时，修复后通过；Dream 新增 `DreamPlugin_LoadAsync_RegistersSessionScannerInPluginContainer` 修复前缺 scanner；Plugins 新增 `UnloadAsync_PluginContainerHasAsyncOnlyService_DisposesAsynchronously` 修复前 5s 超时。
- 回归：`Pipelines.Tests` 81/81、`Dream.Tests` 213/213；`Plugins.Tests` 193 通过，5 个 `NativePluginHostE2ETests` 因本机未发布 native DLL 抛 `SkipException`（既有环境项，与本次改动无关）。

<!-- 🤖 Auto Decision: 2026-11-25 -->
<!-- 决策: 批次只做 TASK130/131/135 剩余，不扩散到 TASK128 backlog/历史全系列 -->
<!-- 原因: 用户质问“你是在做全系列修复吗”，并明确害怕 AI 忘记剩余任务而不是要扫全部历史 -->
<!-- 替代方案: 从 TASK103 总账捞零散项（范围失控，已否定） -->
<!-- 验证: T1 已通过；T2-T4 待续 -->
