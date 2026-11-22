# TASK107 — dream 自动触发路径（TASK103 §2.3 #40 取证）

> 状态：**已裁决 — 需专项设计，本轮不做** · 日期 2026-11-22 · 总账编号：TASK103 §2.3 **#40 / TASK071-B5**
> 基准树：`D:\project\claude-code-rust\claude-code-rev-main\src`
> 本仓库：`D:\project\w2`

---

## 0. 结论（先看这里）

原文「C# dream 独立进程/命令触发（TS 是 stop hook 内联自动触发）— 自动触发路径仍缺失」**属实**，不是转述过时：

- 基准确有 `services/autoDream/autoDream.ts`(324) + `consolidationLock.ts`(140) + `consolidationPrompt.ts`(65) + `query/stopHooks.ts`(473)，合计 1023 行独立子系统，stop hook 内联触发。
- 本仓 dream 是**独立可执行 pipeline**（`modules/services/dream/core/pipeline/app/Program.cs`，`DreamFeature.ExecuteAsync`）+ 命令触发（`DreamPlugin` 注册 `/dream`、`/dream-tasks`），**没有 "回合 Stop 事件 → dream pipeline" 的接线**。

但本项与前几项（#35–#39）性质不同：**这是新行为，不是修 bug / 补断链**。它要求"回合结束后后台跑一次 LLM 记忆整合"，会改变回合返回时机与 token 成本，涉及并发/取消/失败降级/时间门三重设计决策。按无人值守「先处理已知问题，不引入新行为」的原则，**本轮不做**，登记为专项设计议题。

---

## 1. 四问回答

### 问1 专家隐性实践
自动记忆整合的成熟实现会在「用户无感知、不阻塞回合、可取消、有频率门」四条件下运行：基准用 sideQuery/后台 task + ConsolidationLock 时间门 + stop hook 触发。任何一环缺位就会出现"回合卡住"或"烧 token 跑空转"。

### 问2 原始素材（一手）
- 基准：`services/autoDream/autoDream.ts`(324)、`config.ts`(21)、`consolidationLock.ts`(140)、`consolidationPrompt.ts`(65)、`query/stopHooks.ts:102`（注释「extract-memories, auto-dream 不污染 timeline」）、`memdir/paths.ts:40`（触发来源列表 extractMemories turn-end fork / autoDream / /remember / /dream / team sync）。
- 本仓：`modules/services/dream/`（8 个 pipeline 中间件 + task 持久化 + `AutoDreamConfig` + `ConsolidationLock` + `ISessionScanner`）；`core/pipeline/app/Program.cs:75-88`（独立 exe 里调 `DreamFeature.ExecuteAsync`）；`DreamPlugin.cs:55-63`（只注册命令）；`HookEvent.cs:32`（`Stop` 事件类型存在）；`WorkflowConfig.cs:99/105`（字段注释「流向 Dream AutoDreamConfig.Enabled/AutoMemoryEnabled」）。

### 问3 具体方法与工具
两侧源码 grep + 逐文件读；确认基准有 stopHooks→autoDream 触发链、本仓有 dream pipeline 但无 Stop→pipeline 接线；读 `AutoDreamConfig` 与 `WorkflowConfig` 桥接注释确认配置侧已对齐。

### 问4 独立验证来源
基准 `stopHooks.ts` 与 `autoDream.ts` 互相印证触发关系；本仓 `DreamCommand`/`Program.cs` 两个入口证明当前只有命令 + 独立进程两条触发路径；`HookEvent.Stop` 存在证明基础设施具备。

---

## 2. 差异与判定

| 维度 | 基准 | 本仓 | 判定 |
|---|---|---|---|
| 触发方式 | stop hook 内联自动触发 | 命令 `/dream` + 独立 pipeline exe | 真差异（原文属实） |
| dream pipeline | autoDream.ts 单文件编排 | 8 个 middleware 组成的 pipeline（更结构化） | 实现不同，本仓不差 |
| 时间门 | consolidationLock.ts(140) | `ConsolidationLock.cs`（已落地，TASK071 服务域） | 已对齐 |
| 配置 | autoDream/config.ts + settings | `AutoDreamConfig` + `WorkflowConfig` 桥（:99/105） | 已对齐 |
| Stop 事件 | hook 触发源 | `HookEvent.Stop` 类型存在，无消费为 dream | 缺接线 |

---

## 3. 裁决：需专项设计，本轮不做

**理由**：
1. **性质不同**：#35–#39 都是"已有能力但配置/通道断了"，修了不改变用户可见行为；本项是给回合结束**新增**一次后台 LLM 调用，改变响应时机与成本。
2. **基准成本**：基准自己用 1023 行做这个子系统（含独立的 consolidation lock/prompt/scanner），不是"补根线"能完成。
3. **本仓已有替代路径**：独立 pipeline exe + `/dream` 命令 + 配置桥 + 时间门都已就绪；用户可显式触发，缺的只是"自动化"。
4. **无人值守边界**：无人值守时不做需要权衡产品行为（延迟 vs 成本 vs 记忆新鲜度）的新设计。

**后续启动条件**（写清以便将来接手）：需先回答三问——①自动触发是否用 sideQuery（不污染会话历史，基准 `stopHooks.ts:102` 明确要求）；②触发失败如何降级（不能阻塞回合、不能吞掉用户消息）；③默认开关与频率门（基准默认开 + 24h/5 session 门，本仓 `AutoDreamConfig` 已同默认值）。三问答完再立 ADR + TDD。
