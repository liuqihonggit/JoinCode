# TASK097 节点继承缺口清单与补全计划（四问深度版）

> 状态: accepted（用户已裁决；阶段1 G2/G4 均已真机验收通过，下一步阶段2）
> 创建: 2026-11-19
> 裁决: 2026-11-19 — G3 走**数据驱动**（不引入 NodeBase，属性继承交给 G2 数据层）；补全节奏=**按阶段逐步验收**，每阶段真机 exe 跑通后再继续
> 关联: [DSG048 万物皆节点，节点皆可继承](../design/DSG048-node-plugin-inheritance-architecture.md)、[TASK072 四问深度模板](../../implemented/task/TASK072-基准对比调研代理提示词模板-四问深度版.md)
> 上游诉求: 用户核心目的 —— 学习 DSH「万物皆节点」+ Google Harness²「可继承」，设计成**节点继承**让 Agent 框架变轻松；要求①调查两者架构选择与细节 ②列出工程未完成的缺口 ③任何修改必须真机启动 exe 验收，不得用单元测试当挡箭牌。

---

## 0. 一句话结论

**骨架已搭好，"继承"只做了一半。** DSG048 标称 P1–P8 全部完成，但代码实况是：数据级继承（Profile）+ 节点统一抽象（INode/NodeRegistry）确实落地；而字面意义上的「节点级属性继承」「运行期可逆注册」要么缺失、要么直接抛异常。文档标签不能替代实际行为。

---

## 1. 四问深度法前置（答完才开工）

| 问 | 本项回答 |
|----|----------|
| **1 专家隐性实践** | ① 不读二手博客，直接 clone `deepseek-ai/deepseek-harness` 读 `docs/architecture.md` + `AGENTS.md`，clone `google-research/harness2` 读 arXiv 原文；② 不只看类定义，要真机切 profile 看合并结果（`--dump-profile`）；③ 区分「动态加载类型」与「运行期注册编译期已知类型」——AOT 下前者不可行、后者可行，混为一谈会误判 G1；④ 用行为对比而非单测断言可逆注册（注册→替换→卸载全链路计数）。 |
| **2 原始素材清单** | ① DSH `cordis` effect 语义（正向操作 + yield disposer，`DSG048 §2.1.3` 含一手代码片段）；② DSH `LlmAdapter` 抽象类（`DSG048 §2.1.2`）；③ Harness² 官方项目页 `harness2.github.io`（8 组件 + 两层递归 + H₀ 重置）；④ Harness² `SurfaceSpec` / Tree dict / Substrate Protocol（`DSG048 §2.2.1–2.2.4`）；⑤ 本仓 DSG048 决策表（`:819-824`）与 P8 总结（`:868-902`）。**禁止**只看 DSG048 的"✅已完成"标签。 |
| **3 具体方法与工具** | ① WebFetch 一手项目页 + 源码行号核对（`Read`/`Grep`）；② `git status` 确认基线干净；③ 编译只编改动域（`dotnet build ... --no-restore -p:BuildProjectReferences=false`）；④ 真机 exe：`JCC_DEBUGLOG=1` + 语义化打点标签 grep、`jcc --dump-profile`/`--dump-nodes`、`jcc tool call <组> <工具>`；⑤ 改契约层后重编 13 个 exe + `Pack-ToolNodes.ps1`。 |
| **4 独立验证来源** | ① 真机 exe 行为（**本次验收唯一标准**，非单测）；② DSG048 既有验收记录（作为对照，不作为结论）；③ Harness² 论文 benchmark 数据（成品继承导致 Code 73.4→63.2 回退，作为 G6 红线证据）；④ 跨 profile 组合矩阵（base/headless/ci/sdk 交叉切换）。 |

---

## 2. 两个理念的一手架构选择（用户诉求①）

### 2.1 DSH ——「万物皆插件」，继承 = 结构 + 契约

| 机制 | 原始证据 | 架构选择 |
|------|----------|----------|
| capability seam | `DSG048 §2.1.1` + dshai.org | Service 定义 + Provider + Consumer 三角色；消费方只认 `ctx(Service)`，不 import 实现 |
| 插件作为父类 | `DSG048 §2.1.2`（LlmAdapter 代码） | 抽象类 + 默认实现 + 选择性 `override` —— **代码层面继承** |
| 可逆注册 | `DSG048 §2.1.3`（ctx.effect 代码） | 正向操作 + yield disposer；插件卸载时逆序回滚，支持 `replace()` 原子替换 |
| 数据驱动组合 | `DSG048 §2.1.4` | bundle → profile → patch 分层，后写者胜（patch id 替换） |
| 两条铁律 | dshai.org / dev.to 源码阅读笔记 | everything is a plugin + every run is traceable |

### 2.2 Harness² ——「可继承」，继承 = 经验而非成品

| 机制 | 原始证据 | 架构选择 |
|------|----------|----------|
| 8 可编辑组件 | `harness2.github.io` | system prompt / guardrails / memory / tool guidance / subagents / skills / scripts / plugins，全部文件化挂载 |
| 任务级递归 | 同上 + arXiv:2610.14224 | Propose→Probe→Compose，靠**行为对比**而非 ground truth 决策 |
| 框架级递归 | 同上 | Improvement library 存 harness files + `EXPERIENCE.md`（cue/lesson/evidence 三字段） |
| **H₀ 重置红线** | 同上 + 论文 benchmark | **下个任务从公共基底 H₀ 重启，只通过 Propose 有选择复用经验，绝不自动继承成品 harness**；违反会触发 Code 73.4→63.2 / Web 69.9→65.0 回退 |
| 不用继承的适配 | `DSG048 §2.2.3` | Substrate 用 Protocol + SurfaceSpec 数据值，**不用基类** —— 与 DSH 路线分歧 |

### 2.3 关键张力（决定补全方向）

DSH 继承**结构/契约**（父类默认值 + patch 覆盖）；Harness² 继承**改进经验**，刻意拒绝继承成品配置。二者对"继承"的态度相反。本工程 DSG048 决策 8 已选边：**「数据层面继承为主，代码层面继承只在不可避免场景」**。补全时必须守住这条边，否则会同时引入两套继承机制。

---

## 3. 缺口清单（按依赖分层，逐项待裁决）

> 图例：🔴 缺失/受阻（实证） 🟡 名不副实（文档与代码矛盾） ⚪ 设计分歧（待用户拍板）

### G1 🔴 运行期可逆注册（受阻）

| 维度 | 内容 |
|------|------|
| 本仓证据 | `NodeRegistry.cs:9` `FrozenDictionary`；`:36-39` `Register()` 直接抛 `NotSupportedException("NodeRegistry is immutable...")` |
| 基准证据 | DSH `ctx.effect()`：`DSG048 §2.1.3` —— 注册返回 disposer，卸载逆序回滚，handle 支持 `replace()` |
| 差异描述 | DSG048 §1.1 用户需求 #4「随时组织某个插件，某些情况不加载」未被满足；注册表 build 后不可变 |
| 影响面 | 无法运行期装卸节点 → 动态组合能力缺失；`inherits` 变更必须重建整个注册表 |
| 原始素材 | DSH cordis effect 语义；DSG048 `:277` 明确把「可逆注册」列为参考模式 |
| 补全方案 | `Register()` 改为返回 `RegistrationHandle`（含 `Dispose()` + `replace()`）；`Unregister` 逆序执行 disposer。**AOT 边界**：只注册编译期已知的工厂，不做动态加载程序集 |
| **真机 exe 验收** | 启动 jcc 加 `JCC_DEBUGLOG=1`，在注册/回滚处打 `[REGISTER]`/`[DISPOSE]`；真机跑一次「注册→替换→卸载」，grep 计数与顺序；**禁止用单测断言替代** |

### G2 🔴 节点级属性/参数继承（最字面的一层，空）

| 维度 | 内容 |
|------|------|
| 本仓证据 | `file/node.yaml:13-39` tools 为扁平列表，无 `inherits` 字段；`CliToolDescriptor.cs:7-61` 是 record、无父节点指针；`ToolAliasTable` 只做**名字等价**（`CliToolDescriptor.cs:47-57`） |
| 基准证据 | DSH `cordis.patch.yml` patch id 替换（后写者胜）；Harness² Tree dict 省略即继承（`DSG048 §2.2.2`） |
| 差异描述 | 现有继承只在 **Profile 组合选择**层（`NodeProfileResolver`：`excludes`/`includes`/`overrides`，`:63-131`）；工具节点之间不能「继承父描述的属性/参数」 |
| 影响面 | "节点皆可继承"最核心的字面含义缺失；新增工具变体须整份复制 node.yaml 描述 |
| 原始素材 | Harness² `write_tree` 先删后写 + Tree dict 省略；DSH patch overlay |
| 补全方案 | node.yaml schema 增顶层 `inherits` + `overrides`/`excludes`；新增 `CliNodeProfileResolver` **复用 `NodeProfileResolver` 的合并语义**（排除→新增→覆写，深度≤3，循环检测），产出 `ResolvedToolDescriptor`；`NodeManifestLoader` 解析后先过 resolver |
| **真机 exe 验收（调研修正）** | ⚠️ 现有 `--dump-nodes` 只读 NodeProfile，**不读 CliToolDescriptor**（`Program.cs:231-253`），原方案不成立。须先扩展该命令输出 descriptor 的合并结果，再对比父子属性/参数；辅以 `jcc tool call file read --path <真实文件>` 验证继承来的默认值生效 |

### G3 ⚪ 是否引入统一 NodeBase（设计分歧，待拍板）

| 维度 | 内容 |
|------|------|
| 本仓证据 | `INode.cs:7-16` 仅 3 属性（NodeId/Metadata/State）；`Node.cs:6` 是 `internal sealed class Node<TService>` 适配器；`CliToolDescriptor.cs:7` 是 record —— 三者**不共享基类** |
| 基准证据 | DSH 走 `LlmAdapter` 抽象基类（`DSG048 §2.1.2`）；Harness² 走 Protocol + SurfaceSpec（`§2.2.3`），**证明不靠基类也能适配** |
| 差异描述 | 公共属性（Id/Metadata/Priority/EnabledWhen）在 record/接口重复声明；但 vs. "万物皆 node" 的公共基类诉求 |
| 影响面 | 涉及 635 工具的契约层；改错要重编 13 个 exe |
| 原始素材 | DSG048 决策 8（`:824`）「数据层面继承为主，代码层面只在不可避免场景」 |
| 补全方案 | **选项 A（DSH 式）**：引入 `NodeBase` abstract 收敛公共属性 + 默认行为；**选项 B（Harness² 式，推荐）**：不引入基类，公共属性收敛到 `INode` + `NodeMetadata`，属性继承走 G2 数据层 |
| **真机 exe 验收** | 无论选哪项，实跑 `jcc tool call <组> <工具>` 覆盖changed 组全部工具 + `jcc --dump-nodes`，确认契约未破裂 |

### G4 🟡 Metadata 硬编码（名不副实）

| 维度 | 内容 |
|------|------|
| 本仓证据 | `Node.cs:23-30` `Version: "1.0"` 写死，`Dependencies: []`/`Capabilities: []`/`RequiredCapabilities: []` 全空；`Priority: 0` |
| 文档矛盾 | DSG048 `:6` 称 P6 已验收，但 `:789` 标注「Metadata 硬编码 → 搁置到 P6」—— 内部自相矛盾 |
| 差异描述 | 元数据不可配置 → 按能力路由（capability-based routing）无法工作 |
| 影响面 | 无法用 metadata 做独立验证断言；节点自描述不完整 |
| 原始素材 | DSH Provider 提供 capabilities 供消费点发现 |
| 补全方案 | `NodeMetadata` 从 node.yaml / profile config 注入，而非 ctor 写死；至少 Version/Dependencies/Capabilities/Priority 可配 |
| **真机 exe 验收（调研修正）** | ⚠️ 现有 `--dump-nodes` 不输出 Metadata（`Program.cs:231-253` 数据源是 profile YAML，绕开 `INode.Metadata`），须扩展命令输出；输出必须是配置注入的真实值，而非 `"1.0"` / 空数组。另：`Node<TService>` 生产路径未被调用（`NodeEntry.Create<TService>` 无调用点），真实验收须走 `NodeRegistry.Build` 实际路径 |

### G5 🟡 DI 自动扫描未落地（名不副实）

| 维度 | 内容 |
|------|------|
| 本仓证据 | `NodeRegistry.cs:22-34` `Build` 只接收显式传入的 `customFactories` |
| 文档矛盾 | DSG048 `:477-483` 设计稿设想 `NodeRegistry.Build` 扫描 DI 全部服务；实际只收显式工厂 |
| 差异描述 | 新增服务须手动登记工厂，违背「万物皆节点」自动归纳 |
| 影响面 | 服务增加与节点注册两处维护，易漏 |
| 原始素材 | 本仓 `AppModule.Generator` 已有源码生成器先例 |
| 补全方案 | 用 source generator 生成「所有 `[Register]` 服务 → INode 工厂」catalog（编译期确定，满足 AOT）；`Build` 默认注入 catalog，`customFactories` 作覆写层 |
| **真机 exe 验收** | 打点 `[DI-SCAN]` 输出 catalog 条目数，与 `jcc --dump-nodes` 交叉核对；新增一个 `[Register]` 服务后实跑确认自动出现 |

### G6 🔴 经验继承 / PPC 循环（缺失，最后一步）

| 维度 | 内容 |
|------|------|
| 本仓证据 | 全仓无 `EXPERIENCE.md` / Propose-Probe-Compose 实现 |
| 基准证据 | Harness² 官方页 + arXiv:2610.14224：两层递归、Improvement library、H₀ 重置 |
| 差异描述 | 跨任务经验迁移缺失 |
| 影响面 | 高阶能力；**依赖 G1/G2 稳定后才可做** |
| 原始素材 | 论文 benchmark：直接继承成品 harness 导致回退（Code 73.4→63.2） |
| 补全方案 | 新增 `improvement/` 目录 + `EXPERIENCE.md` schema（cue/lesson/evidence 三字段）；每轮 reflect 写入，下轮从 H₀ 重启仅 Propose 阶段选择性复用 |
| **红线** | **绝不自动继承成品 harness** —— 论文实证会回退 |
| **真机 exe 验收** | 模拟两轮：第二轮初始状态必须等于 H₀，library 条目 +1；实跑行为对比而非单测 |

### G7 🟡 AOT 发布验证未完成（环境受限）

| 维度 | 内容 |
|------|------|
| 本仓证据 | DSG048 `:783` 标注「待 AOT 发布验证」；本机无 NativeAOT 工具链 |
| 补全方案 | 在 CI 做 AOT 发布验证；本机仅保证不引入运行时反射式动态加载 |
| **真机 exe 验收** | CI AOT 产物启动 `jcc --dump-nodes` 正常（本机受限，须注明） |

---

## 4. 补全顺序与依赖

```
阶段1 地基（一起做，都动 descriptor/节点结构）
  G2 节点级继承  ─┐
  G4 Metadata 注入 ─┴→ 真机 --dump-nodes 验收
        │
阶段2 可替换（都动 NodeRegistry）
  G1 可逆注册 ─┐
  G5 DI 扫描   ─┴→ 真机打点 + --dump-nodes 交叉核对
        │
阶段3 决策：G3 NodeBase（用户拍板 A/B）
        │
阶段4 高阶：G6 经验继承（守 H₀ 重置红线）
        │
阶段5 验证：G7 AOT（CI）
```

**为什么这个顺序**：继承是组合的前提，可替换是继承生效的前提。在节点还不能"继承父描述、运行期装卸"时先加新节点/经验库，等于在半截地基上盖楼。

---

## 5. 验收总表（真机 exe，非单测）

| 缺口 | 真机命令/动作 | 期望输出 | 禁止 |
|------|--------------|----------|------|
| G1 | `JCC_DEBUGLOG=1 jcc ...` 注册→替换→卸载 | `[REGISTER]`/`[DISPOSE]` 计数正确、顺序逆序 | 单测断言注册次数 |
| G2 | `jcc --dump-nodes` + `jcc tool call file read --path <真实文件>` | 合并后属性符合继承预期；继承的参数默认值生效 | 只比对 descriptor 对象 |
| G3 | `jcc tool call <组> <工具>` 覆盖 changed 组全部工具 | 635 工具契约不破裂 | 编译通过即算数 |
| G4 | `jcc --dump-nodes` | 真实 metadata，非 `"1.0"`/空数组 | 单测读 metadata 属性 |
| G5 | `[DI-SCAN]` 打点 + `--dump-nodes` 交叉核对 | 数量一致，新增服务自动出现 | 反射单元测试 |
| G6 | 两轮模拟，第二轮查初始状态 | 初始 == H₀，library +1 | 单测断言 library |
| G7 | CI AOT 产物 `jcc --dump-nodes` | 正常输出（本机注明受限） | 本机 Debug 通过代替 |

> 统一的打点纪律：标签语义化（`[REGISTER]`/`[DISPOSE]`/`[DI-SCAN]`/`[NODE-INHERIT]`）；`JCC_DEBUGLOG=1` grep；无命中说明入口猜错，换层继续打点，禁止在没执行的代码上加 await/日志。

---

## 6. 风险与缓解

| 风险 | 缓解 |
|------|------|
| 改契约层波及 635 工具 | 每步只编改动域，改契约后重编 13 个 exe + `Pack-ToolNodes.ps1`；门禁 `Verify-ToolParamNames.py` / `Verify-ToolNodes.ps1` |
| G1 可变注册表引入并发问题 | 用 Actor 邮箱模型替代共享锁（项目约定），不做 `lock` 搅拌 |
| G2 继承链循环/过深 | 复用 `NodeProfileResolver` 的循环检测 + 深度≤3 |
| G6 踩成品继承回退 | H₀ 重置红线写进实现与验收断言 |
| 文档"✅已完成"标签误导 | 本清单以代码实证为准，完成后回填 DSG048 矛盾行 |

---

## 7. 用户裁决记录

1. **G3 选项** → **数据驱动（B）**：不引入 NodeBase，`INode` 保持最小接口，属性继承交给 G2 数据层。理由：G2 落地后属性继承已由数据层解决，再加基类就是重复；且 Harness² 已证明不靠基类也能适配。
2. **补全节奏** → **按阶段逐步验收**：每阶段真机 exe 跑通、用户看到结果后再继续。

---

## 8. 剩余任务与进度

| 阶段 | 任务 | 状态 |
|------|------|------|
| 1a | **G2 节点级继承** | ✅ 已真机验收（见 §10） |
| 1b | **G4 Metadata 配置注入** | ✅ 已真机验收（见 §11，范围限定：配置注入 + 真机可观测 + 唯一默认值；路由消费端另立任务） |
| 2 | G1 可逆注册 + G5 DI 扫描 | ⏳ 待阶段1 全部验收通过 |
| 3 | G3 数据驱动收敛（已定方案） | ⏳ 待阶段2 |
| 4 | G6 经验继承（H₀ 重置红线） | ⏳ 待阶段3 |
| 5 | G7 AOT（CI） | ⏳ 待阶段4 |

> 阶段1 拆分理由：G2 在 abstractions 层、G4 在 composition 层，分属两个模块，绑在一起验收会让"继承生效"与"metadata 生效"的因果链混在一起。先独立验收 G2，再动 G4。

> 每阶段纪律：红测试 → 实现 → 编译 → 绿测试 → **真机 exe 验收（不得用单测代替）** → 文档 → 提交 → 差评大师 → 修复。

---

## 9. 实现级调研修正（2026-11-19，阶段1 动手前）

> 派两路只读调研核实 G2/G4 实际改动面，发现四处文档撰写时未知的事实。以代码实证为准，修正实现方案。**这一节的存在本身就是四问深度法的价值证明**：只看 `DSG048` 的"✅已完成"标签会得出错误验收方案。

### 9.1 `--dump-nodes` 不读 descriptor / metadata（验收方案失效）

- 实证：`Program.cs:231-253` 的 `DumpNodesInfo()` 数据源是 `NodeProfileLoader.LoadFromResources()` + `NodeProfileResolver.Resolve()`，输出 `Profile/消费点/nodeId(priority)` 纯文本，**完全绕开** `CliToolDescriptor` 与 `INode.Metadata`。
- 修正：阶段1 必须**扩展 `--dump-nodes`**（或新增 `--dump-tool-nodes`），输出继承合并后的 descriptor 属性/参数与注入后的 metadata，否则 G2/G4 无法真机验收。

### 9.2 依赖墙：resolver 不能跨层复用（架构决策点）

- 实证：`NodeProfileResolver` 在 `modules/composition/`（上层）；待改造的 `CliToolDescriptor`/`NodeManifestLoader` 在 `abstractions.primitives.tools`（底层）。后者由 `cli_node.props` 约束为**零 ProjectReference**，13 个 exe 靠此保持独立。
- 三个选项：
  - **① 下沉合并算法到 abstractions 层**（推荐）：把"排除→新增→覆写 + 深度≤3 + 循环检测"提取为底层无依赖的纯函数，composition 的 `NodeProfileResolver` 与 cli_nodes 的新 resolver 都委托它。零重复、零依赖墙击穿、符合"唯一数据源 + 委托消费"。
  - ② 在 cli_nodes 内新建同构 resolver：逻辑重复，违反"提取重复为公共组件"。
  - ③ 让 abstractions 反向引用 composition：击穿依赖墙，13 exe 全部变重，**禁止**。

### 9.3 regenerate 脚本会覆盖手写 inherits

- 实证：`migrate_mcp_tools_to_cli_nodes.py:492-517,749` 每次全量重写 node.yaml，`is_generated_or_missing` 只保护 Program.cs/csproj，不保护 node.yaml。
- 修正（推荐）：继承声明放**独立叠加文件** `node.inherit.yaml`（生成器不触碰），`NodeManifestLoader` 加载 node.yaml 后叠加。生成脚本零改动，最稳。

### 9.4 metadata 是死数据 + `Node<TService>` 生产路径未调用（范围决策点）

- 实证：`Version`/`Dependencies`/`Capabilities`/`RequiredCapabilities` 全仓无读取点；真正路由依据是 `ResolvedNodeEntry.Priority`/`EnabledWhenFeatureFlag`（`NodeRegistry.cs:55-63`），绕开 `NodeMetadata`。`NodeEntry.Create<TService>` 无调用点，`Node<TService>` 实际未被生产路径使用。
- 修正：阶段1 G4 范围为"**配置注入 + `--dump-nodes` 可观测**"，让 metadata 从硬编码变可配置、可见；"打通 capability-based routing 消费端"另立后续任务（避免阶段1 无限膨胀）。真实验收走 `NodeRegistry.Build` 实际路径，不依赖未使用的 `Node<TService>`。

### 9.5 配置落点（遵循 G3 数据驱动决策）

- metadata 注入源复用现有 profile config 体系（`base.yaml` 节点条目下加 `metadata:` 段），天然继承父 Profile、零新增文件、零 csproj 改动。与 G3"数据驱动、不引入 NodeBase"一致。

---

## 10. 阶段1a · G2 节点级继承实施记录（2026-11-19）

### 10.1 关键实现决策（与 §9 调研修正的差异）

- **验收面改用现成命令，不扩展 `--dump-nodes`**：§9.1 建议扩展该命令，但实施时发现 `jcc tool tree` / `jcc tool help` 本就读 `CliToolDescriptor` 且**渲染了 category/summary/exe/tags/examples/parameters** —— 这正是继承的全部字段，无需改命令。少一处改动、少一处回归风险。
- **`inherits` 直接写 node.yaml，不采用 §9.3 的叠加文件**：本阶段不改生成脚本；现网 13 个分组尚无 `inherits`，regenerate 覆盖只在"给真实分组加继承"时才成为问题。**遗留风险**：`migrate_mcp_tools_to_cli_nodes.py` 全量重写 node.yaml（`:492-517,749`）会冲掉手写 `inherits`，真实采用前须按 §9.3 落实叠加文件或让生成器保留该字段。
- **parser 契约放宽**：声明 `inherits` 的子节点允许省略 `name`/`summary`（由父继承），`id` 仍必填；未声明 `inherits` 的节点维持原必填校验（`NodeYamlParser.cs:88-95`）。这是"省略即继承"能否成立的硬前提——否则子节点在 parse 阶段就被拒，resolver 没有机会介入。
- **合并语义**：`DeclaredKeys`（internal 解析元信息）区分"省略 vs 显式"；子节点显式字段胜出，标量/数组省略则从父继承。`parameters`/`tools` 按 name/id 合并时**逐字段**继承（父打底 → 子同名字段覆写 → 子省略字段继承父 → 子新增项追加）—— 不能整体替换，否则子只写 `default` 会把父的 `type=integer`/`description` 冲成 parser 默认值。深度≤3、循环检测、缺父均给"原因+触发节点+拦截层+正确写法"四段式报错。
- **parser 的 tools 判定改用 YAML 键而非 `exe is null`**：继承父 exe 的子节点也要声明自己的 tools，旧判定会静默丢掉全部子工具（由新增测试 `Inheritance_ToolEntryInheritsAliases` 抓出）。
- **缺父节点补占位描述**：继承失败仍保留节点（可发现性优先），但 parser 放宽后 name/summary 为空串，须补 `[继承失败] 未找到父节点 'X'`，否则 tree 显示空摘要且 find 搜不到。

### 10.2 改动文件

| 文件 | 变更 |
|------|------|
| `modules/abstractions/abstractions.primitives.tools/src/cli_nodes/CliToolDescriptor.cs` | 新增 `Inherits` + internal `DeclaredKeys` |
| `.../src/cli_nodes/CliToolParameter.cs` / `CliToolEntry.cs` | 新增 internal `DeclaredKeys`（字段级继承的判定依据） |
| `.../src/cli_nodes/private/NodeYamlParser.cs` | 解析 `inherits`、三处填充 `DeclaredKeys`；带 `inherits` 时放宽 name/summary 必填；tools 判定改看 YAML 键 |
| `.../src/cli_nodes/CliNodeInheritanceResolver.cs` | **新增**：继承合并纯函数（Resolve/ResolveAll，参数/工具逐字段继承，缺父占位） |
| `.../src/cli_nodes/NodeManifestLoader.cs` | 目录扫描末尾接 `ResolveAll`（无 inherits 时 no-op）；单节点 API 注明不解析 inherits |
| `...tests/CliToolNodeTests.cs` | 新增 8 个继承红测试（省略继承/参数合并/多层链/循环/缺父/字段级 default/tools aliases/缺父占位） |
| `...tests/fixtures/inheritance/{valid,broken}/` | **新增**可复现真机夹具（子参数只写 default；缺父节点） |
| `scripts/migrate_mcp_tools_to_cli_nodes.py` | 继承护栏：检测到 node.yaml 含 `inherits` 拒绝覆写（守 §9.3 遗留风险） |

### 10.3 红→绿（回归网，非验收）

- 红：`CliNodeInheritanceResolver` / `Inherits` 不存在，编译报 6 错（预期）。
- 绿：修正 `IReadOnlyList.Length`→`Count`、parser 放宽契约后，库与测试项目零警告零错误；`dotnet test` = **86/86 通过**（含 8 个继承用例）。
- 过程中两处测试失败都暴露了真实设计问题，按"修设计，不迁就测试"处理：① `Inheritance_MultiLevelChain` 首轮失败 → parser 必填契约与继承语义冲突，放宽（见 10.1）；② `Inheritance_ToolEntryInheritsAliases` 失败 → parser 用 `exe is null` 判定 tools 导致继承 exe 的子节点丢失全部子工具，改看 YAML 键（见 10.1）。
- **差评大师发现 P0**（见 §10.5）：原参数合并是整体替换，子只写 `default` 时父的 type/description 被默认值冲掉；原 5 个测试与首轮真机夹具都把字段写全，恰好绕开该场景。已补字段级继承实现 + 2 个针对性红测试 + "只写 default"夹具，重跑 86/86 绿。

### 10.4 真机 exe 验收（唯一验收标准，非单测）

编译 `JoinCode.csproj`（8.8s，零错误）后，用 `artifacts/bin/JoinCode/Debug/net10.0/jcc.exe` 实跑四组：

| # | 命令 | 真机输出要点 | 判定 |
|---|------|--------------|------|
| 1 | `jcc tool tree --dir <fixtures>/valid` | `search/` 下同时出现 `base_search/` 与 `child_search/`，两者摘要都是父的"搜索类工具基类" | ✅ 子节点的 category/summary 来自父 |
| 2 | `jcc tool help child_search --dir <fixtures>/valid` | 分类 `search`、exe `jcc-base-search.exe`、`--pattern`（父定义）；`--head_limit` 显示 **`integer` + 父类描述"最多输出条数（定义在父类）"+ 默认值 50** | ✅ 标量/数组继承 + **参数字段级继承**（子 YAML 只写 `default`，type/description 从父浮出） |
| 3 | `jcc tool tree --dir <fixtures>/broken`（orphan 缺父） | stderr 四段式引导；节点列为 `orphan/ — [继承失败] 未找到父节点 'parent_that_does_not_exist'`（可被 find 搜到），进程未崩 | ✅ 容错不静默吞错、不拖垮整棵树、不留空描述 |
| 4 | `jcc tool tree`（真实 13 组） | `13 组 / 635 个工具，独立 exe 388（61%）...247 由 jcc 内执行`，与基线一致 | ✅ 无 inherits 的现网节点零回归 |
| 5 | `python migrate_mcp_tools_to_cli_nodes.py --scan modules --out <tmp> --category file` | 检测到手写 `inherits` 时拒绝覆写并打印处理路径；node.yaml 内容完好 | ✅ §9.3 遗留风险已加护栏（非 exe 验收，脚本自身实跑） |

`<fixtures>` = `modules/abstractions/abstractions.primitives.tools.tests/fixtures/inheritance`（**纳入源码版本管理，可重跑**）。`child_search/node.yaml` 只写 `id/name/inherits` 与 `parameters.head_limit.default: "50"` 五项，其余全部从父浮出——这是字段级继承生效的直接证据；旧实现在此夹具下会输出 `string` 类型 + 空描述。

> 打点纪律说明：本次改动走 `tool tree`/`tool help` 的**用户可见输出**作为执行链命中证据（子 YAML 未写的字段凭空出现，因果链唯一），无需在零依赖抽象层注入诊断标签；§1 的 `JCC_DEBUGLOG` 打点纪律保留给 G1/G5 这类"无可见输出"的运行期机制。

### 10.5 差评大师评审与修复（阶段1a 收尾）

派独立评审代理逐文件挑刺，分级结果与实际处置：

| 级别 | 问题 | 处置 |
|------|------|------|
| **P0** | 参数"按 name 合并"实为**整体替换**：子只写 `default` 时父的 `type`/`description` 被 parser 默认值冲掉；原 5 个测试与真机夹具都写全字段，恰好漏检核心场景 | ✅ 已修：`CliToolParameter.DeclaredKeys` + `MergeParameter` 逐字段继承；补 2 个针对性红测试 + "只写 default"夹具；真机 B 验证 `integer`+父描述浮出 |
| P1 | 缺父节点以空 name/summary 进入生产，tree 显示空摘要、find 搜不到 | ✅ 已修：`ResolveAll` catch 补占位描述；真机 C 验证 |
| P1 | tools 整体替换丢父条目 aliases（评审提示，实查更严重：parser 用 `exe is null` 判定会丢**整个 tools 列表**） | ✅ 已修：`CliToolEntry.DeclaredKeys` + `MergeTool` 逐字段；parser 改看 YAML tools 键；`Inheritance_ToolEntryInheritsAliases` 抓出并锁死 |
| P1 | `LoadNodeFromFile`/`LoadNodeFromText` 不走 resolver，API 语义割裂 | ✅ 已修：XML 注释明确"返回原始声明，不解析 inherits，完整结果走 Resolve/目录扫描" |
| P1 | 真机夹具在 artifacts（重建即丢）且字段写全 | ✅ 已修：夹具纳入源码 `tests/fixtures/inheritance/`，子参数只写 default |
| P2 | chain 累加不回溯是否误判循环 | 核实后非 bug：单链无分叉，深度检查位置恰好实现"最多 3 节点含根" |
| P2 | `DeclaredKeys` 原样传递的语义 | 当前无影响（Merge 只读 child 的 keys，合并结果不二次入链）；未来若允许二次 Resolve 需改为并集 |
| P2 | 生成脚本覆写 inherits | ✅ 已加护栏并实跑验证（见验收 #5） |

**总判定**：评审指出的 P0 属实且已修复；阶段1a 从"测试绿但漏检核心场景"纠正为"真机验证到字段级继承"。此轮证明纪律有效——**单元测试全绿不等于验收通过，必须让真机命令命中真实场景**。

---

## 11. 阶段1b · G4 Metadata 配置注入实施记录（2026-11-19）

### 11.1 方案与关键取舍

- **配置落点**：`meta.*` 作为 profile 节点 `config:` 内的保留命名空间（如 `resources/profiles/base.yaml` 的 `stream-rate-limit-mw`）。**刻意复用** `NodeProfileResolver` 已验证的 excludes/includes/overrides 合并机制 —— metadata 自动获得 Profile 的"省略即继承"，零新增继承逻辑，符合 G3 数据驱动决策。
- **职责切分**：`meta.*` 只放**自描述字段**（version/dependencies/capabilities/required-capabilities）；`priority` / `enabled-when` 是路由字段，以 `ResolvedNodeEntry` 为权威值，由 `NodeRegistry` 注入时补齐 —— 避免同一节点出现两个互相矛盾的 priority。
- **唯一默认值**：`NodeMetadata.Unspecified` 替代散落在 `Node.cs` / `CommandCategoryNode` / `PromptSectionCategoryNode` / 测试里的 `new("1.0", [], [], [], 0, null)`。

### 11.2 差评大师发现并修复（本阶段最重要的一轮）

首版做成"`FromConfig` 只被 `Program.cs` 的诊断输出调用、`NodeEntry.Create(metadata)` 零生产调用"——**真机绿灯只证明"config 能解析显示"，没证明 metadata 进了节点对象**。评审判定为"用真机绿灯掩盖注入从未发生"，与本次任务要消除的"文档标签≠行为"是同一类错误。已按如下修复：

| 级别 | 问题 | 处置 |
|------|------|------|
| P1 | "配置注入"是假象，注入通道零生产调用 | ✅ 打通真实链：`NodeRegistry.GetNodesForConsumerAsync`（被 4 个 Consumer 调用的生产路径）从 `ResolvedNodeEntry.Config` 构造 metadata 并注入 `INode`；`NodeEntry` 工厂签名改为 `(IServiceProvider, NodeMetadata?) → INode` |
| P1 | 同一行两个 priority（`priority=100` vs `meta.pri=175`），且 meta.pri 无人消费 | ✅ 删除 `meta.priority` 解析与配置，诊断输出不再显示 `pri=` |
| P1 | `meta.enabled-when` 是僵尸配置（不生效也不显示） | ✅ 从 `meta.*` 移除；flag 以节点级 `enabled-when` 为准 |
| P1 | 非法 `meta.priority`（"abc"）静默降级为 0，与节点 priority 非法直接报错双标 | ✅ 随字段删除自然消除（不再有该解析路径） |
| P1 | 注入测试测的是生产永不执行的死路径（"单测当挡箭牌"） | ✅ 改为端到端测试 `NodeRegistry_InjectsProfileMetadataIntoRuntimeNode`：经 `NodeRegistry.Build` + `GetNodesForConsumerAsync` 断言运行期 `INode.Metadata` 拿到配置值 |
| P2 | 未知 `meta.*` 键（如 `meta.ttl`）会触发"假 metadata" | 记录为约定：保留前缀只认四个自描述键；未知键返回 version=unspecified 的 metadata，不加 loader 校验（避免扩大改动面） |

### 11.3 改动文件

| 文件 | 变更 |
|------|------|
| `modules/abstractions/composition/node/interfaces/NodeMetadata.cs` | 新增 `ConfigPrefix` / `FromConfig` / `Unspecified` |
| `.../interfaces/CommandCategoryNode.cs` / `PromptSectionCategoryNode.cs` | 硬编码 metadata 改 `Unspecified with { Priority }` |
| `.../composition/node_registry/core/Node.cs` | 构造接收 metadata，消硬编码 |
| `.../core/NodeEntry.cs` | 工厂签名带 metadata；`CreateNode(sp, metadata)` |
| `.../core/NodeRegistry.cs` | **生产注入链**：`GetNodesForConsumerAsync` 注入配置 metadata |
| `modules/apps/cli/core/commands/core/Program.cs` | `--dump-nodes` 输出真实 metadata（不输出 pri） |
| `resources/profiles/base.yaml` | `stream-rate-limit-mw` 加 `meta.*` 配置 |
| `...composition.tests/node_registry/{NodeProfileResolverTest,ConsumerTest}.cs` | 4 个测试（解析/无声明/Profile 继承覆写/端到端注入） |

### 11.4 验收

- 红→绿：首轮 15 错（含 JCC11003 门禁抓到 null-forgiving `!`，已改 `Assert.NotNull`）；修复后 abstraction/composition/tests/CLI 四个 csproj 零警告零错误，Composition.Tests **1083/1083 通过**。
- 真机 exe（唯一标准）：
  - `jcc --dump-nodes` 在 base/ci/headless/sdk **四个 profile** 均输出 `stream-rate-limit-mw (priority=100) [meta v=2.1.0 deps=config-key-registry caps=rate-limit,audit req=config-key-registry]` —— 配置真实值，非硬编码 `1.0`/空数组；无 meta 声明的节点不占位；无双 priority。
  - `jcc --dump-profile base` 对照显示 config 中的 `meta.*` 保留键。
  - `jcc tool tree` 真实 13 组 635 工具零回归。

### 11.5 真实边界（不得宣称超出）

- **metadata 仍无路由消费端**：`NodeRegistry` 排序/开关判定用 `ResolvedNodeEntry.Priority` / `EnabledWhenFeatureFlag`，不读 `NodeMetadata`。Phase1b 只做到"配置注入 + 真机可见 + 默认值唯一"，**capability-based routing 另立任务**。
- **`Node<TService>` 生产零实例化**：`NodeRegistry.Build(sp)` 在生产不传 customFactories（G5 缺口），故 `INode.Metadata` 的真机值目前只能在端到端单测中观测；G5 落地 DI 扫描后，本阶段打通的注入链会自动生效。不得用"真机 --dump-nodes 有值"宣称运行期节点已带 metadata。
