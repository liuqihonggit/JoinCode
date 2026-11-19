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

---

## 11. 契约字段规格（S1 施工单，零行为变更）

### 11.1 数据模型改动

| 类型 | 文件 | 改动 |
|---|---|---|
| `ConsumerConfig` | `modules/abstractions/composition/node/profile/ConsumerConfig.cs:7` | 新增 `string Contract`，默认 `"middleware"`（位置参数之后带默认值，避免改动全部构造点） |
| `ResolvedConsumerConfig` | `modules/abstractions/composition/node/profile/ResolvedProfile.cs:14` | 同上，携带契约供运行期与 dump 使用 |
| `NodeProfileResolver` | `modules/composition/composition/node_registry/profile/NodeProfileResolver.cs` | 解析 `contract:` 键；子 profile 省略即继承父的契约 |
| `NodeProfileLoader` | `.../profile/NodeProfileLoader.cs` | 未知 `contract` 值 → 解析失败并报错（Rust 风格：列出合法值） |

**YAML 形态**（唯一新增键，全部现有 profile 零改动）：

```yaml
consumers:
  chat-pipeline:
    contract: middleware     # 可省略，省略即 middleware
    nodes: [...]
```

**合法契约值**（首版 6 个，收敛在单一常量类，与 `NodeContract` 枚举一一对应）：

| 值 | 组合语义 | 消费者 |
|---|---|---|
| `middleware` | 顺序叠加（priority 升序） | 16 条 `MiddlewarePipeline<T>` |
| `guard` | 顺序叠加（priority 降序），首结论胜 | `CommandInterceptionDispatcher` |
| `provider` | 互斥选择（按协议/能力） | `QueryServiceFactory` |
| `role` | 互斥选择（按 role+variant） | `AgentRoleProfileRegistry` |
| `capability` | 存在即提供 | `EngineServiceContext` |
| `command-group` | 分类聚合 | `ChatCommandRegistry` / category node |

### 11.2 `--dump-nodes` 输出扩展

每个消费点首行输出契约，便于人工核对：

```
  消费点: chat-pipeline  [contract=middleware, 14 节点]
  消费点: guard-chain    [contract=guard, 8 节点]
```

同时新增汇总行：`契约分布: middleware=21, guard=1, provider=1, role=1`。

### 11.3 一致性门禁规则（`Verify-NodeConsistency.py`）

| # | 规则 | 失败示例 |
|---|---|---|
| 1 | 每个 `consumer:node` 声明必须有工厂 | `tool-dispatch:shell` 无工厂 → 失败 |
| 2 | 每个工厂必须至少被一个 profile 声明 | 孤儿工厂 → 失败 |
| 3 | `contract` 必须是合法值 | `contract: middlewares` → 失败 |
| 4 | `contract: provider/role` 的消费点，节点 ID 不得重复 | 同名两 provider → 失败 |

门禁读取方式：`jcc --dump-nodes --json` 输出结构化清单（新增 `--json`，AOT 源生成 DTO），脚本与 CI 用同一份输出做三方比对，避免脚本自己解析 C#。

### 11.4 S1 验收

1. `jcc --dump-nodes` 21 个消费点全部显示 `contract=middleware`，669 放置不变。
2. `--dump-nodes --json` 输出可被脚本解析；门禁 `--verify` 当前报 **4 个 `tool-dispatch` 违规**（预期失败，作为首个待修项）。
3. 1103 测试零回归；`jcc tool tree` 635 不变。

---

## 12. 分域实施细则

> 统一命名：节点 ID 用 kebab-case，前缀表域（`git-commit-guard` / `anthropic-adapter` / `defense-strict-parse`），与现有 `-mw` 风格一致。

### 12.1 guard 域（S2）

**现状精确清单**

| 族 | 条目 | 现状位置 |
|---|---|---|
| 命令守卫 | `GitCommitGuard` / `DriveProtectionGuard` / `CmdIndirectCallGuard` / `RobocopyMirrorGuard` / `HeredocGuard` / `GhPrBodyGuard` / `GhTimeoutGuard` / `VpnRouteGuard` | `orders.yaml:21-29` 定序；`[Register(typeof(ICommandGuard))]` 收集 |
| 工具修正 hook | `GhPrBodyFixHook` / `JsonFixHook` / `GhTimeoutFixHook` | `orders.yaml:35-38` + `ToolFixHookRegistry.cs:39-43` 手写 new |
| BashDefense 链 | `Begin` → `CheckDangerousCommand` → `StrictParse` → `CheckRetainedDevice` → `CheckRedirectWhitelist` → `CheckGitPushCiGuard` → `RequireArgvHash`（1+6 步） | `ShellCommandInterceptionMiddleware.cs:106-113` 硬编码 `.Then()` |
| 沙箱提供器 | `Soft` / `Process` / `Docker` / `Bubblewrap`，降级链 `Bubblewrap > Process > Soft` | `SandboxProvidersPlugin.cs:39-42`、`SandboxManager.cs:138-140` |

**目标形态**：新增 3 个 `contract: guard` 消费点（`command-guard-chain` / `tool-fix-chain` / `defense-chain`）+ 1 个 `contract: provider` 消费点（`sandbox-providers`，降级链用 `priority` 表达）。

**迁移映射**：`orders.yaml` 的列表顺序 → profile `priority`（首项最高 = `800`，逐项 −100）。**`orders.yaml` 保留为生成期校验源**，避免两套顺序打架（门禁比对二者一致）。

**三处重复名单收敛**：`DI 集合收集` / `orders.yaml` / `InterceptFlagDetector` 三份 → 收敛为「profile 声明为唯一装配源，`orders.yaml` 降级为门禁校验表，`InterceptFlagDetector` 改为读取节点 `CanHandle` 声明的能力标签」。

**验收**：dump 可见完整守卫链且顺序 = `orders.yaml`；`Verify-BashGuard.ps1` 全绿；profile 去掉一个守卫后真机对应拒绝行为消失、其余不变。

### 12.2 provider 域（S3）

**现状**：`LlmProvidersPlugin.cs:29-39` 注册 5 个 + default；`ServiceRegistration.cs:15-36` 又注册一份（含 Hub，插件没有）；`ProviderDefinitionRegistry.cs:107-115` 协议 `switch` 决定 Definition。

**目标**：`llm-providers` 消费点，`contract: provider`，节点 = `anthropic-adapter` / `azure-adapter` / `agnes-adapter` / `responses-adapter` / `jev-adapter` / `openai-adapter`(default) / `hub-adapter` / `pipe-adapter`。选择依据从 `ProtocolKind` 枚举改为节点声明的 `meta.capabilities`（如 `protocol:anthropic`），枚举只留作兼容常量。

**收益**：新协议 = 1 个适配器文件 + 1 条 profile 声明；两份不对称注册表合并为一份；未知协议不再静默降级（无匹配节点 → 明确报错并列出可用节点）。

**验收**：现有 8 厂商配置逐个真机出话；删一条 provider 声明 → 启动报「无匹配 provider」且提示可用列表。

### 12.3 role 域（S3）

**现状**：`AgentRoleProfileRegistry.cs:222` 9 条（1 Coordinator + 8 Executor 变体），工具白名单/黑名单/模型/温度全在 C#。

**目标**：`agent-roles` 消费点，`contract: role`，每档一个节点，`config` 携带 `allowed-tools` / `disallowed-tools` / `model` / `when-to-use`。同时修 `subagent_type` 断链（当前永远拿不到对应档案）。

**验收**：`--dump-nodes` 列出 9 档；改 profile 里某档 `allowed-tools` 后真机 spawn 的工具面随之变化。

### 12.4 能力域（S4）

**现状**：`EngineServiceContext.cs:8-58` 13 个字段手工暴露；`McpInitModule.cs:79-90` 8 插件硬编码；`[Register(typeof(IWorkflowPlugin))]` 已注册却不用于装配。

**目标**：`capabilities` 消费点，`contract: capability`，节点 = `todo` / `task` / `cron` / `goal` / `memdir` / `dream` / `code-index` / `vision`。`EngineServiceContext` 改为由节点表构造（`GetServicesForConsumerAsync<ICapabilityNode>("capabilities", ...)`），插件清单由 `contract: capability` 的节点驱动。

**验收**：profile 去掉 `todo` 后 `todo` 工具不出现且 `EngineServiceContext.TodoService` 为 null；去掉 `dream` 后 dream 任务不再自动注册。

### 12.5 命令表面（S4）

**现状**：`CliSubCommand` 20 值 + `FlatSubCommandRouter.cs:14-49` 16 case + `ApplicationBuilder` 2 个 if。

**目标**：`cli-commands` 消费点，`contract: command-group`，每值一个节点（ID = 枚举值的 kebab 形式），router 改为按节点表查执行器；`-h` 与 dump 同源。

**验收**：20 条命令全部可 dump；profile 去掉 `probe` 后 `jcc probe` 报「该命令未在当前 profile 启用」而非「未知子命令」。

### 12.6 工具分组与死声明（S1 内先修）

`tool-dispatch` 4 个声明：**删除**（推荐）或接 4 个 `contract: capability` 工厂。S1 门禁会先让它可见，再由 §12.6 处置；635 工具契约本身不动。

### 12.7 外部 profile 覆盖（S5）

`NodeProfileLoader` 增加覆盖层读取顺序：

```
内嵌 base.yaml（H₀）
  → $JCC_PROFILE_DIR/<name>.yaml（磁盘 delta，可选）
  → 经验 patch（Round，仅 config/priority）
```

合并走 `DeltaComposer`（`DeltaLifetime.Permanent`），不新增覆写逻辑；磁盘层缺失即回到内嵌基线。AOT 下为纯数据解析，无反射。

**验收**：磁盘 yaml 改 `stream-rate-limit-mw` 的 `max-requests` 后，`jcc probe` 基线随之变化；删除目录后回 30。

---

## 13. 风险与回滚

| 风险 | 影响 | 对策 |
|---|---|---|
| 契约语义改动波及 165 声明 | 装配顺序变化 | 契约缺省 = middleware；S1 不改任何声明 |
| guard 域安全回归 | 漏拦/误拦 | 逐守卫真机对照（拒绝/改写/放行三态）；`Verify-BashGuard.ps1` 必跑 |
| 双份注册表合并漏实现 | 某协议不可用 | 合并后逐协议真机出话，缺一即失败 |
| 门禁误报 | CI 阻塞 | 门禁只校验「声明↔工厂↔契约值」，不做语义校验 |
| 外部覆盖被滥用 | 环境漂移 | 覆盖层可整体丢弃；启动时打印 `[PROFILE] source=embedded+disk` |

回滚：每步独立提交，S1 只加字段与门禁，回滚即恢复 `contract` 默认值。

---

## 14. 进度

| 步 | 内容 | 状态 |
|---|---|---|
| 0 | 普查与本文档（含 §11 施工单、§12 分域细则） | ✅ |
| S1 | 契约字段 + dump 扩展 + 一致性门禁 | ✅ |
| S2 | guard 域节点化：S2a 守卫链 ✅ / S2b 修正器 ✅ / S2c 沙箱 ✅ | ✅ |
| S3 | provider + role 节点化 | ✅ |
| S4 | 能力 + 命令表面节点化 | ✅ |
| S5 | 外部 profile 覆盖 | ✅ |

### 14.1 S1 实施记录

**代码**

| 文件 | 改动 |
|---|---|
| `modules/abstractions/composition/node/profile/NodeContract.cs` | 新增：6 个契约常量 + `All` / `IsValid` / `Normalize`（唯一清单，错误提示复用） |
| `.../profile/ConsumerConfig.cs` | 新增 `string? Contract = null`（null = 未声明，交由继承链） |
| `.../profile/ResolvedProfile.cs` | `ResolvedConsumerConfig` 新增 `Contract = NodeContract.Middleware`（解析后必为具体值） |
| `modules/composition/composition/node_registry/profile/NodeProfileLoader.cs` | 解析 `contract:`，非法值报错并列出合法清单 |
| `.../profile/NodeProfileResolver.cs` | 缺省 middleware；includes 合并时「子省略即继承父契约」 |
| `modules/apps/cli/core/commands/core/NodeDumpReport.cs` | 新增：dump JSON DTO + AOT 源生成上下文 |
| `modules/apps/cli/core/commands/core/Program.cs` | `--dump-nodes` 先于参数解析处理；显示 `[contract=…, N 节点]` 与契约分布；`--json` / `--verify` |
| `non_deliverables_tools/scripts/Verify-NodeConsistency.py` | 新增门禁：dump 三方一致 + 契约合法 + 磁盘 profile 全部入 dump |
| `.github/workflows/ci-unit-tests.yml` | 新增门禁步骤 |
| `resources/profiles/base.yaml` / `sdk.yaml` | 删除 5 条 `tool-dispatch` 死声明（0 工厂 0 消费方） |
| `modules/composition/composition.tests/node_registry/NodeContractTest.cs` | 新增 6 个测试（缺省/继承/覆写/非法值） |

**真机验收**

| 项 | 结果 |
|---|---|
| `jcc --dump-nodes` | 176 节点；消费点显示 `[contract=middleware, N 节点]`；`契约分布: middleware=80` |
| `jcc --dump-nodes --verify` | `[OK] 声明 ↔ 工厂 ↔ 契约 三方一致`，退出码 0 |
| 负向验证 | 临时把 `tool-dispatch` 2 条声明塞回 base.yaml → 门禁 `[FAIL] 死声明: tool-dispatch:file, tool-dispatch:shell`，退出码 1；还原后复绿 |
| `jcc --dump-nodes --json` | `nodeCount=176 / consumerCount=80`，verification 三数组全空 |
| 零回归 | 1117/1117 测试（1111 + 6 新增）；`jcc tool tree` 635；`--dump-profile sdk` 正常 |
| 意外收获 | `--dump-nodes` 此前必须走完整参数解析，`--json/--verify` 会被判未知选项；改为先于 ParseArgs 处理后 CI 可直接用 |

### 14.2 S2a 实施记录（守卫链）

**做法**：守卫链的执行顺序从 `orders.yaml` 序号改为 profile 消费点 `command-guard-chain`（`contract: guard`），`orders.yaml` 退化为校验表。

| 文件 | 改动 |
|---|---|
| `modules/guard/guard.hooks/execution/core/NodeChainOrder.cs` | 新增：按 profile 排任意节点链；kebab ID ↔ Pascal 实现名比对；profile 缺失退回 Priority；实现缺失fail-closed抛错；实现未声明追加链尾并打点 |
| `.../interception/core/CommandInterceptionDispatcher.cs` | 构造改为 `NodeChainOrder.Order(...)`，新增可选 `IProfileManager` 参数 |
| `modules/composition/pipelines/registration/DataDrivenPipelineComposition.cs` | 新增 8 个 `command-guard-chain:*` 工厂 |
| `resources/profiles/base.yaml` | 新增 `command-guard-chain`（8 守卫，priority 800→100） |
| `non_deliverables_tools/scripts/Verify-NodeConsistency.py` | 新增第 6 条校验：profile 顺序 ↔ `orders.yaml` 顺序必须一致 |

**真机验收**

| 项 | 结果 |
|---|---|
| `jcc --dump-nodes` | 184 节点（176 + 8 守卫）；`消费点: command-guard-chain [contract=guard, 8 节点]` |
| 运行期顺序 | `jcc mcp_call bash` + `JCC_DEBUGLOG=1` → `[ORDER] command_guards: GitCommitGuard > DriveProtectionGuard > … > VpnRouteGuard`（= profile 声明） |
| 拦截真实生效 | `mcp_call bash --command "git commit -m …"` → `execution_failed`：禁止通过 shell 直接执行 git commit |
| 顺序对账门禁 | 故意交换 `orders.yaml` 前两项 → `[FAIL] 守卫顺序两套事实不一致`，退出码 1；还原后复绿 |
| 零回归 | Guard.Hooks.Tests 598 全绿；Composition.Tests 1117；`tool tree` 635 |

**已知边界**：从 profile 删除某守卫不会让它失效 —— 实现存在但未声明时按安全优先追加到链尾（打点告警），同时 CI 门禁会报「工厂无声明」。即：**运行期绝不静默丢守卫，配置漂移由 CI 拦**。真正的「关闭某守卫」留给 `enabled-when`（S2b 一起做）。

### 14.3 S2b 实施记录（工具修正器链）

| 文件 | 改动 |
|---|---|
| `modules/guard/guard.hooks/execution/fix_hooks/ToolFixHooks.cs` | 3 个修正器加 `[Register(typeof(IToolFixHook))]` |
| `.../execution/core/ToolFixHookRegistry.cs` | 构造注入 `IEnumerable<IToolFixHook>` + `IProfileManager`，`NodeChainOrder` 排序；删除 `RegisterDefaultFixHooks`（硬编码 new 三个）；`Register` 改为「已声明段之后按 priority 降序插入」 |
| `modules/guard/guard/plugins/FixHooksPlugin.cs` | **删除**（只做 `new` 的插件已无用），同步移除 `McpInitModule` 清单项 |
| `resources/profiles/base.yaml` | 新增 `tool-fix-chain`（`contract: guard`，3 节点，priority 300/200/100） |
| `.../DataDrivenPipelineComposition.cs` | 新增 3 个 `tool-fix-chain:*` 工厂 |
| `ToolFixHookRegistryTest.cs` | 两个用例改为构造注入（`RegisterDefaultFixHooks` 已删） |

**真机验收**

| 项 | 结果 |
|---|---|
| `jcc --dump-nodes` | 187 节点 / 88 消费点；`tool-fix-chain [contract=guard, 3 节点]` |
| 宿主装配（`mcp_serve` + `JCC_DEBUGLOG=1`） | `[ORDER] tool_fix_hooks: GhPrBodyFixHook > JsonFixHook > GhTimeoutFixHook` —— DI 真的注入 3 个、顺序来自 profile |
| 引擎启动 | 删除 `FixHooksPlugin` 后宿主正常启动（无残留引用） |
| 零回归 | Guard.Hooks.Tests 598；Composition.Tests 1117；门禁 `[OK]` |

**差评抓到的问题（值得记）**：首次构建后生成注册表仍引用已删除的 `FixHooksPlugin`、且 `IToolFixHook` 一个都没注册 —— 原因是 `Guard.csproj` 未重编，生成器扫的是**旧的 Guard.dll**。重编 Guard → guard.hooks → `Composition -t:Rebuild` 后，`FixHooksPlugin` 残留 0、`IToolFixHook` 注册 12 处正常。**教训与 TASK098 同源：编译成功 ≠ 生成物更新，跨库改动必须确认生成代码内容。**

### 14.4 S2c 实施记录（沙箱提供器）— 当时部分完成，后由 §12.8 拆墙闭环（见 12.8.5）

**已完成**

| 文件 | 改动 |
|---|---|
| `modules/guard/guard.sandbox/providers/*.cs` | 4 个提供器加 `[Register(typeof(ISandboxProvider))]` |
| `modules/guard/guard/plugins/SandboxProvidersPlugin.cs` | **删除**（只做 4 连 `new`），同步移除 `McpInitModule` 清单项与 `Core.Plugins` 全局 using |
| `modules/guard/guard.sandbox/core/SandboxManager.cs` | 构造注入 `IProfileManager`，降级链改读 `_fallbackOrder`（profile 消费点 `sandbox-providers`，缺失时退回 OS 默认）；`AddProvider` 仍保留供运行期热插 |
| `modules/abstractions/composition/node/chain/NodeChainOrder.cs` | 从 guard.hooks **下沉到契约层**（S3/S4 都要用），命名空间 `JoinCode.Abstractions.Composition` |
| `Guard.Sandbox.csproj` | 补 `Abstractions.Composition` 引用 |

**真机验收**：`ISandboxProvider` / 4 个提供器类型出现在 `JoinCode.Composition.dll`（生成注册 16 处），`SandboxProvidersPlugin` 残留 0；`mcp_call bash` 正常、`tool tree` 635、门禁 `[OK]`、Composition 1117 + Guard.Hooks 598 全绿。

**阻塞（重要，影响 S3/S4）**

`SandboxManager` 的 profile 降级链已就绪，但**节点声明加不回去** —— 因为 `BuildNodeFactories()` 集中在 `Pipelines`，要写 `F("sandbox-providers:soft-sandbox-provider", sp => sp.GetRequiredService<SoftSandboxProvider>())` 就必须让 Pipelines 引用 `Guard.Sandbox`；实测该程序集不在 Pipelines 的编译闭包内（`ISandboxProvider` 都不可见），补 `ProjectReference` 又触发 restore（违反本仓「改动不 restore」纪律）。

这不是沙箱独有的问题：**176 个节点的工厂全部集中在一个静态方法里，任何"实现不在 Pipelines 引用闭包内"的域都无法成为节点**。S3（provider 在 `llm/core`）、S4（能力在 `storage`/`services`）会撞同一堵墙。

**因此 S3/S4 的前置条件是「节点工厂的分布式贡献」**（新增 §12.8），而不是继续往 Pipelines 堆引用。

### 14.5 新增施工项：§12.8 节点工厂的分布式贡献

目标：每个域自己贡献工厂，Pipelines 只做汇总，不再认识具体类型。

```
[NodeFactory("sandbox-providers", "soft-sandbox-provider")]   // 域内标注
→ 源生成器产出 INodeFactoryContribution（域程序集内）
→ Pipelines 汇总：BuildNodeFactories() = 编译期 catalog（生成物）+ 现有 161 条
```

约束：AOT 友好（源生成，非反射）；与 `--dump-nodes --verify` 的门禁口径一致（声明 ↔ 工厂 ↔ 契约）；现有 161 条手写工厂可原样保留，逐步迁移。

这一步做完，S3/S4 才能在不改依赖图的前提下推进。

#### 12.8.1 三个可选架构（必须先定，再动手）

| 架构 | 机制 | 优点 | 代价 |
|---|---|---|---|
| **A. DI 聚合**（推荐） | `[NodeFactory]` → 源生成 `INodeFactoryContribution` 实现 + 走现有 `ServiceRegistration` 生成器注册 → 宿主 `sp.GetServices<INodeFactoryContribution>()` 汇总 | 与现有 832 处 `[Register]` 同一条通道；AOT 友好；域零改依赖 | `--dump-nodes` 当前用空容器，必须改成「建真实宿主再 dump」，诊断命令变重（需实测启动耗时） |
| B. 宿主聚合 | 每个域出 `static XxxNodeFactories.Create()`，由 CLI/Engine 汇总 | 不依赖 DI，dump 仍是纯静态 | CLI 与 GUI 各汇总一次 → 两套清单漂移（正是本任务要消灭的 R4）；且要求宿主引用所有域 |
| C. 反射扫描 | 启动时扫程序集找 `[NodeFactory]` | 零生成器代码 | AOT 直接不可用（第一约束），且启动变慢；与「全静态工厂」原则冲突 |

**结论：走 A，但按「静态目录」裁掉它的容器依赖（实测见 12.8.3）。** 理由：本仓 AOT 是第一约束（G7 已验证），B 会复制出第二份装配清单，C 不可用；而 A 的「建真实宿主再 dump」只是为了拿一份**键列表**，代价与收益不成比例。

#### 12.8.2 施工结果：A′ = 静态目录 + 宽闭包持有（已完成）

与实际计划的差异（诚实记录，三处）：

| 原计划 | 实际做法 | 为什么改 |
|---|---|---|
| `INodeFactoryContribution` + 走 DI 注册 | 生成器直接产出 `internal static class NodeFactoryCatalog.Create()` | 贡献物是**静态键列表**，不是服务。走 DI 会把 `--dump-nodes` 逼成"建真实宿主"，且 `GetServices<>` 属于运行期解析，对静态清单没有收益 |
| `Pipelines.BuildNodeFactories(sp)` 汇总 | 由 `Composition.NodeFactoryContribution.Create()` 持有，`Pipelines` 只 `foreach` 转发 | 见 12.8.3：`Pipelines` 的编译闭包看不到 `Guard.Sandbox`，`Composition` 看得到。目录必须住在闭包最宽的那个程序集 |
| 特性放 `Abstractions.Composition` | 放 `Abstractions.Primitives.Attributes`（命名空间 `JoinCode.Abstractions.Attributes`） | 后者几乎是全仓默认 global using，各域标注不需要改 `GlobalUsings.cs`；与 `[Register]` 同类同层 |

落地形态：

```
域内标注：[NodeFactory("sandbox-providers", "soft-sandbox-provider")]
  → NodeFactoryCatalogGenerator 按编译单元静态聚合
  → Composition 持有全量目录（internal NodeFactoryCatalog → public NodeFactoryContribution）
  → Pipelines.BuildNodeFactories() 末尾 foreach 转发
```

防漂移：生成器内置 `JCC4013`（同编译单元内同键重复）、`JCC4014`（跨程序集同键不同实现）两条编译期错误。

#### 12.8.3 关键实测：那堵墙是真的，但方向被判断错了

S2c 当时的结论是"`Guard.Sandbox` 不在 Pipelines 的编译闭包里，需要补 `ProjectReference`"。这次把它验证到确定：

- 用 `Core.Security.Sandbox.Providers.SoftSandboxProvider`（正确命名空间）→ `CS0234`；
- 换成 `global::Core.Security.Sandbox.Providers.SoftSandboxProvider` → 仍然 `CS0234`；
- 但 `Composition` 的生成注册里**本来就写着** `Core.Security.Sandbox.Providers.SoftSandboxProvider`（S2c 实测 `JoinCode.Composition.dll` 16 处），且编译通过。

即：同样的引用路径 `Pipelines → Composition → Guard → Guard.Sandbox`，**`Composition` 看得到、`Pipelines` 看不到**。所以问题不是"缺引用"，而是"工厂目录住进了闭包较窄的那个程序集"。修法因此不是加 `ProjectReference`（那会把装配层绑到叶子实现上，还要 restore），而是**把目录搬到闭包最宽的程序集**——`Composition` 作为装配根，本来就该认识所有实现。这一条同时解释了 S2c 为什么"降级链就绪却加不回节点声明"。

#### 12.8.4 验收口径

| 项 | 结果 |
|---|---|
| `jcc --dump-nodes` | 191 节点 / 92 消费点；`sandbox-providers [contract=provider, 4 节点]`（bash-defense-chain 落地后为 197/96，见 12.8.6） |
| `jcc --dump-nodes --verify` | 声明无工厂 / 工厂无声明 / 互斥内重复 三项全 `[OK]` |
| 门禁 `Verify-NodeConsistency.py` | `[OK] 191 节点 / 92 消费点（guard=8, middleware=80, provider=4）` |
| 真机降级链 | `mcp_call bash` 宿主日志 `[ORDER] sandbox_providers(profile): bubblewrap > process > soft > docker` —— 顺序来自 `base.yaml` 而非 OS 默认（Windows 默认是 `process > soft`） |
| 零回归 | `tool tree` 635；Composition 1117；Guard.Hooks 598；4 个 python 门禁 + 工具节点门禁全绿 |
| `--dump-nodes` 重量 | 未变（仍是静态目录，不建容器） |

#### 12.8.5 S2c 收尾：沙箱提供器真正成为节点

拆墙后把 S2c 剩下的"声明加不回去"补齐，顺手把降级链的顺序来源落到 profile。

| 文件 | 改动 |
|---|---|
| `abstractions.primitives.attributes/src/NodeFactoryAttribute.cs` | **新增** `[NodeFactory(consumer, nodeId)]`（放特性层，各域不改 GlobalUsings） |
| `generators/mcp_tool_dispatch.generator/NodeFactoryCatalogGenerator.cs` | **新增** 源生成器：按编译单元静态聚合 `[NodeFactory]` → `internal static class NodeFactoryCatalog.Create()`；`JCC4013`/`JCC4014` 两条防漂移错误；空集合必出 `yield break`（否则非迭代器，CS0161） |
| `composition/dependency_injection/core/NodeFactoryContribution.cs` | **新增** 公开汇聚点，把生成目录交给装配层 |
| `composition/pipelines/registration/DataDrivenPipelineComposition.cs` | `BuildNodeFactories()` 末尾 `foreach` 转发贡献（签名未变，调用点零改动） |
| `guard.sandbox/providers/*.cs` | 4 个提供器加 `[NodeFactory("sandbox-providers", "…-sandbox-provider")]` |
| `guard.sandbox/core/SandboxManager.cs` | 排序改用**全量**提供器（含本平台不可用者）：声明缺实现仍 fail-closed，但"实现在、本平台不可用"不再阻断启动；加 `[ORDER] sandbox_providers(...)` 打点 |
| `resources/profiles/base.yaml` | 新增 `sandbox-providers`（`contract: provider`，bubblewrap 400 / process 300 / soft 200 / docker 100） |

顺序设计说明：profile 只表达**偏好**，可用性过滤在消费侧做（`_providers` 只收 `IsAvailable`，降级循环还会 `ContainsKey` 复查）。因此一份跨平台清单即可：Windows 上 bubblewrap 不可用会被跳过，实际顺序退回 `process > soft`，与历史行为一致；docker 排最后是保持"只供显式指定、不进自动降级"的既有语义。

这一步之后 `provider` 契约第一次真实出现在 dump 里（`provider=4`），S3 的 `provider` 与 `role` 有了可复制的落地样板。
#### 12.8.6 S2c 另一半：Bash 防御链从「硬编码 `.Then()`」变成节点链

原来这 6 步写死在 `ShellCommandInterceptionMiddleware.EvaluateBashDefenseAsync` 的链式调用里——**改顺序、去掉某一步，都要重编译**，正好撞上四问里的第 ④ 条（不改代码改配置）。

| 文件 | 改动 |
|---|---|
| `guard.hooks/.../defense/BashDefenseService.cs` | 新增 `ConsumerId = "bash-defense-chain"`；步骤表 `nodeId → BashDefenseStepAsync` 就地登记；`OrderChain()` 用 `NodeChainOrder.Order` 按 profile 排序并打 `[ORDER] bash-defense-chain: …`；新增 `ExecuteChainAsync(...)` 与 `StepOf(nodeId)`；构造注入可选 `IProfileManager`；无 profile 时退回 `DefaultStepOrder`（与历史顺序逐字一致） |
| `hands_shell/.../ShellCommandInterceptionMiddleware.cs` | 6 行 `.Then(...)` 换成一次 `ExecuteChainAsync(...)`，`BashDefense` 构建器语义未动（仍在内部 `Begin().Then().ExecuteAsync()`） |
| `pipelines/registration/DataDrivenPipelineComposition.cs` | 6 条 `bash-defense-chain:*` 工厂，指向 `BashDefenseService.StepOf(id)` |
| `resources/profiles/base.yaml` | `bash-defense-chain`（`contract: guard`，dangerous 600 → argv-hash 100） |
| `abstractions/composition/node/chain/NodeChainOrder.cs` | `KebabToPascal` 由 `internal` 改 `public`——各消费点把节点 ID 存在自己小表里时，身份归一必须与 `Order` 的匹配规则同源，否则两份命名规则迟早漂移 |

**节点身份怎么保证不漂移**：`OrderChain` 的 `nameOf` 直接用 `NodeChainOrder.KebabToPascal(entry.Id)`，与 `Order` 内部算期望名的函数是同一个函数，因此不可能出现「表里写 `dangerous-command`、匹配时算成 `DangerousCommand2`」这类偏差。

**真机验收**：
- `--dump-nodes`：`bash-defense-chain [contract=guard, 6 节点]`，总数 197 / 96 消费点，`guard=12`；
- `--dump-nodes --verify` 三项全 `[OK]`；门禁 `[OK] 197 节点 / 96 消费点`；
- `mcp_call bash --command "echo ok"` 宿主日志 `[ORDER] bash-defense-chain: dangerous-command > strict-parse > retained-device > redirect-whitelist > git-push-ci-guard > argv-hash`；
- 防御仍真实生效（重构没有把链变成空转）：未闭合引号 → `strict-parse` 拒绝；`format C: /q` → `dangerous-command` 拒绝；
- 零回归：`tool tree` 635；Composition 1117；Guard.Hooks 598；4 个 python 门禁 + 工具节点门禁全绿。
## 15. S3：provider 契约落地（LLM 协议实现）

### 15.1 为什么这一批不是「加个 `[NodeFactory]` 就完事」

审计里 S3 记的是「8 个 QueryService、两个不对称注册表」。打开看是：

- `ServiceRegistration.EnsureDefaultProvidersRegistered()` 里手写 5+1 条 `RegisterProvider`
- `LlmProvidersPlugin.InitializeAsync()` 里**又手写一遍**同样的 5 条
- 两者写同一个静态 `s_factory` → 同一份清单两个真相，改一处忘一处不会报错（R4 的教科书样本）

而且这些 QueryService **不是 DI 单例**：它们按 `ProviderConfig` 逐次构造（一次请求一份配置）。所以不能直接给它们挂 `[NodeFactory]`，也不能为了节点化把它们改成单例——那会动 LLM 构造链这条热路径。

### 15.2 做法：节点包装层，热路径一行不动

`Api.LLM.QueryServices.Nodes`：

| 类型 | 作用 |
|---|---|
| `IProtocolQueryServiceNode` | 一个节点 = 一种 `ProtocolKind` 的实现方式；`Kind` + `Create(config, http, logger, fs, executor)` |
| 7 个节点类 | `openai-compatible` / `anthropic` / `azure` / `agnes` / `openai-responses` / `jev` / `hub`，各自封装 `new XxxQueryService(...)`（Hub 的构造参数差异封装在节点内） |
| `LlmProviderNodes.All` | **唯一清单**；注册表、profile 校验、节点工厂都从它派生 |
| `LlmProviderDeclaration` | profile 声明的**启用集合**（纯数据契约，不依赖 `IProfileManager`） |

LLM 层不认识 profile：装配层（Composition）读 profile → 构造 `LlmProviderDeclaration` 注入 → `CreateQueryService` 创建前 `AssertEnabled(ProtocolKind)`。

为什么 `provider` 契约的 priority 在这里不表达顺序：同协议内互斥，没有「先试谁」的问题（沙箱那边有降级顺序，所以它读 priority）。声明只表达**启用集合**。

**不静默退回**是刻意的：没声明的协议直接拒绝，而不是落到 default factory。否则「Anthropic 的配置被按 OpenAI 格式发出去」这类事故会被藏起来。

### 15.3 真机红→绿（这次的红灯抓到真 bug）

第一版只声明了 6 个节点，漏了 `openai-compatible`——而 `openai` / `deepseek` / `sensenova` / `agnes` 这些 vendor 全部映射到它。真实运行立刻炸：

```
JCC_ALLOW_DIRECT_FALLBACK=1 JCC_VENDOR=openai jcc chat --prompt hi
→ System.InvalidOperationException: [LLM-PROVIDER-DISABLED] 协议 'OpenAiCompatible' 未在 profile 消费点
  'llm-providers' 中声明…当前已启用: Agnes, Anthropic, Azure, Hub, Jev, OpenAiResponses
```

补上 `openai-compatible` 节点后同一命令：

```
[LLM] providers registered: openai-compatible, anthropic, azure, agnes, openai-responses, jev, hub (+ default)
[LLM] provider declaration from profile 'llm-providers': openai-compatible, anthropic, azure, agnes, openai-responses, jev, hub
✖ API 错误  未配置 API Key，LLM 调用将失败
```

`LLM-PROVIDER-DISABLED` 计数 0，走到「未配置 API Key」——即协议校验通过、真正进入供应商调用路径（本机没有 key，这是预期终点）。

**如果只写单测不跑真机，这个漏项不会暴露**：单测里不会有 `JCC_VENDOR=openai` 这条最常用的路径。

### 15.4 分布式工厂在这里兑现了价值

7 个 `[NodeFactory]` 加在 `Llm` 程序集里，**`DataDrivenPipelineComposition.BuildNodeFactories()` 一行没改** —— 生成器在 Composition（宽闭包）里自动收集。这是 §12.8 的直接回报：S4 的能力/命令面可以照此办理，不必再回装配层堆代码。

### 15.5 验收

| 项 | 结果 |
|---|---|
| `jcc --dump-nodes` | **204 节点 / 100 消费点**；`llm-providers [provider, 7 节点]`、`sandbox-providers [provider, 4 节点]` |
| `--dump-nodes --verify` | 三项全 `[OK]` |
| 门禁 `Verify-NodeConsistency.py` | `[OK] 204 节点 / 100 消费点（guard=12, middleware=80, provider=8）` |
| 真机 red→green | 见 15.3（红灯是真 bug，不是彩排） |
| 零回归 | `tool tree` 635；Composition 1117；Llm **613**；Guard.Hooks 598；Engine / Dream 编译通过 |
## 16. S3 后半：role 契约（9 份内置 Agent 角色档案）

### 16.1 形态上与 provider 不同，因为对象不同

`AgentRoleProfile` 是**数据**（AllowedTools/DisallowedTools/Prompt 等），不是服务；9 份档案原本只存在于
`BuildBuiltInProfiles()` 的一个大数组里。所以做法是：

- **档案数据一行不动**：`BuildBuiltInProfiles()` 仍是唯一来源，节点只是「按 ID 取视图」
- 节点 = `IAgentRoleNode`（`NodeId` + `Profile`），9 个类各持一个 `public const string Id`
- `[NodeFactory(AgentRoleNodes.ConsumerId, Id)]` 直接用类自己的 const → **一个类只有一个字面量**，特性与属性不可能漂移
- 启停由 profile 消费点 `agent-roles`（`contract: role`）决定；`AgentRoleDeclaration` 是纯数据（放 primitives 层，各宿主可见）

### 16.2 语义差异（刻意的）

| 契约 | 缺失时行为 | 为什么 |
|---|---|---|
| `provider`（LLM 协议） | **抛错拒绝** | 缺协议会把 A 协议的配置按 B 协议格式发出去，必须响 |
| `role`（角色） | **不登记，查询返回 null** | 角色缺失只是少一个可选能力；`GetProfile` 本来就允许返回 null，调用方已处理 |

### 16.3 打点抓到第二个命名 bug

第一版把节点 ID 由枚举的 `[EnumValue]` 派生，日志立刻显示只启用了 7 个：

```
[ROLES] built-in enabled: coordinator, executor-code, ..., executor-verification   ← 少了 2 个
```

原因是 `ExecutorVariant` 的两个枚举值是 camelCase（`claudeCodeGuide` / `contextCompression`），
拼出来的 ID 是 `executor-claudeCodeGuide`，与 profile 里的 kebab 声明对不上。
改为由枚举**名称** kebab 化（`JoinCodeGuide → join-code-guide`）后 9 个全部匹配。
**教训与 S2c/§12.8 同源：这类「声明 ↔ 实现」的错位不会编译报错，只能靠运行期打点或门禁抓。**

### 16.4 双向验证（不是只验证「全开」）

只证明「启用 9 个」等于没验证过滤逻辑。所以做了临时负向实验——从 profile 删掉 2 条：

| 观察点 | 结果 |
|---|---|
| 宿主日志 | `[ROLES] built-in enabled:` 只剩 **7 个**，被删的两个确实未登记 |
| `--dump-nodes --verify` | `工厂无声明: [FAIL] agent-roles:executor-doctor, agent-roles:executor-join-code-guide` |

即：过滤真的生效，**门禁也真的会反向报孤儿工厂**（不是单向摆设）。恢复后三项全 `[OK]`。

### 16.5 验收

| 项 | 结果 |
|---|---|
| `jcc --dump-nodes` | **213 节点 / 104 消费点**；`agent-roles [contract=role, 9 节点]` |
| 契约分布 | `guard=12, middleware=80, provider=8, role=4` —— `role` 契约首次出现在 dump |
| `--verify` + 门禁 | 三项全 `[OK]`；`[OK] 213 节点 / 104 消费点` |
| 真机 | `[ROLES] declaration from profile 'agent-roles': <9 个>` → `[ROLES] built-in enabled: <同 9 个>` |
| 零回归 | `tool tree` 635；Composition 1117；Llm 613；Engine 编译通过 |
| 未跑 | `Agents.Tests` 与 `Pipelines.Tests` 从未 restore，按纪律未执行（断言均为数量/存在性，不涉及新参数） |
## 17. S5：外部 profile 覆盖（让「改配置不重编」全局成立）

### 17.1 卡点

profile 只存在于 `EmbeddedResource`（`Composition.csproj` 编译时嵌入），`NodeProfileLoader` 也只读 manifest。
所以前面四个契约虽然都能被 profile 调，但**调 profile 本身要重编译** —— 四问的第 ④ 条（不改代码改配置）在全局层面一直是假的。

### 17.2 做法与语义

`JCC_PROFILE_DIR` 指向一个目录：

- **同名文件整体覆盖**内嵌版本，新名字则新增
- 目录不存在 / 没有任何 `.yaml` / 某个文件解析失败 → **一律抛错**（配置写错要响，不能静默退回内嵌）

为什么不做字段级深合并：yaml 本身已经给了三个局部改写机制（`inherits` / `overrides` / `excludes`），
想要「只改一个消费点」就写个 5 行小文件 `inherits: base`，比隐式深合并可预测得多。
隐式合并的坏处是没人能说清最终生效的是哪份字段。

### 17.3 与 DeltaComposer 的关系（计划在这里修正了一处）

原计划写的是「S5 用 `DeltaComposer`」。实际动手发现二者是**串联而非替代**：

```
外部 yaml（S5，定义 H₀ 基底） → NodeProfileResolver → ResolvedProfile
                                                   ↓
                              DeltaComposer（TASK098，叠加经验/经验 patch）
```

`DeltaComposer` 的键级覆写原语处理的是**运行期经验 patch**，让它去合并 yaml 文件等于把两个生命周期混成一层。
所以 S5 只做「文件级装载」，`DeltaComposer` 一行没改 —— 这比硬套更符合「一个原语只管一类覆写」。

### 17.4 JCC9001 冲突与解法

分析器禁止在 Composition 里直接触盘（`Directory.EnumerateFiles` / `File.ReadAllText` 均报错）。
没有用 `[SuppressMessage]` 绕，而是给 `LoadFromResources` 加 `IFileSystem?` 参数：

| 调用点 | 传什么 |
|---|---|
| `Composition.ServiceRegistration` / `Pipelines.AddDataDrivenPipelines` | `sp.GetService<IFileSystem>()` |
| CLI 的 4 个诊断入口（`Program` ×2、`ExperienceCommand` ×2、`ProbeCommand`） | `new PhysicalFileSystem()`（CLI 的 csproj 已 `NoWarn JCC9001`，且语义上它就是替用户读盘） |

没传 `IFileSystem` 却设了环境变量 → 抛 `[PROFILE-DIR-NO-FS]` 并说明处置，不静默忽略。

### 17.5 真机验收

**正向（覆盖真的生效，且不用重编译）**：外部 `base.yaml` 把 `process`/`soft` 的优先级对调后：

```
（内嵌）  docker=100, soft=200, process=300, bubblewrap=400
（外部）  docker=100, process=200, soft=300, bubblewrap=400
```

**负向（fail-closed）**：

```
JCC_PROFILE_DIR=D:\no\such\dir jcc --dump-nodes
✖ 致命错误  [PROFILE-DIR-NOT-FOUND] 环境变量 JCC_PROFILE_DIR 指向的目录不存在: D:\no\such\dir。
            处置: 修正路径，或删除该环境变量以使用内嵌 profile。
```

**门禁两种模式都跑**：默认 `[OK] 213 节点 / 104 消费点`；带覆盖同样 `[OK] 213 节点 / 104 消费点`
（节点集合未变，只有优先级变化，符合预期）。

**行为未退化**：`format C: /q` 仍被 `dangerous-command` 拒绝、未闭合引号仍被 `strict-parse` 拒绝。

### 17.6 已知缺口（已于 S4 查清并关闭）

原记录：本轮之后 `[ORDER] sandbox_providers` / `[ORDER] bash-defense-chain` 这类打点在 `mcp_call` 路径下**看不到了**
（`[LLM]` / `[ROLES]` 仍在）。

**S4 结论：不是被吞掉，是那些服务在 `mcp_call` 进程里从来没被构造。**

证据（同一次真机运行 `jcc -p "hi"`，走完整引擎装配）：

```
[ORDER] sandbox_providers(profile): bubblewrap > process > soft > docker
[ORDER] bash-defense-chain: dangerous-command > strict-parse > retained-device > redirect-whitelist > git-push-ci-guard > argv-hash
[ORDER] command_guards: GitCommitGuard > DriveProtectionGuard > ... > VpnRouteGuard
```

根因：`bash` 工具属于「独立 exe 实现」（`jcc-tool-shell.exe`），`jcc mcp_call bash` 只是把参数转给子进程，
jcc 自己的 `ShellCommandInterceptionMiddleware` / `SandboxManager` / `BashDefenseService` 一次都没被解析 ——
它们的 ctor 打点当然不会出现。`[LLM]`/`[ROLES]` 之所以在，是因为那两条在容器构建/插件加载时就跑了。

判据（以后别再误判）：**打点缺失先问「这个对象在当前路径上会不会被构造」**，而不是先怀疑日志层。
`mcp_call` 是「子进程代理」路径，凡是活在宿主管道里的服务都不该期待在那里出现。


---

## 18. S4：能力（capability）与命令表面（command-group）

### 18.1 这两种契约为什么一起做（它们的共同点与中间件相反）

中间件、守卫都是「按顺序叠加」——节点之间互相影响，顺序是语义的一部分。
`capability` 与 `command-group` 是另一类：**节点之间互不影响，问的只是「它在不在」**。
把这两类硬塞进顺序模型会得到一堆 priority 无意义的声明；反过来，把「在不在」也当成顺序问题，
就会出现「关掉一个能力」需要删 6 行、且不知道删干净没有。

两者还共享同一个坑：**只声明不消费就是死配置**。S1 删掉的 `tool-dispatch` 4 条就是标本。
所以 S4 的验收不是「dump 里看得到」，而是「改 profile → 真机行为真的变了」。

### 18.2 先抽一个语义基元：`ConsumerDeclaration`

新增 `modules/abstractions/composition/node/profile/ConsumerDeclaration.cs`，只有三条规则：

| 情形 | 语义 |
|---|---|
| profile 里没有这个消费点 | **全开**（老 profile 不必逐条补声明） |
| 消费点存在 | **白名单**：只有列出的节点启用 |
| 需要顺序时 | `OrderedIds()` 按 priority 降序给一份确定顺序 |

CLI 门禁、能力视图都只调这一个入口 —— 否则「CLI 认为全开、引擎认为全关」是迟早的事。

### 18.3 命令表面：20 个枚举值 → 18 个节点

**原状**：`CliSubCommand`（20 值）、`ApplicationBuilder` 的 2 个 `if`（schema / remote-control 家族）、
`FlatSubCommandRouter` 的 16 个 `case`。三处各自新增，谁也不知道少没少。

**改法**：

1. `CliCommandNodes.cs`：`ICliCommandNode`（`Command` + `Aliases` + `ExecuteAsync`）+ 18 个节
   点类，每个 `[NodeFactory("cli-commands", id)]` + `[Register]`，执行体全部委托**既有**实现（热路径零改动）。
2. 节点 ID = **枚举名称 kebab 化**（`McpCall` → `mcp-call`）。不用 `[EnumValue]`：那是命令行字面量
   （`mcp_call` 带下划线），拿它当 ID 会污染 kebab 约定 —— 同一个坑 S3 角色域踩过一次。
3. 别名（`rc`/`remote`）不是节点，是节点的 `Aliases`：否则三条命令会被算成三个节点。
4. `ApplicationBuilder.RunSubCommandAsync` 变成「枚举 → 节点表 → profile 门禁 → 执行」，`switch` 与 `if` 全删。
5. `FlatSubCommandRouter` 只保留解析工具（位置参数/未知选项/输出格式），`TryExecuteAsync` 删除。

**未启用 ≠ 未知**（这是可运维性的关键区别）：

```
$ jcc probe                                    # 正常
[PROBE-H0] 真实请求 60 次 → allow=30 deny=30 ...
$ JCC_PROFILE_DIR=<去掉 probe 的 profile> jcc probe
子命令 'probe' 未在当前 profile 启用（profile=base，消费点=cli-commands，节点=probe）。
  恢复用法: 在 resources/profiles/base.yaml 的 cli-commands 里加回 '- id: probe'。
（退出码 2 = ConfigurationError）
```

**一个必须处理的结构问题**：生成器对 Exe 只扫自身程序集，而命令执行器是 CLI 里的 `internal static`，
搬不到下游库。于是 CLI 程序集自己的 `NodeFactoryCatalog_Jcc` 不在装配层（Composition）的目录里 ——
`--dump-nodes` 必须**显式合并**这两段，否则 18 条声明会全被报成「声明无工厂」。
这不是绕过门禁，而是让门禁看见「实现住在哪一层」。

### 18.4 能力：从「属性存在」变成「节点存在」

**原状**：`EngineServiceContext` 26 个可选属性无条件 `GetService<T>()`；`McpInitModule` 硬编码 6 个插件。
于是「headless 关掉 todo/dream」只能改代码。

**改法**：

1. `CapabilityNodes.cs`（Composition 程序集）：7 个节点
   `goal / todo / task / cron / memdir / dream / code-index`，各带 `[NodeFactory("capabilities", id)]`。
2. `CapabilitySet`（`[Register]` 单例）：每次判断都读 `IProfileManager.Current` 而非快照 ——
   profile 支持运行期切换，快照会把切换变成「下次启动才生效」。无 profile 的轻量宿主走 `AllEnabled`，历史行为不变。
3. `EngineServiceContext.FromServiceProvider`：goal/todo/task/cron/memdir/dream 六个能力各自门禁；
   未启用解析为 `null`，消费方天然走「能力不可用」分支，而不是拿到服务却装作没有。
4. `McpInitModule.StartPluginLoads`：`DreamPlugin` ← `dream`，`CSharpLanguagePlugin` ← `code-index`；
   其余 4 个插件（Telemetry / AgentRoles / LlmProviders / McpInit）属装配骨架，不做能力。

**双面真机验证**（`jcc -p "hi"` 走完整引擎装配，假 key 止于 401，但装配全跑完）：

```
（全开）  [CAPS] todo=on task=on cron=on goal=on memdir=on dream=on code-index=on
          [CAPS-PLUGIN] dream=load code-index=load
（裁剪）  [PROFILE] external override: base <- ...\jccprof\base.yaml
          [CAPS] todo=off task=on cron=on goal=on memdir=on dream=off code-index=on
          [CAPS-PLUGIN] dream=skip code-index=load
```

`[CAPS]` 这一行本身就是交付物之一：配置到底生效了没，不该靠读 yaml 猜。

### 18.5 三个坑（都值得记住）

| 坑 | 表现 | 判据/处置 |
|---|---|---|
| 陈旧产物冒充「我的 bug」 | `jcc mcp_list` 报 `Could not load type 'Core.Utils.AsyncLock'` | 那次只重编了上游，下游 DLL 还是 TASK099 之前的。带依赖重编一次即消失 —— **局部编译会让旧产物在运行期报「类型不存在」**，别先怀疑自己刚改的代码 |
| 能力注册了但没有消费点 | 想到 `vision` 时没有 `IVisionService` 之类可选服务被引擎消费 | 只登记「真有消费方」的能力。登记 `vision` 只会造一条假节点 —— 假节点比缺节点更糟 |
| `internal static` 执行器搬不进宽闭包 | 门禁报 18 条「声明无工厂」 | 不是放宽门禁，而是把 CLI 的生成目录显式并入 dump 的工厂清单 |

### 18.6 验收数字与仍未做的部分

- `--dump-nodes`：**238 节点 / 112 消费点**；契约分布 `capability=4, command-group=4, guard=12, middleware=80, provider=8, role=4`
  —— **六个契约值全部有活的消费点**，第 ④ 条判据（改配置不重编）从此对每一类扩展点成立。
- `--verify` 三项全 `[OK]`；`Verify-NodeConsistency.py` `[OK] 238 节点 / 112 消费点`；
  `Verify-ExplicitOrder` / `Verify-RegistrationOrder` / `Verify-ToolNodes`（635）/ `Verify-HonestyChecklist` 全绿。
- 真机：18 条子命令逐条走过（`schema`/`config-print`/`mcp_*`/`slash_*`/`tool`/`prompt`/`experience`/`probe`）；
  `rg`/`gh` 已正确路由到节点，但本机 Debug 目录没有 `jcc-rg.exe`/`jcc-gh.exe`（需 `Pack-ToolNodes.ps1` 打包，与本次改动无关）。

**未做（诚实记录）**：

1. **工具面 gating**：`capabilities` 目前只门禁服务与插件。`todo` 工具仍会出现在工具表里
   （`TodoToolHandlers` 是 `[McpToolDispatch]` 生成的静态注册，`ITodoService` 在 DI 里也还在）。
   「profile 去掉 todo → todo 工具不出现」属清单第 7 项（635 工具 / capability-aware 注册表），
   与 S4 的 5、6 两项不是同一层，不在这里假装做完。
2. `vision` 未注册（见 18.5）。
3. 帮助文本仍由 `[SubCommandInfo]` 生成，未改成从节点表读 —— 二者目前一致，但仍是两处描述。

---

## 19. S4 收尾：能力打通到工具注册表（清单第 7 项）

### 19.1 上一轮留下的「只关一半」

§18.6 未做第 1 项写的就是这件事：`profile` 里去掉 `todo`，`ITodoService` 确实为 `null`，
但 `todo` 工具还挂在工具表上 —— 调进去才发现服务不在。配置看起来生效了，实际只生效在某一层，
这类「半生效」比完全不生效更难查：现象是「工具报错」，而人会去查工具，不会去查 profile。

所以这一轮不加新契约、不加新节点、不加新 profile 词汇，只把 **`capabilities` 的消费深度**补齐：
同一条声明，现在同时治理 **服务解析 / 插件加载 / 工具注册** 三层。

### 19.2 数据从哪来（不改生成器）

关键发现：源码生成器早就生成了 `GetAvailableToolCategories()`（分类 → 工具清单），
而它算分类用的正是 `tool.Category ?? handler.DisplayName` —— 与 `[McpToolDispatch(ToolCategory.Xxx)]`
的 `[EnumValue]` 字符串同一坐标系。于是「工具名 → 分类」可以直接反查，不必：

- 不必改生成器（635 条映射不新增生成代码）；
- 不必反射（分类在编译期已知）；
- 不必读 `modules/tools/cli_nodes/groups/*/node.yaml`（Composition 禁触盘，JCC9001）。

新增 `CapabilityToolCategories`：分类 → 治理它的能力（6 条），并把生成物翻成两张只读索引
（`CategoryByTool`、能力 → 工具数）。

### 19.3 只登记真被治理的分类

| 能力 | 工具分类 | 工具数 |
|---|---|---|
| `goal` | `goal` | 3 |
| `todo` | `todo` | 4 |
| `task` | `task` | 12 |
| `cron` | `cron` | 4 |
| `memdir` | `memory` | 13 |
| `code-index` | `code_index` | 23 |
| `dream` | —— | 0 |

合计 **59**。`dream` 不进表：它是引擎后台任务，没有暴露工具 ——
填一条假映射会让「关掉 dream 工具却没少」变成新的谜题，宁可留空并在 dump 里不标注。

### 19.4 过滤怎么接（与既有 CLI 过滤是「与」关系）

`ServiceRegistration.Mcp.cs` 里原本只有一条来自 `EngineApplicationBuilder` 的 `toolFilter`
（`SkipChatServices` 时只留 `gh_*`）。现在组合成：

```csharp
ComposeToolFilter(cliFilter, gate)
  = gate 全开        → 原样返回 cliFilter
  = cliFilter 为空   → gate.IsToolEnabled
  = 两者都有          → name => cliFilter(name) && gate.IsToolEnabled(name)
```

**都全开时返回 `null`** —— 生成代码据此走「不过滤」快路径，未配置能力裁剪的宿主与改动前逐字节等价
（真机 `filter=none`、工具数 368 与改动前一致，就是这条的证据）。

三条保守规则防「静默少工具」：生成物里查不到的工具 → 放行；分类不归任何能力 → 放行；
分类被多个能力共享 → 任一开启即放行。

### 19.5 真机验收（账目必须对得上）

`jcc -p "hi"` 走完整引擎装配（假 key 止于 401，但装配全跑完）：

```
（基线）  [TOOL-GATE] indexed=368 off=[-] blocked=0 samples=[-] filter=none
          MCP Tool Handlers 注册完成，共 368 个工具

（只关 todo）
          [TOOL-GATE] indexed=368 off=[todo] blocked=4 filter=active
          samples=[complete_step,todo_update,todo_list,todo_write]
          MCP Tool Handlers 注册完成，共 364 个工具          ← 368-4

（关 6 项：goal/todo/task/cron/memdir/code-index）
          [TOOL-GATE] indexed=368 off=[goal,todo,task,cron,memdir,code-index] blocked=59 filter=active
          samples=[memory_team_status,memory_scan,search_callers,search_project_nugets,memory_daily_log_get,task_update]
          MCP Tool Handlers 注册完成，共 309 个工具          ← 368-59
```

`blocked` 与实际减少数**逐次相等**（4/4、59/59）—— 这不是巧合，是同一份索引算出来的；
若两处各算一遍，迟早会出现「说裁了 59、实际裁了 57」。工具总数 368 与生成物条目数、去重后工具数
三者一致（368/368/0 重复），说明索引没有漏项也没有重叠。

行为闭环（同一 profile 下）：

| 入口 | 结果 |
|---|---|
| `jcc mcp_list` | 309 条，`category=todo` 的工具 0 个 |
| `jcc mcp_call todo_write` | `未找到工具: todo_write（用 jcc mcp_list 查看已注册工具）` |
| `jcc mcp_call read file_path=...` | 正常返回文件内容 |

### 19.6 `--dump-nodes` 看得见治理面

节点自省新增 `[tools=N]`（只对 capability 契约、且 N>0 时输出）：

```
消费点: capabilities  [contract=capability, 7 节点]
  - code-index (priority=100) [tools=23]
  - dream (priority=200)
  - memdir (priority=300) [tools=13]
  - cron (priority=400) [tools=4]
  - task (priority=500) [tools=12]
  - todo (priority=600) [tools=4]
  - goal (priority=700) [tools=3]
```

7 个数加起来 59，与 `[TOOL-GATE] blocked=59` 同源。`dream` 没有标注 —— 它确实不治理工具面，
不写 `tools=0` 是因为那看起来像「这个节点坏了」，而事实是「这个能力没有工具面」。

### 19.7 明确的边界（不假装）

| 边界 | 事实 | 为什么这样 |
|---|---|---|
| `jcc tool call <工具>` 仍能执行被裁工具 | 该通道是 CLI 直连执行层（`jcc-tool-*.exe` / 本地执行），不经宿主 `IToolRegistry` | 它是调试/自检通道，`tool call` 的语义就是「绕过宿主直接打执行层」；能力门禁管的是 Agent 能看到与能调用的工具面 |
| `jcc mcp serve` 在裁剪 profile 下也少工具 | 组合过滤不特判 `SkipToolFilter` | profile 是配置意愿，优先级高于「CLI 侧只留 `gh_*`」的性能优化；若需要全量，profile 里声明全部能力即可 |
| 工具树 `jcc tool tree` 仍显示 635 | 它读 `modules/tools/cli_nodes/groups` 的**声明目录**，不是运行时注册表 | 两者本就不同层：635 = 声明（含 388 个独立 exe），368 = 宿主实际可注册（`[McpToolDispatch]`） |

补充一个数字关系（容易被「635」误导）：生成物里 `github` 分类只有 **1** 个条目，而 `tool tree` 的
github 组有 173 个 —— 那 173 个是独立 exe 的 CLI 节点，经 `jcc gh` / `tool call` 直达，
**不在宿主注册表，也就不在能力门禁覆盖范围内**。所以本节的 59 个是「Agent 可见工具」的裁剪量，
不是「635 全量」的裁剪量。388 个 exe 工具多数不属于这 7 个能力（github/media/slash 等），
按能力归类对它们没有语义。

### 19.8 零回归数字

- `--dump-nodes`：**238 节点 / 112 消费点**不变（本轮不加节点）；`--verify` 三项 `[OK]`。
- `Verify-NodeConsistency`（238/112）、`Verify-ExplicitOrder`、`Verify-RegistrationOrder`、
  `Verify-ToolNodes`（635「声明与现实一致」）、`Verify-HonestyChecklist`、`Verify-NoLockPrimitives` 全绿。
- `Composition.Tests` 1117 通过；Composition / CLI 编译 0 警告 0 错误。
- `Verify-StyleGate` 存量 247 条不变，新增/修改文件零新增违规。
- 真机：基线 368 工具且 `filter=none`（与改动前一致）；只关 todo 364；关 6 项 309；`mcp_call` 闭环三态。

---

## 20. 帮助文本与节点表同源（§18.6 未做第 3 项）

### 20.1 上一轮只是「执行时」认得 profile，帮助还不认

S4 让命令分发按 `cli-commands` 门禁判启停，但帮助文本仍由 `[SubCommandInfo]` 生成的静态表输出。
真机（外部 profile 删掉 `probe`）：

```
$ jcc -h 融合门禁
融合门禁:
  probe    行为对比门禁 — ...
$ jcc -h probe
probe — 行为对比门禁 — ...
```

而执行 `jcc probe` 会报「未在当前 profile 启用」。—— 用户看到命令、点进去才被拒，等于两次浪费。
这不是「潜在的隐患」，是当场可见的不一致。

### 20.2 改法：给生成的帮助方法加一个可见性参数

`EnumMetadataGenerator` 生成的 `CliSubCommandHelpText` 四个方法
（`GetCategories` / `GetByCategory` / `GetByCommand` / `GetHelp`）各加一个
`Func<string, bool>? isEnabled = null`（默认 `null` = 全开，历史行为不变），并生成一个私有判定：

```csharp
private static bool __IsVisible(SubCommandEntry e, Func<string, bool>? isEnabled)
    => isEnabled is null || isEnabled(e.IsAlias ? (e.AliasOf ?? e.Command) : e.Command);
```

**别名跟随主命令**：`rc` / `remote` 不是独立节点，`remote-control` 被裁掉时它们也该消失。

三处细节都是「不做就会错」的：

1. **分类判定也要用过滤后的集合** —— 否则某个分类的命令全被裁掉后，它仍被当成「已知分类」返回一张空表。
   `GetHelp` 里改的是 `Any(e => 分类匹配 && __IsVisible(...))`，不是只过滤列表。
2. **未启用 ≠ 未知** —— `GetByCommand` 对不可见命令返回
   「命令 'probe' 未在当前 profile 启用（cli-commands 消费点未声明该节点）」，而不是「未知命令」。
   两者处置方式完全不同：前者去改 profile，后者去查拼写。
3. **默认参数保兼容** —— 四个方法都带默认值，其它调用方零改动。

### 20.3 踩到的坑：帮助用的是「字面量」，节点用的是「kebab」

第一版直接传 `CliCommandGate.IsEnabled`（按节点 ID 判），真机结果是**只删了 `probe`，却少了 4 个分类 10 条命令**：

```
（应只剩 10 类，实际 6 类）
  缺失: MCP 工具(5) 引擎服务(1) 斜杠命令(3) 融合门禁(1)
```

原因：帮助表里的命令名是 CLI 字面量（`mcp_call` 带下划线），节点 ID 是枚举名 kebab（`mcp-call`）。
`isEnabled("mcp_call")` 在白名单语义下当然是 `false`。

修法不是「把节点 ID 改成下划线」（会污染 S4 定下的 kebab 约定，S3 角色域已经踩过一次），
而是在门禁里加一层换算，且**只在这一处换算**：

```csharp
public static bool IsEnabledLiteral(string literal) {
    var command = CliSubCommandExtensions.FromValue(literal);
    if (command is null) return true;              // 未识别字面量交给常规「未知命令」路径
    var node = CliCommandNodes.For(command.Value);
    return node is null || IsEnabled(node.NodeId);
}
```

一句话记：**同一个概念出现两套词（字面量 / kebab）时，换算点必须只有一处，且要在最靠近「词」的那层做。**

### 20.4 真机三态 + 别名（外部 profile 驱动，无重编译）

| 场景 | 期望 | 实测 |
|---|---|---|
| 全开 `-h sub` | 10 个分类 | 10 个（含融合门禁） |
| 全开 `-h probe` | 命令详情 + 示例 | 正常 |
| 删 `probe` → `-h sub` | 融合门禁分类消失 | 9 类，融合门禁不在 |
| 删 `probe` → `-h 融合门禁` | `未知主题` | `未知主题: 融合门禁` |
| 删 `probe` → `-h probe` | 未启用提示 | `命令 'probe' 未在当前 profile 启用…` |
| 删 `probe` → `jcc probe` | 未启用 + 恢复用法 | 与帮助同一句措辞 |
| 删 `remote-control` → `-h rc` / `-h remote` | 别名随主命令消失 | 两者都报「未在当前 profile 启用」 |

### 20.5 零回归

- 生成器改动向后兼容（默认参数），全仓只有 `ApplicationBuilder` 两处调用，均已改为传门禁。
- `--dump-nodes` **238 / 112** 不变，`--verify` 三项 `[OK]`；`tool tree` 635 不变。
- NodeConsistency / ExplicitOrder / RegistrationOrder / HonestyChecklist / NoLockPrimitives 全绿；
  Composition.Tests 1117 通过；Composition / Utils / CLI 编译 0 警告。

**仍未统一的一处（诚实记录）**：`-h options` 的参数表来自 `CliArg` 特性（`CliArgParser.GetHelpText`），
与 `cli-commands` 消费点是两个体系 —— 参数级的启停目前没有节点语义，也不该硬套（参数不是命令）。

---

## 21. 运行期 Profile 切换的真实作用域（核实，并修正提示）

### 21.1 为什么要查这件事

S4 写 `CapabilitySet` 时给的理由是「每次读 `IProfileManager.Current`、不快照，保证运行期切换生效」。
**不快照只是必要条件，不是充分条件**：如果消费点本身在启动时只求值一次，读多少次 `Current` 都没用。
这句话如果不核实，就是一句听起来很对、实际无从验证的声明 —— 而这类声明比「没做」更容易误导。

### 21.2 代码证据（谁真的订阅了切换）

```
$ grep -rn "ProfileSwitching +=" modules
DataDrivenPipelineComposition.cs:568   Task 管道重建
DataDrivenPipelineComposition.cs:584   Stream 管道重建
```

只有这两处。其余各层都是**构造/启动时求值**：

| 层 | 求值时机 | 切换后 |
|---|---|---|
| Task / Stream 中间件管道 | 订阅 `ProfileSwitching` 后重建 | ✅ 跟随 |
| 命令守卫链 `CommandInterceptionDispatcher._guards` | 构造函数 `NodeChainOrder.Order` | ❌ 不变 |
| 工具修正器链 `ToolFixHookRegistry._hooks` | 构造函数 | ❌ 不变 |
| bash 防御链 `BashDefenseService._chain` | 构造函数 `OrderChain` | ❌ 不变 |
| 沙箱降级链 `SandboxManager._fallbackOrder` | 构造时 `BuildFallbackOrder` | ❌ 不变 |
| 能力（服务解析 / 插件加载 / 工具注册） | 启动时（DI 解析、插件加载、`RegisterAll`） | ❌ 不变 |
| 命令表面（`cli-commands`） | 每次进程启动时判定 | ❌ 本进程内不变 |

### 21.3 修正提示（不改行为，只改说法）

`/profile` 切换成功后原本只有一句「管道中间件列表已动态重建」—— 准确，但容易被读成「整条链路都换了」。
现在分两行说清楚：

```
已切换 Profile: base → headless
已重建: Task / Stream 中间件管道（订阅了 ProfileSwitching）
仍按启动时求值（改动需重启）: 能力（服务/插件/工具表）、命令表面、守卫/修正器/沙箱/防御链顺序
```

真机（`jcc slash_call /profile {"arguments":"headless"}`）即上述三行；`/profile list` 四个 profile 正常。

### 21.4 为什么不把其余层也做成动态

- **服务解析**是 DI 的本质：`GetService<T>` 返回 `null` 之后不会因为换了 profile 又变回来。
  要做就得重解析整个 `EngineServiceContext`，而它被几十个消费者持有 —— 那是一次重启，不是一次切换。
- **工具注册**可以做成 `ClearAsync` + 重新 `RegisterAll`，但运行中清空工具表会让并发中的工具调用拿不到 handler。
- **链顺序**（守卫/防御/修正器/沙箱）技术上可以每次请求重算，但那等于把「启动时确定性」换成「每请求读一次配置」——
  而这些链是安全边界，顺序抖动比「要重启」危险得多。

结论：**「要重启」不是实现缺陷，是这类配置的正确语义**。真正要做的是把它说清楚，而不是假装热更新。
