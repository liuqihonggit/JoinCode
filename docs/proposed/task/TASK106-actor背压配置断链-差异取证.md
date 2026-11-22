# TASK106 — actor 背压配置断链（TASK103 §2.3 #37 取证）

> 状态：**取证完成 — 待用户裁决** · 日期 2026-11-22 · 总账编号：TASK103 §2.3 **#37 / TASK068-C3**
> 基准树：`D:\project\claude-code-rust\claude-code-rev-main\src`
> 本仓库：`D:\project\w2`

---

## 0. 结论（先看这里）

这一项与前两项（#35 sandbox / #36 enabledPlugins）**性质不同**：

- #35/#36 是「基准有配置键，本仓缺消费」——对齐问题。
- **#37 是「基准压根没有 actor 背压配置这个概念」**：基准 settings schema 无 `actor` 键；搜到的 `backpressure` 全是网络传输层缓冲（ccrClient/relay/uploader），与本仓的 Actor 邮箱背压无关。`actor` 是**本仓自造的配置键**（C# Actor 模型特有，源于 ADR 0074）。

所以这不是对齐基准，而是**本仓自造配置的内部断链**：

1. **配置能进 WorkflowConfig**：`SettingsMapper.ApplyActorSettings`（:419-441）把 `settings.Actor.Backpressure` 四档预设（CodingAgentTask/LlmGateway/Router/Build）映射进 `WorkflowConfig.Actor.Backpressure`，还调 `Validate()`。这段是 #374 补的。
2. **但零运行时消费**：`WorkflowConfig.Actor` 在 `SettingsMapper` 和定义处之外全仓零读取。所有 Actor 子类仍用 `ActorBackpressure` 的静态预设（`GatewayActor.cs:91`、`RouterActor.cs:86`、`GlobalBuildQueue.cs:91`、`BuildQueueRouter.cs:184`、`FileMailbox.cs:30` 等）或字面量 `new ActorBackpressure(...)`（`LoopDiagnosticJournal.cs:45`、`CommandExecutionAuditor.cs:48`、`FileWatcherActorBase.cs:26`、`SettingsChangeApplier.cs:25` 等 10+ 处）。
3. **ADR 0074 的设计本就是静态四档**：决策6（:93-96/:184）明确「背压调参复杂 → 预定义四档」。所以静态预设不是 bug，是设计；#374 补的配置映射才是「加了没人用」的半成品。

> 一句话：**原文「10+ Actor 子类硬编码背压」说对了现象，但归因错了方向——这不是「对齐基准的架构差距」，是「本仓自己加的配置映射没接消费端」；而且基准根本没有这个配置，谈不上对齐。**

---

## 1. 四问回答

### 问1 专家隐性实践
背压是运行时保护参数，成熟做法是：默认值合理 + 允许按部署环境覆盖 + 覆盖能真正生效。本仓已有合理默认（ADR 0074 四档），缺的是「覆盖能生效」。但覆盖入口未必非要走 settings.json —— 构造函数注入、启动期一次性配置都是等价手段，且更简单。

### 问2 原始素材（一手）
- 本仓：`SettingsJson.cs`（Actor settings 定义）；`SettingsMapper.cs:46/419-441`（ApplyActorSettings 映射 + Validate）；`WorkflowConfig.cs:181`（`ActorSettings Actor = new()`）；`ActorSettings.cs:83/108`（BackpressureSettings/BackpressurePreset）；`ActorBackpressure.cs`（4 静态预设 + 构造校验）；`GatewayActor.cs:91` / `RouterActor.cs:86` / `GlobalBuildQueue.cs:91` / `BuildQueueRouter.cs:184` /多份 `new ActorBackpressure(...)`（子类消费）；ADR 0074 决策6（:93-96/:184）。
- 基准：settings schema 无 `actor` 键；`backpressure` 命中均在 `cli/transports/*` / `utils/process.ts` / `upstreamproxy/relay.ts`，语义是网络缓冲，非 Actor 邮箱。

### 问3 具体方法与工具
两侧源码 grep + 逐文件读；本仓用 `grep ".Actor.Backpressure|config.Actor" -v tests -v SettingsMapper` 确认零消费点；对照基准 settings schema 确认无对应键；读 ADR 0074 确认静态四档是原始设计意图。

### 问4 独立验证来源
本仓 `ActorBackpressureTest` 锁静态预设语义；`SettingsMapper` 映射有单测覆盖（historical）；ADR 0074 是架构组 accepted 决策，明确四档预设。基准侧 schema 无此键 = 负向证据（无法对齐）。

---

## 2. 两侧差异表

| 维度 | 基准 Claude Code | 本仓库现状 | 判定 |
|---|---|---|---|
| 配置项 `actor` | 不存在 | `SettingsJson.Actor` → `WorkflowConfig.ActorSettings` | **本仓独有，无基准可对齐** |
| 背压默认值 | 无此概念 | ADR 0074 四档静态预设（合理默认） | 本仓设计，非缺口 |
| 配置覆盖生效 | N/A | 映射进了 WorkflowConfig 但零消费 → 写了不生效 | **真缺口（本仓内部）** |
| 子类背压来源 | N/A | 静态预设 or 字面量构造（10+ 处） | 现象如原文所述 |

---

## 3. 三类归因

- **真缺口（本仓内部）**：`WorkflowConfig.Actor.Backpressure` 无消费点，settings 里改背压不生效。
- **转述过时/归因错误**：原文把这项归为「C 类待对齐基准的配置键」「大-Actor 系统重构」。事实是基准无此键，谈不上对齐；且默认四档是 ADR 明确设计，不是"硬编码 bug"。
- **无产生点**：基准侧无对应物（actor 配置键不存在）。

---

## 4. 待裁决选项

**问题**：本仓自造的 `actor` 背压配置断链，如何处理？

- **选项 A（推荐 — 最小且诚实）**：**删掉配置映射，回归 ADR 0074 设计**。背压用编译期四档静态预设即可（这是 accepted ADR 的原意），删除 `SettingsJson.Actor.Backpressure` 映射与 `WorkflowConfig.Actor.Backpressure` 字段（或保留字段但明确标注「预留、当前无消费」），总账判「不适用/回归设计」。理由：基准无此配置，强行接通等于为本仓独有概念建一整套「配置解析 + DI 注入」子系统（原文估"大-Actor 系统重构"），收益仅为"让用户能改一个基准根本不存在、ADR 也没打算让用户改的参数"。符合项目「无后向兼容」与「不做空中楼阁」。**注意**：删配置字段要先 grep 全部消费点确认无测试/模板依赖（模板 `.jcc/config/current.template.json` 等）。
- **选项 B（接通消费）**：新建「Actor 配置解析 + DI 注入」子系统，让 10+ 子类从 `WorkflowConfig.Actor.Backpressure` 取值构造。改动大（原文估"大"），且是为本仓独有概念服务，对齐基准收益为零；但能让"配置写了就生效"的原则成立。
- **选项 C（保留现状，登记已知断链）**：不动代码，总账保留为「已知但未做」，等以后真有用户调背压需求再做。零改动，但留着一个"写了不生效"的配置坑。

> 未裁决前不改生产代码。
