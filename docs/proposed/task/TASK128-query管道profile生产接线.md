# TASK128 — #53 查询管道由 profile 治理

> 状态：已闭合（源码 + 定向用例 + 真机 `[ORDER]` 验收）。
> 设计：[ADR 0164](../../implemented/adr/architecture/0164-query-profile-runtime-consumption.md)（accepted）。
> 来源：[TASK103 #53](TASK103-未清事项总账.md)、[TASK126 §2](TASK126-dream自动触发-三问决策与接线.md)、[TASK100 §25](TASK100-节点化缺口普查与统一消费协议-四问深度版.md)。

## 缺陷与证据

`resources/profiles/base.yaml` 的 `query-pipeline` 已声明查询节点、顺序以及 `query-rate-limit-mw`（60 次/60 秒）。`CoreModule` 调用 `AddDataDrivenPipelines` 注册了这条管道，但 `QueryEngine.BuildPipeline` 自己枚举 DI 的 `IQueryMiddleware`，因此生产查询没有执行 profile 限流。判据来自本仓产品配置与组装契约，不以外部实现差异判 bug。

- **专家实践**：查真实入口和启动装配；对照新旧成员；用最小限额测试同一进程内的状态、切换与排除，避免只测节点目录。
- **原始素材**：`resources/profiles/base.yaml`、`CoreModule.cs`、`QueryEngine.cs`、`DataDrivenPipelineComposition.cs`、`ProfileManager.cs`。
- **方法**：真实 `QueryEngine.QueryAsync` 配本地计数模型；断言允许和限流时的模型调用数，检查重建读取目标 profile。
- **独立验证**：面向行为的装配测试；后续用独立 CLI + 本地 MockServer 验证生产入口，不要求真实模型凭据。

## 实施与验收

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---|---|---|---|
| `AddDataDrivenPipelines` + `NodeServiceResolver` 从 profile 装配查询节点 | `QueryEngine.QueryAsync` 首轮延迟取装配后的链，不合并旧 DI 集合 | ✅ | ✅ 限流 1→2→排除 三段切换用例绿；负向验证（跳过 profile 链）该用例转红 |
| `QueryCoreMiddleware` 固定终点，查询上下文绑定实际的核心循环 | 生产初装、切换重建、测试装配、独立引擎 | ✅ | ✅ 空链/停止 hook 续轮/工具调用上限用例绿；真机跑到模型调用（401）说明核心经该终点执行 |
| `pre-request-budget-guard-mw` 与 `message-dump-mw` | `base.yaml` 声明，复用既有具体类型与转储开关 | ✅ | ✅ 声明与工厂双向核对用例绿；真机 `[ORDER]` 同时出现两者 |

- **参数快照**：`ProfileSwitching` 早于 `Current` 更新，重建必须按事件中的目标快照同时取节点清单、限流参数和快照名。节点实例随链重建，不按查询重建；旧链已开始执行的调用继续在旧链上运行。沿用现有 profile 事件的异常处理，未扩展为跨管道事务。
- **限流口径**：默认每 60 秒允许 60 次进入 `QueryEngine.QueryAsync` 的查询；同一查询内的工具与 stop hook 续轮不重复扣配额。外层聊天流限流是独立节点。查询链不安装会吞掉下游异常的通用 Continue 回调，现有限流异常继续向调用方传播。
- **切换旧 DI 集合的完整性核对（本项最关键的判据）**：由生成物 `GeneratedServiceRegistration.g.cs` 取到 DI 注册的 11 个 `IQueryMiddleware`（ContentReplacement / CostTracking / DiminishingReturns / HistorySnip / IdleReminder / MessageDump / PreRequestBudgetGuard / StateTransition / StopHook / TokenBudget / UsdBudget），逐个映射到 profile 的 13 个节点 —— **DI 集合 ⊆ profile 声明**，其中两个（`pre-request-budget-guard-mw`、`message-dump-mw`）是本次补声明才收住的。若漏映射，症状是「某层守卫静默消失」，属 TASK100 §25 的同型事故。
- **静态检查**：`git diff --check` 通过；YAML 可解析，13 个查询节点的 ID 和优先级无重复、顺序正确，显式工厂覆盖 13 个节点；保留默认 60/60，项目 XML 可解析。

## 验收记录（2026-11-23）

| 项 | 命令 | 结果 |
|---|---|---|
| 定向用例 | `dotnet test modules/composition/pipelines.tests -c Debug --filter QueryProfileConsumptionTests` | 4/4 绿 |
| 负向验证 | 同上，把 `BuildPipeline` 的 profile 分支短路掉 | 限流与 stop hook 两条转红（`No exception was thrown` / 历史里没有「请继续收尾」），工具上限与声明核对两条仍绿 —— 证明前者真的依赖新接线 |
| 管道回归 | `dotnet test modules/composition/pipelines.tests`（全量） | 80/80 绿 |
| 查询回归 | `dotnet test modules/cognition/brain.other.tests --filter Query` | 259/259 绿 |
| 节点目录 | `jcc --dump-nodes` | 256 节点（+2），两个新节点在 base/ci/headless/sdk 均出现 |
| 门禁 | `Verify-NodeFactoryCoverage.py` / `Verify-RegistrationOrder.py` / `Verify-ExplicitOrder.py` | 三者 `[OK]`，256/256 工厂键 |
| 真机装配 | `JCC_DEBUGLOG=1 jcc -p hi` | `[ORDER] middleware query-pipeline: CrashSnapshot > FixedRateLimit > UsdBudget > TokenBudget > CostTracking > DiminishingReturns > HistorySnip > IdleReminder > StopHook > StateTransition > ContentReplacement > PreRequestBudgetGuard > MessageDump`；随后打到模型 401（本机无 key），`[CrashStore] 快照已捕获: [ERROR] Query:` 说明 `crash-snapshot-mw` 以 `pipeline-name: Query` 真实执行 |

- **未覆盖**：低限额下「请求不进入 MockServer」的进程级验证未做（本机无 key、未起 MockServer）。限流生效已由同进程用例证明（第 2 次查询 `RateLimitExceededException`，模型调用数停在 1），进程级只差一次带凭据/本地端点的手工确认。
- **产物可信度**：按 UTF-16 串在 `artifacts/bin/JoinCode/Debug/net10.0/JoinCode.Brain.Query.dll` 命中新增异常文案、`JoinCode.Pipelines.dll` 命中两个新节点 ID，确认跑的是新产物而非旧 DLL。
