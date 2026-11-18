# TASK097 节点继承缺口清单与补全计划（四问深度版）

> 状态: proposed（待用户逐项裁决）
> 创建: 2026-11-19
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
| **真机 exe 验收** | `jcc --dump-nodes` 对比父子节点合并后的属性/参数差异；真机 `jcc tool call file read --path <真实文件>` 验证继承来的参数默认值生效；跨组继承用例实跑 |

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
| **真机 exe 验收** | `jcc --dump-nodes` 输出必须显示配置注入的真实值，而非 `"1.0"` / 空数组 |

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

## 7. 待用户逐项裁决（一问一答）

1. **G3 选项**：NodeBase 走 DSH 式（A）还是 Harness² 式数据驱动（B，推荐）？
2. **补全范围**：按阶段1→5 全补（用户已表达"补充全部"倾向），还是先补阶段1 看真机效果？
3. **G6 范围**：是否需要本轮实现经验继承（PPC + library），还是先停在 G1–G5 的结构层？
