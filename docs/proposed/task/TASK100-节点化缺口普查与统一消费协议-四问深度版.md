# TASK100 节点化缺口普查与「统一消费协议」重构（四问深度版）

> 前置：TASK097（节点继承 G1–G7）、TASK098（DSH×Harness² 融合：统一 Delta / 可回滚句柄 / Probe 门禁）。
> 本文回答一个问题：**按 DSH「万物皆节点」与 Harness²「可继承」的标准衡量，jcc 还有哪些代码没节点化，为什么，怎么补。**
> 本文只做普查与方案，不含实施；实施由本文 §6 的 S1–S5 分步推进，每步独立验收。

---

## 0. 一句话结论

**节点体系只覆盖了「中间件」这一种形态。**

- `jcc --dump-nodes` 报告 **176 个唯一节点 / 669 个放置**，其中 **588 个放置带 `-mw` 后缀**；剩下的是 3 个 `prompt-assembly` 类别节点、若干 `command-dispatch` 类别节点。
- 其余六类可扩展点（命令守卫链、模型适配器、Agent 角色档案、业务能力、命令表面、工具分组）**一个节点都不是**：它们靠 `[Register]`（全仓 832 处）、手写 `new`、硬编码清单、编译期枚举活着。
- 根因不在命名，而在 `INode` 缺一个「**消费协议**」字段：节点被**什么契约**消费，只写在 `BuildNodeFactories()` 那 161 行手写 lambda 的字符串前缀里（`"chat-pipeline:xxx-mw"`）。因此**只有 `IMiddleware<T>` 能当节点**，`ITodoService`、`ICommandGuard`、`ILLMQueryService` 天生当不了。
- 补这一层，等价于把 Harness² 的 **SurfaceSpec**（"同一套逻辑装到不同宿主，差异收进一份数据"）落到本工程；已有 `NodeMetadata.Capabilities/RequiredCapabilities` 字段正好是它的落点（当前是**只写不读的死字段**）。

---

## 1. 审计判据与取证方法

### 1.1 判据（四条全满足才算「节点化」）

| # | 判据 | 可核对口径 |
|---|---|---|
| ① | 有数据源声明的稳定 ID | `resources/profiles/*.yaml` 或 `*.node.yaml` 里能搜到该 ID |
| ② | 位置/顺序/开关由 profile 消费点决定 | 改 profile 的 `priority`/`excludes`/`enabled-when` 能改变装配 |
| ③ | 能被 NodeRegistry 归纳、`--dump-nodes` 可见 | 有 `BuildNodeFactories()` 工厂并在 dump 中出现 |
| ④ | 不重新编译就能启用/禁用/改配置 | 无需 `dotnet build`，仅改外部文件即可 |

### 1.2 取证方法

1. **真机清点**：`jcc --dump-nodes` 导出全部放置，按后缀/消费点归类（`artifacts/bin/JoinCode/Debug/net10.0/jcc.exe`）。
2. **静态比对**：用脚本把 `base.yaml` 的 `consumer:node` 声明与 `DataDrivenPipelineComposition.cs` 的 `F("...")` 工厂做双向差集。
3. **注册面清点**：`grep -c '\[Register('` = **832**；`[McpTool]` 方法 **580**、`[McpToolDispatch]` 类 **101**；`[ChatCommand]` **99**。
4. **域级深挖**：guard / llm / storage+cognition / cli+mcp 四域分别逐类核对注册方式、顺序来源、配置键。

### 1.3 本次实测的关键数字

| 指标 | 数值 | 来源 |
|---|---|---|
| 唯一节点 | 176 | `--dump-nodes` |
| 节点放置（消费点 × 节点） | 669 | `--dump-nodes` |
| 手写工厂 | 161 | `DataDrivenPipelineComposition.cs:30-289` |
| `base.yaml` 声明放置 | 165 | 脚本比对 |
| 声明了但无工厂（死声明） | **4**（全部是 `tool-dispatch:*`） | 脚本比对 |
| 有工厂但四 profile 都不声明（孤儿） | **0** | 脚本比对 |
| `[Register]` 特性 | 832 | grep |
| `[McpTool]` / dispatch 类 | 580 / 101 | grep |
| 斜杠命令 | 99（15 类） | grep + `command-dispatch` |
| CLI 子命令 | 20 值 / router 16 case | `CliSubCommand.cs` / `FlatSubCommandRouter.cs:14-49` |
| 命令守卫 / fix hook / BashDefense 步 / sandbox provider | 8 / 3 / 6 / 4 | guard 域核对 |
| 内置 Agent 角色档案 | 9（1 Coordinator + 8 Executor 变体） | `AgentRoleProfileRegistry.cs:222` |

---

## 2. 现状：节点体系真实覆盖到哪

### 2.1 已经节点化的部分（做对了，别推翻）

| 层 | 覆盖 | 证据 |
|---|---|---|
| 16 条管道中间件 | 全量（chat/preprocess/chatinit/chatadmin/compact/query/permission/settings/unified-spawn/agent-dispose/fork/web/shell/skill/code/dream/tool-execution/agent-tool） | `base.yaml` + `--dump-nodes` |
| 守卫的**权限中间件层** | 12 个 `IPermissionMiddleware` 已在 `permission-pipeline` | `base.yaml:137-166` |
| 提示词装配 | 3 个类别节点（always/agent-mode/coordinator-mode） | `base.yaml:469-476` |
| 斜杠命令**类别** | 15 类，`command-dispatch` 消费 | `base.yaml:436-467` |
| 工具**清单** | 635（`jcc tool tree`），14 个 `node.yaml` | `NodeManifestLoader` |
| 继承与回滚 | `CliNodeInheritanceResolver` / `RegistrationHandle` / `CompositionHandle` | TASK097/098 |

### 2.2 未节点化的部分（本次普查结论）

| 域 | 对象 | 现在靠什么 | 判定 |
|---|---|---|---|
| guard.hooks | 8 个 `ICommandGuard` | DI 集合收集 + `orders.yaml:21-29` 排序，整体塞进 `shell-intercept-mw` | ①半 ②✗ ③✗ ④✗ |
| guard.hooks | 3 个 `IToolFixHook` | `ToolFixHookRegistry.cs:39-43` 手写 `new` 三个 + 插件卸载写死三个字符串 | ①有 ②✗ ③✗ ④✗ |
| guard.hooks | 6 个 `BashDefense` 步骤（**类名带 Node，不是 INode**） | `ShellCommandInterceptionMiddleware.cs:106-113` 硬编码 `.Then()` 链 | 全 ✗ |
| guard.sandbox | 4 个 SandboxProvider + 降级链 | `SandboxProvidersPlugin.cs:39-42` 四连 `new`；`SandboxManager.cs:138-140` 硬编码 fallback | 全 ✗ |
| llm.core | 8 个 QueryService 适配器 | **两份**注册表（`LlmProvidersPlugin.cs:29-39` 与 `ServiceRegistration.cs:15-36`，且互不对称） | ①枚举 ②✗ ③✗ ④✗ |
| llm.core | ProviderDefinition | `ProviderDefinitionRegistry.cs:107-115` 硬 `switch`，未知协议**静默降级**为 openai | 全 ✗ |
| llm.agents | 9 个内置角色档案 | `AgentRoleProfileRegistry.cs:222` C# 对象初始化器写死工具白名单/模型/温度 | 全 ✗ |
| storage / services / cognition | todo、cron、goal、memdir、dream 插件、vision | DI 特性；能力由 `EngineServiceContext.cs:8-58` 13 个字段硬编码暴露；`McpInitModule.cs:79-90` 8 插件硬编码清单 | 全 ✗ |
| apps/cli | 20 个 `CliSubCommand` | 枚举 + `ApplicationBuilder` 2 个 if + router 16 case | ①有 ②✗ ③✗ ④✗ |
| tools | 635 工具 / 580 `[McpTool]` | 生成器 + `cli_groups.yaml` 影子体系；`tool-dispatch` 4 个节点是死声明 | ①有 ②✗ ③✗④✗ |
| composition | 161 个工厂的 ID | 与 yaml 双写（`"chat-pipeline:stream-rate-limit-mw"`） | ①有 ②有 ③有 ④✗ |

---

## 3. 四个根因（按影响面排序）

### R1 形态绑架：`INode` 没有「消费协议」

`INode` 只有 3 个属性（`NodeId`/`Metadata`/`State`，`INode.cs:7-16`）。节点服务于哪种消费点，只由工厂字符串前缀表达：

```csharp
yield return F("chat-pipeline:stream-rate-limit-mw", sp => sp.GetRequiredService<RateLimitMiddleware>());
```

于是：

- 消费点没有「我接受什么形态」的声明，注册表无从校验；
- 一个非中间件能力（如 `ITodoService`）**在类型上无法成为节点**，只能另写一套装配；
- 161 个 ID 在 yaml 与 C# 双写，改名要改两处且无编译期保护。

DSH 的解法是 `ctx.provide('llm')` —— **契约是显式的、双向的**。本工程缺的正是这个。

### R2 数据源编译期内嵌 → 判据 ④ 全局失效

`resources/profiles/*.yaml` 是 `EmbeddedResource`（`Composition.csproj:45-48`），`NodeProfileLoader.cs:13,35` 只读 `GetManifestResourceStream`，**没有磁盘回退、没有 env 指定外部 yaml 的入口**。

后果：改一个 `priority` 或关一个节点，必须重编 `JoinCode.Composition`（全仓序列 6~8 分钟）。运行期唯一可改通道是 TASK098 的 `JCC_EXPERIENCE_ADOPT`（仅 config/priority，fail-closed）。**这意味着当前所有「节点」都只满足判据 ③，不满足 ④**——「轻」只落在代码结构上，没落到运维上。

### R3 声明 ↔ 工厂无对齐门禁

`base.yaml` 165 个放置 ↔ 161 个工厂，差集里 **4 个 `tool-dispatch:*` 是死声明**（`base.yaml:428-434` 声明 shell/file/web/code_execution，工厂 0 个、消费者 0 个），`ToolDispatchConsumer` 从未被实例化。这个缺口靠人工发现，`--dump-nodes` 只打印不校验（`Program.cs:245-317`）。

反例对照：孤儿工厂 0 个 —— 说明单向漂移存在，双向一致没有门禁兜底。

### R4 两套事实并存：DI 是真装配，节点表是旁路观测

832 处 `[Register]` 才是真实装配；节点表只是被 dump 出来的一个平行视图。三处典型旁路：

- `EngineServiceContext.cs:8-58` 13 个字段手工暴露能力；
- `McpInitModule.cs:79-90` 8 个插件硬编码清单（`[Register(typeof(IWorkflowPlugin))]` 已被注册却用不上，profile 关不掉 dream）；
- `LlmProvidersPlugin` 与 `ServiceRegistration` 两份适配器注册表互不对称（前者缺 Hub、`OnUnload` 不撤销）。

**这就是 DSH 与本工程最本质的差距**：DSH 里「注册了」= 「在容器里」= 「可被组合」，是一件事；本工程是三件事，其中只有一件是数据驱动的。

---

## 4. 重构方案：三层（不引入 NodeBase，沿用 G3 裁决）

### L1 消费协议（contract）—— SurfaceSpec 的本工程对应物

**做法**：在 profile 消费点上声明 `contract`，而不是给节点加基类。

```yaml
consumers:
  chat-pipeline:
    contract: middleware        # 默认值，缺省即 middleware（165 个现有声明零改动）
    nodes: [...]
  guard-chain:
    contract: guard             # 新增：按 priority 排序的执行链
    nodes: [{ id: git-commit-guard, priority: 100 }]
  llm-providers:
    contract: provider          # 新增：按 protocol 选择，互斥而非叠加
    nodes: [{ id: anthropic-adapter }, { id: openai-adapter }]
  agent-roles:
    contract: role              # 新增：按 role/variant 选择
    nodes: [{ id: executor-readonly }]
```

**契约语义表**（每种契约决定「如何组合」，这是 Harness² SurfaceSpec 的字段表）：

| contract | 组合语义 | 选择依据 | 现有载体 |
|---|---|---|---|
| `middleware` | 顺序叠加 | `priority` 升序 | 16 条管道 |
| `guard` | 顺序叠加，可改写/短路 | `priority` 降序 | 8 守卫 + 3 fix hook + 6 BashDefense |
| `provider` | 互斥选择 | `protocol` / 配置键 | 8 QueryService |
| `role` | 互斥选择 | `role` + `variant` | 9 角色档案 |
| `capability` | 存在即提供 | 依赖声明 | todo/cron/memory/dream |
| `command-group` | 分类聚合 | 类别 ID | 15 类命令 |

**关键收益**：`NodeMetadata.Capabilities/RequiredCapabilities`（`NodeMetadata.cs:16-17`，当前无任何消费方）从死字段变成**路由依据** —— provider/role 的选择不再靠 `switch`，而靠「谁声明了我需要的能力」。这正是 DSH Service/Consumer 的语义，也是 Harness²「数据表达差异」的语义。

**不做**：不加 `NodeBase`、不加节点类层次、`INode` 保持 3 属性。

### L2 一致性门禁（让漂移编译期/CI 可见）

新增 `non_deliverables_tools/scripts/Verify-NodeConsistency.py` + `jcc --dump-nodes --verify`：

1. profile 声明的每个 `consumer:node` 必须有工厂（当前 4 个不通过 → 要么接上、要么删除）；
2. 每个工厂必须至少被一个 profile 声明（孤儿 = 0，保持）；
3. 每个 `contract` 必须是已知值；
4. 不一致 → 非零退出，CI 失败。

成本极低、收益直接：把「靠人肉比对」变成门禁。

### L3 运行期外部覆盖（让判据 ④ 成立）

`NodeProfileLoader` 增加磁盘覆盖层：

```
内嵌 profile（H₀ 基线）
  ← 磁盘 delta：$JCC_PROFILE_DIR/<name>.yaml 或 ~/.jcc/profiles/<name>.yaml
```

复用 TASK098 的 `DeltaComposer`（`DeltaLifetime.Permanent`）合并，不新写覆写逻辑。这样「关一个节点 / 改一个 priority / 换一个 provider」不再需要重编。

**边界**：磁盘覆盖在 AOT 下仍是纯数据解析（无反射），且 H₀ 基线永远存在，覆盖层可整体丢弃 → 不破坏「绝不继承成品」红线（覆盖层是 Permanent delta，不是上轮产物）。

---

## 5. 分域缺口清单（按「收益 ÷ 成本」排序）

| 序 | 对象 | 现在靠什么（file:line） | 缺判据 | 收益 | 成本 |
|---|---|---|---|---|---|
| 1 | 8 守卫 + 3 fix hook + 6 BashDefense 步骤 | `CommandInterceptionDispatcher.cs:49`、`ToolFixHookRegistry.cs:39-43`、`ShellCommandInterceptionMiddleware.cs:106-113` | ②③④ | 安全链全序进 dump；按 profile 关某守卫（headless 关 VpnRoute）；消除三处重复名单 | 低（已有 `orders.yaml` ID） |
| 2 | 4 个 SandboxProvider + 降级链 | `SandboxProvidersPlugin.cs:39-42`、`SandboxManager.cs:138-140` | ①②③④ | 降级链变配置；新增 provider 只加文件 | 低 |
| 3 | 8 QueryService + ProviderDefinition | `LlmProvidersPlugin.cs:29-39`、`ProviderDefinitionRegistry.cs:107-115` | ①②③④ | 新协议从「10 个文件」降到「1 yaml + 1 适配器」；消掉两份不对称注册表 | 中 |
| 4 | 9 个 Agent 角色档案 | `AgentRoleProfileRegistry.cs:222` | ①②③④ | 角色=数据；工具白名单改 yaml；顺带修 `subagent_type` 断链 | 中 |
| 5 | 能力：todo / cron+goal / memdir / dream 插件 | `EngineServiceContext.cs:8-58`、`McpInitModule.cs:79-90` | ①②③④ | profile 可关能力（headless 关 todo/dream）；`EngineServiceContext` 由节点表生成 | 中高 |
| 6 | 20 个 CLI 子命令 | `CliSubCommand.cs` + `FlatSubCommandRouter.cs:14-49`（16 case） | ②③④ | 子命令表与帮助/dump 同源；可按环境裁剪 | 中 |
| 7 | 635 工具 / 580 `[McpTool]` | 生成器 + `cli_groups.yaml` 影子体系 | ②③④ | 与节点表合并，`tool-dispatch` 死声明转活 | 高 |
| 8 | 4 个 `tool-dispatch:*` 死声明 | `base.yaml:428-434` | — | 接上或删除，消除唯一双向漂移 | 极低 |

---

## 6. 实施顺序（每步独立可验收，不要求一次做完）

| 步 | 内容 | 验收（真机 exe） | 风险 |
|---|---|---|---|
| **S1** | L1 契约字段 + L2 一致性门禁（现有 165 声明 contract 默认 middleware，零行为变更） | `--dump-nodes` 显示 contract；故意删一个工厂 → 门禁红；176/669 零回归 | 低 |
| **S2** | guard 域上节点（清单第 1、2、8 项） | dump 可见完整守卫链且顺序 = `orders.yaml`；`Verify-BashGuard.ps1` 全绿；关一个守卫后真机拒绝行为消失 | 中（安全域，必须逐条回归） |
| **S3** | provider + role 上节点（第 3、4 项） | 新协议用 yaml 接入并真机出话；角色白名单改 yaml 后真机生效 | 中 |
| **S4** | 能力 + 命令表面（第 5、6 项） | profile 关 todo/dream 后工具不出现；`jcc -h` 与 dump 同源 | 中高 |
| **S5** | L3 外部 profile 覆盖 | 改磁盘 yaml 的 priority，不重编即生效；H₀ 基线仍可整体回退 | 中 |

---

## 7. 四问（每项调研前必答）

**Q1 专家隐性实践**：顶尖平台工程会做哪些用户没要求的事？
- 装配一致性门禁（声明↔实现双向比对）—— 本工程缺，`tool-dispatch` 即是漏网标本；
- 契约变更的爆炸半径评估（改 `contract` 语义 = 改所有消费点）；
- 灰度/可回滚的装配变更（对应 L3 外部覆盖 + TASK098 句柄回滚）。

**Q2 原始素材清单**：本项从哪些最原始素材入手？
- DSH 一手：`Context`/`Service`/`ctx.effect()`/`cordis.patch.yml` 的源码语义（不看我方总结稿）；
- Harness² 一手：`SurfaceSpec` 的字段表与 `write_tree` 的先删后写语义；
- 本工程一手：`--dump-nodes` 真实输出、`base.yaml` 与 `DataDrivenPipelineComposition.cs` 的逐行比对、832 处 `[Register]` 的实际装配路径。

**Q3 具体方法与工具**：
- 静态：`F("...")` 与 yaml 的双向差集脚本（本文 §1.3 已产出一版）；
- 动态：`JCC_DEBUGLOG=1` 打点核对真实装配序（如 `[ORDER] command_guards:`）；
- 回归：`Verify-BashGuard.ps1`、`Verify-ExplicitOrder.py`、`Verify-RegistrationOrder.py`、635 工具树。

**Q4 独立验证来源**（除 AI 与用户外，什么能证明结果正确）：
- 真机 `jcc.exe` 输出（dump 顺序、拒绝行为、工具数）；
- CI AOT 产物实跑（`daily-aot-publish.yml` 三 job）；
- `orders.yaml` 与运行期 `[ORDER]` 打点的逐字比对（顺序唯一真相）；
- 现存 1103 测试作为回归网（不作验收挡箭牌）。

---

## 8. 验收标准（S1 起逐条累加）

1. `jcc --dump-nodes` 每个消费点显示 `contract`，且 165 个声明 100% 有对应工厂。
2. `Verify-NodeConsistency.py` 在 CI 中运行；故意删除任一工厂 → 失败退出。
3. guard 链在 dump 中可见，顺序与 `orders.yaml` 完全一致；`Verify-BashGuard.ps1` 全绿。
4. 635 工具、99 斜杠命令、1103 测试零回归。
5. `JCC_PROFILE_DIR` 指向的磁盘 yaml 改 priority 后，不重编即生效；删除该目录即回 H₀。
6. 任一新增扩展点（守卫/provider/角色/能力）**不再需要改 `BuildNodeFactories()`**。

---

## 9. 真实边界（明确不做，避免误判为已完成）

- **不引入 `NodeBase` / 节点类层次**（沿用 TASK097 G3 裁决：数据驱动，`INode` 保持 3 属性）。
- **不把 AOT 源生成器改成反射**（AOT 是第一约束）。
- **不追求「万物皆节点」字面完成**：渲染/UI/终端层、纯库（`pithosdb`）不节点化 —— 它们是叶子，不是装配单元。
- **不动 635 工具契约**（`CliToolDescriptor` / `ToolAliasTable` 已稳定）。
- **S1 不改任何现有行为**：`contract` 缺省 = `middleware`，165 个声明不改一行。
- `tool-dispatch` 死声明：**先由门禁暴露，再决定「接上」还是「删除」**，本文不预设结论。

---

## 10. 进度

| 步 | 内容 | 状态 |
|---|---|---|
| 0 | 普查与本文档 | ✅ |
| S1 | 契约字段 + 一致性门禁 | ⬜ |
| S2 | guard 域节点化 | ⬜ |
| S3 | provider + role 节点化 | ⬜ |
| S4 | 能力 + 命令表面节点化 | ⬜ |
| S5 | 外部 profile 覆盖 | ⬜ |
