# 0161. 架构契约可执行化：模块边界规则 + 仓库门禁

> 📍 **导航**: [docs/](../../README.md) › [proposed/adr/](README.md)
> 🔗 **上游**: [0137](../../implemented/adr/data_core_selection/0137-colocation-module-isolation.md)（就近域隔离）、[0134](../../implemented/adr/engineering_ci_test/0134-layered-ci-nuget-decouple-incremental.md)（分层 CI/NuGet 解耦）
> 📋 **执行计划**: [DSG052](../design/DSG052-architecture-contract-and-gate-plan.md)

- 状态：proposed
- 日期：2026-11-21
- 决策者：项目架构组
- 拟取代：无（新增决策，不推翻 0137 的物理分域）

## 背景

ADR 0137 已完成 16 功能域的物理迁移：每个域自带 `module.slnx`、`README.md`、`CHANGELOG.md`、`docs/`，域间依赖支持 `ProjectReference` / `PackageReference` 双模式。**目录意义上的模块隔离已经成立**。

但"隔离"目前只有物理约束，没有可执行约束。对当前仓库实测（脚本见 `DSG052` 附录，产物在 `.xxx/analysis/`）：

| 指标 | 实测值 | 含义 |
|------|--------|------|
| 功能域 / `modules/` 下 csproj | 16 域 / 202 个 | 分域规模 |
| 全仓 csproj | 259 个 | 含 test、native_tui、native_gui、non_deliverables_tools |
| `ProjectReference` 总数 | 1169（code 1013 + analyzer 156） | 构建立边 |
| 跨域 code 边 | 693 / 1013（68.4%） | 大多数依赖都是跨域的 |
| 生产项目 cross-domain 边 | 474 / 703（67.4%） | 排除 `*.tests` 与 `test/` 后仍占 2/3 |
| 生产域级环 | 15 条环路径 | `abstractions ↔ async_primitives/plugins/storage`、`storage ↔ composition`、`apps ↔ native_tui` 等 |
| `module.slnx` 与域内项目不一致 | 4 个域 | abstractions 自有 37/43（slnx 另含 4 个域外项目）、generators 16/17、perception 3/7、tools 8/23 |
| 目录名不符合"全小写+下划线" | 176 个 | `brain.context_core`、`mcp.protocol`、`agents.tests/Agents` 等 |
| `private/` 分层目录 | 仅 2 个域使用（abstractions 1、tools 12） | 目录层次规约基本停留在文字 |
| `InternalsVisibleTo` | 96 个项目声明（modules 审计口径；门禁含 native/工具项目为 99），单项目最多 17 个 | 程序集边界被大面积打开 |
| 声明 `public` 类型 | 182 个项目 | API 边界无准入控制 |
| 被跨域引用的不同项目 | 51 个 | 182 个声明公开类型的项目中只有 51 个被跨域引用，其余 131 个可逐步收为 internal |
| 自定义分析器规则 | 98 条 | 62 Warning / 20 Error / 15 Info / 1 未标注 |
| 其中与模块/依赖/命名空间/API 边界相关的规则 | 0 条 | 现有规则全部是文件级/语法级 |

关键结论：**物理分域完成，逻辑分域未完成**。域边界靠 `AGENTS.md` 文字和人工 review 维持，编译器和 CI 都不理解"模块"这个概念。

### 现有分析器为什么抓不到

1. **项目上下文信息不足**：`ProjectContext.From` 只读取 `build_property.ProjectType` 与 `JccAllowModuleInitializer` 两个属性（`modules/generators/aot_safety_generator/infrastructure/IAnalyzerRule.cs:48-64`）。分析器不知道当前项目属于哪个域、允许依赖谁、哪些类型可以公开。
2. **单 compilation 视野**：Roslyn 分析器只看当前编译单元，看不到 `ProjectReference` 图，因此"域间成环""引用了未授权域"这类跨项目规则无法在分析器内完成。
3. **结构规则只有 2 条**：`JCC10001`（单文件夹文件 >40）和 `JCC10002`（文件/文件夹混放），见 `modules/generators/aot_safety_generator/rules/code_organization/ProjectStructureRule.cs:8-27`。目录命名、层次、私有化都没有规则。

### 现有资产（本决策要复用的部分）

- 分析器已具备特性驱动规则基建：`[AnalyzerRule]` + `RuleDescriptorFactory` + `GuardChain` + `RuleRegistry`，98 条规则、5 个 CodeFixProvider、270 个 Fact/Theory 测试标记。
- `Directory.Build.props:166-171` 已把分析器接入所有 net10.0 项目，并带 `JccAnalyzers` 总开关。
- `modules/Directory.Build.props` 已是域级共享配置入口，可承载契约属性并向分析器透传。
- `non_deliverables_tools/scripts/Build-AllDomains.ps1:158-176` 已维护 16 域 slnx 清单，门禁可复用同一份域清单来源。

## 决策

### 1. 契约的单一事实源：域级 `Directory.Build.props`

每个域新增 `modules/<domain>/Directory.Build.props`（`native_tui`、`native_gui` 在各自根目录同样新增一份），声明本域契约属性；不引入 YAML/JSON manifest，避免与 MSBuild 双源漂移。

| 属性 | 必填 | 语义 | 默认 |
|------|------|------|------|
| `JccDomain` | 是 | 域标识，必须与 `modules/<dir>` 一致 | 无（缺失即 JCC12001） |
| `JccLayer` | 否 | 层级号，仅用于文档/排序展示，**不参与判定** | 空 |
| `JccAllowedDomains` | 是 | 允许依赖的域白名单，`;` 分隔；测试域为 `*` | 空（只允许域内） |
| `JccApiVisibility` | 否 | `public` = 允许被其他域引用；`internal` = 仅域内（域级默认，项目可覆盖） | `internal` |
| `JccNamespaceRoots` | 否 | 允许的命名空间前缀，`;` 分隔 | 空（不检查） |
| `JccAllowInternalsVisibleTo` | 否 | 允许的 IVT 程序集名，`;` 分隔；**项目级声明**，域级只做默认 | 空（新增 IVT 必须逐项目登记） |
| `JccSlnxExcluded` | 否 | 明确不纳入 `module.slnx` 的域内项目（`;` 分隔，必须附注释说明） | 空（要求全部登记） |

项目级可在 csproj 内覆盖 `JccApiVisibility`（如域内实现项目强制 `internal`）。

### 2. 双层执行面

| 执行面 | 载体 | 负责的规则 | 理由 |
|--------|------|------------|------|
| 编译期（IDE + `dotnet build`） | 扩展 `modules/generators/aot_safety_generator`，新增 `rules/architecture/`，编号 `JCC12xxx` | 项目内可判定的规则：契约合法性、公开类型边界、IVT 白名单、命名空间根 | 开发者保存文件即得到反馈；复用现有 `[AnalyzerRule]` 基建，不新建分析器项目 |
| 仓库期（CI / pre-push） | 新增 `non_deliverables_tools/architecture/ArchitectureGate.ps1` | 需要全仓图视野的规则：跨域依赖白名单、域级环、`module.slnx` 完整性、生产引用测试、Hub 预算、文档齐备、目录命名 | Roslyn 单 compilation 看不到 `ProjectReference` 图；XML 解析秒级完成，适合每次 PR |

**明确不采用的路径**：不把项目图硬塞进分析器（需要 MSBuild 生成中间文件 + AdditionalFiles，链路脆弱且调试困难）；不用 `#pragma` 或 `[SuppressMessage]` 做豁免。

### 3. 判定语义：能力白名单 + 强制 DAG

- 每个域显式声明"允许依赖谁"（allowlist，**粒度=域对**），门禁逐条校验实际 code 边是否在白名单内。项目级只对两类目标单独约束：跨域目标必须是 `public` 项目（JCC12003）、Hub 入度不得超预算（JCC12012）。
- 同时要求**生产域图无环**（DAG）。环是比"跨域"更严重的问题：它让"整域拆走/独立打包"在 NuGet 层也不成立。
- 不使用刚性层级号判定（`JccLayer` 只做展示）。原因见替代方案 A。
- 图模型必须区分三类边，否则指标失真：
  - `code`：`ProjectReference` 且非 Analyzer；
  - `analyzer`：`OutputItemType="Analyzer"`（构建期工具，不构成运行时耦合）；
  - `test`：`*.tests` 或 `test/` 下的项目（测试可以跨域，生产不可引用测试；测试项目不要求域契约）。
- 契约只覆盖 `modules/`、`native_tui/`、`native_gui/`；`non_deliverables_tools/`、`build/`、`scripts/` 不在范围。

### 4. 抑制纪律

| 场景 | 允许的做法 | 禁止的做法 |
|------|------------|------------|
| 存量违规 | 门禁规则写入 `non_deliverables_tools/architecture/baseline.json`，**只减不增**；分析器规则用 `.editorconfig` severity 先降为 `suggestion`，清账后翻 `error` | 全局调低 severity 后不再回看 |
| 合理例外（如 Infrastructure 过渡期） | 写入契约的 allow 条目，必须带 `reason` + `adr` + `expires` | 在源码里 `#pragma warning disable` / `[SuppressMessage]` |
| 新代码违规 | 直接修 | 加 baseline |

`expires` 到期后门禁报 Error，强制重新决策。

### 5. 分阶段推进

按 `DSG052` 的 P0→P5 执行：基线冻结 → 契约落地 → 编译期规则 → 仓库门禁 → 债务清偿 → 严格模式。每阶段独立编译、测试、提交，可回滚。

## 考虑的替代方案

### 方案 A：刚性分层编号（layer 0..N，只允许依赖更低层）

**放弃理由**：当前真实依赖跨层严重（`Infrastructure` 同时依赖 abstractions/async/persistence/plugins/storage），强行套层号只有两种结果：给项目乱标层号过门禁，或一次性大重构。层号适合做导航，不适合做判定。allowlist + DAG 能表达"允许的边"，且不要求先重构。

### 方案 B：只做 CI 脚本

**放弃理由**：无 IDE/编译期反馈，违规在 PR 阶段才暴露；且脚本规则与源码位置脱节，修复成本高。实测现有 176 个目录命名违规、96 个项目 IVT 扩散，正是"只有文字规约"的结果。

### 方案 C：只做 Roslyn 分析器

**放弃理由**：分析器拿不到跨项目图。勉强实现需要 MSBuild 在编译前生成项目图并通过 `AdditionalFiles` 注入，链路长、增量编译易失效、CI 与本地行为易分叉。

### 方案 D：新增 YAML/JSON 模块清单

**放弃理由**：与 `Directory.Build.props`/csproj 形成双源，必然漂移；且构建本身仍需 MSBuild 属性，清单还要再翻译一次。MSBuild 属性本身就是声明式契约，配合门禁校验足够严格。

### 方案 E：维持 AGENTS.md 文字规约 + 人工 review

**放弃理由**：已被实测证伪。跨域边占 2/3、15 条环、4 个域 `module.slnx` 与域内项目不一致，说明文字规约无法在规模上约束行为。

## 后果

### 正面

- 模块边界从"文档约定"变成"编译错误 / CI 红灯"，新代码无法悄悄越界。
- 51 个真正对外的项目显式标记 `public`，其余 131 个声明公开类型的项目逐步收为 `internal`，程序集边界可度量。
- 域图变成 DAG 后，`UsePackedComponents=true` 的 NuGet 打包与"整域拆走"才有可执行前提。
- 契约属性可被分析器、门禁、文档生成器、CI 共同消费，符合"单一事实源 + 委托消费"。

### 负面

- 需要一次性给 18 个域补契约文件，并给 182 个项目的公开类型定基线。
- 存量债务（4 个 slnx、176 个目录名、15 条环）不会自动消失，需要分阶段清账。
- 门禁规则本身需要测试与维护，否则会成为新的"文字规约"。

### 缓解

- 全部存量进 baseline，先"只减不增"，不阻塞当前开发。
- 规则先以 `suggestion/Info` 上架，清账后再逐条翻 `error`。
- 每条门禁规则配 fixture 测试（正例/反例），并用"故意注入违规→红灯→撤销→绿灯"做验收。

## 验证

- [ ] 指标可复现：`pwsh .xxx/analysis/dependency-graph.ps1` + `structure-audit.ps1` 输出与本文数字一致
- [ ] 18/18 域契约文件存在（16 modules + native_tui + native_gui），`JccDomain` 与目录名一致
- [ ] `ArchitectureGate.ps1 -Mode enforce` 对当前仓库返回已知的 baseline 数量，不误报
- [ ] 注入一条未授权跨域引用 → 门禁红灯；撤销 → 绿灯
- [ ] 注入一个域级环 fixture → 门禁红灯并打印完整环路径
- [ ] 新分析器规则在 `modules/generators/aot_safety_tests/architecture/` 中有正例/反例各至少 1 条
- [ ] CI `ci-architecture.yml` 在 PR 上阻断违规

> 状态流转：讨论通过后转 `implemented/`；被取代时转 `archived/` 并标注 superseded。
