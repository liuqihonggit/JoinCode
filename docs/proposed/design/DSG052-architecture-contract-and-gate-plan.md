# DSG052. 架构规约与模块隔离落地规划

> 状态: proposed
> 日期: 2026-11-21
> 上游决策: [ADR 0161](../adr/0161-architecture-contract-analyzer-and-gate.md)
> 范围: 全仓 16 功能域 + `test/` + `native_tui/` + `native_gui/`
> 原则: 先把边界变成可执行契约，再清存量；不改运行时行为，不引入新构建系统

---

## 1. 现状分析

### 1.1 数字快照

数据来源：`.xxx/analysis/dependency-graph.txt`、`.xxx/analysis/structure-audit.txt`（脚本可重跑，见第 12 章）。

| 维度 | 实测 | 判断 |
|------|------|------|
| 功能域 | 16 个（`modules/*`），202 个 csproj | 物理分域完成 |
| 全仓 csproj | 259 个 | 含 test、native_tui、native_gui、non_deliverables_tools |
| `ProjectReference` 总数 | 1169（code 1013 + analyzer 156） | 构建图很大 |
| 域内 code 边 / 跨域 code 边 | 320 / 693 | 68.4% 的依赖跨域 |
| 生产 code 边（排除测试） | 703（域内 229 / 跨域 474） | 排除测试后仍有 67.4% 跨域 |
| 生产域级环 | 15 条环路径 | 域图不是 DAG |
| 规则数量 | 98 条（Error 20 / Warning 62 / Info 15 / 未标注 1） | 代码级规则强，架构级规则 0 |
| 规则测试 / CodeFix | 270 个 Fact/Theory 标记；5 个 CodeFixProvider（3 个文件） | 基建成熟可复用 |
| `module.slnx` 完整性 | 4 个域不一致 | abstractions 自有 37/43（另含 4 个域外项目）、generators 16/17、perception 3/7、tools 8/23 |
| 目录命名违规 | 176 个目录 | 未按"全小写+下划线"执行 |
| `private/` 分层 | 仅 abstractions(1) + tools(12) | 层次规约基本未使用 |
| `core/core` 嵌套 | 0 | 已避免 |
| `InternalsVisibleTo` | 96 个项目（modules 审计口径；门禁含 native/工具项目为 99），单项目最多 17 | 程序集边界大面积打开 |
| 声明 `public` 类型 | 182 个项目 | 无 API 准入 |
| 被跨域引用的不同项目 | 51 个 | 只需 51 个项目对外公开 |

### 1.2 覆盖矩阵：规约条目 vs 现有执行手段

| 规约条目（AGENTS.md / ADR） | 现有执行手段 | 执行面 | 缺口 |
|----------------------------|--------------|--------|------|
| 文件夹直接文件 < 40 | `JCC10001` | 编译期 | 已覆盖 |
| 文件夹内文件/文件夹不混放 | `JCC10002` | 编译期 | 已覆盖 |
| `.cs` 禁写 `using` | `JCC1005` | 编译期 | 已覆盖 |
| 禁 null-forgiving `!` | `JCC11003` | 编译期 | 已覆盖 |
| 禁手写 JSON 拼接 | `JCC1017` | 编译期 | 已覆盖 |
| 禁元组作公开 API | 无规则 | — | **缺口**（AGENTS 有禁令，分析器未实现） |
| 目录命名全小写+下划线 | 无规则 | — | **缺口**（实测 176 处） |
| `private/` 目录层次 | 无规则 | — | **缺口**（仅 2 域使用） |
| 模块归属 / 域契约 | 无 | — | **缺口**（分析器不知道域） |
| 域间依赖方向 / 白名单 | 无 | — | **缺口**（693 条跨域边无约束，其中生产 474 条） |
| 域图无环 | 无 | — | **缺口**（15 条环） |
| `module.slnx` 完整性 | 无 | — | **缺口**（4 域与域内项目不一致） |
| 模块文档齐备 | 无 | — | **缺口**（slnx 4 域不一致；README 内容漂移，至少 abstractions 只列 2/43） |
| 公开 API 边界 | 仅 `apps/sdk` 用 PublicAPI 分析器 | 编译期 | **缺口**（182 个项目无边界） |
| `InternalsVisibleTo` 准入 | 无 | — | **缺口**（96 个项目，modules 口径） |
| 生产不得引用测试 | 无 | — | **缺口**（1 处：`Vault.Testing.Common → Testing.Common`，属测试辅助，需分类） |

### 1.3 三个根因病灶

#### 病灶 1：`Infrastructure` 是跨域 God Project

`modules/abstractions/infrastructure/Infrastructure.csproj`：

- 被 **94 个项目**直接引用（全仓含测试口径；生产 code 入度 53），全仓 code 入度第 2，仅次于 `Abstractions.csproj` 的 108（生产 88）；
- 自身 code 出度 **20**，横跨 abstractions / async_primitives / collections / persistence / plugins / storage 六个域；
- 声明约 **190 个 public 类型**、**16 个 InternalsVisibleTo**。

它同时承担 DI/Options/Hosting/Logging（合理）与 PithosDB、Plugins.Contracts、Downloader 适配（不合理），因此 `abstractions → persistence/plugins/storage` 的反向边全部由它产生，直接构成域级环。项目名 `Infrastructure` 与所在域名 `abstractions` 的语义也不一致。

#### 病灶 2：`abstractions` 的"底层"定位与依赖方向矛盾

- `abstractions` 域对外入度 108（生产 88），本应是底层；
- 但 `abstractions/Abstractions.csproj` 引用 `async_lock/AsyncLock.csproj`，`Infrastructure` 又引用 `Storage.Downloader`、`Plugins.Contracts`、`PithosDB.Core`；
- 这些边让 `abstractions ↔ async_primitives/plugins/storage` 成环。

#### 病灶 3：模块自包含"有形无实"

16 个域都齐备 `module.slnx` / `README.md` / `CHANGELOG.md` / `docs/`，但：

- 4 个域的 `module.slnx` 与域内项目不一致：
  - abstractions：自有 37/43，漏 `Abstractions.Brain/Composition/Guard/Memory/Perception` 与 `Abstractions.Primitives.Tools.Tests`；同时额外列了 4 个域外项目（`Structura`、`AotSafety.Generator`、`CodeFixes`、`EnumMetadata.Generator`）；
  - generators：漏 `AotHandler.Generator`；
  - perception：漏 `Eyes`、`Eyes.Tests`、`Vision`、`Vision.Tests`；
  - tools：8/23，差 15（13 个 `cli_nodes/groups/*` + `cli_nodes/sample` + `onnx_embedding.tests`），其中 13 个分组项目很可能按 `Pack-ToolNodes.ps1` 流程单独构建，需要在契约里显式排除并写理由，而不是简单补进 slnx。
- `modules/abstractions/README.md` 只列了 2 个项目（实际 43 个）；
- `modules/collections/module.slnx` 只有 2 条记录，是正确样板，但没有门禁保证其它域一致。

结论：**自包含文件是"迁移时生成过一次"的产物，没有持续维护机制。**

### 1.4 为什么现有分析器抓不到（精确到代码）

1. `ProjectContext` 只有 `ProjectType` 与 `AllowModuleInitializer` 两个字段（`modules/generators/aot_safety.generator/infrastructure/IAnalyzerRule.cs:48-64`），没有任何域/API/白名单信息。
2. `Directory.Build.props` 只向分析器暴露 `ProjectType` 与 `JccAllowModuleInitializer`（`Directory.Build.props:140-143`），没有模块属性。
3. 现有结构规则只有 `JCC10001/10002`（`rules/code_organization/ProjectStructureRule.cs`），只看目录内文件数量与混放，不看命名与层次。
4. Roslyn 分析器运行在单个 compilation 内，无法读取 `ProjectReference` 图；域级环、跨域白名单必须由仓库级工具完成。

---

## 2. 目标与非目标

### 目标

1. **新代码零越界**：新增的跨域依赖、公开类型、IVT、目录命名违规在编译期或 PR 阶段被阻断。
2. **存量可视、只减不增**：182 个公开类型项目、96 个 IVT 项目、176 个目录名进入存量账本（门禁 baseline + 分析器报告），允许逐步清账，但数量不得回升。
3. **域可独立编译/打包/拆走**：生产域图达到 DAG，`module.slnx` 100% 覆盖域内项目，跨域只允许引用显式 `public` 的项目。
4. **规约单一事实源**：域契约、规则 ID、基线、CI 门禁共享同一份数据，不产生第二套"文字规约"。

### 非目标

- 不在本规划内做全量目录重命名（176 处）或全量 `public → internal` 改造；这些属于 P4/P5 的债务清理。
- 不引入新的构建系统（Bazel/CMake 等）或新的包管理器。
- 不改任何运行时行为、协议、DI 注册。
- 不追求"层级号严格单调"；允许以显式 allowlist 表达真实依赖。

---

## 3. 契约模型

### 3.1 契约属性

每个域一个 `modules/<domain>/Directory.Build.props`，内容示例（cognition 域）：

```xml
<Project>
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />

  <PropertyGroup>
    <JccDomain>cognition</JccDomain>
    <JccLayer>4</JccLayer>
    <JccAllowedDomains>abstractions;collections;async_primitives;guard;storage;plugins;mcp</JccAllowedDomains>
    <!-- 域级默认 internal；对外项目在 csproj 覆盖为 public（全仓 51 个） -->
    <JccApiVisibility>internal</JccApiVisibility>
    <JccNamespaceRoots>JoinCode.Brain;JoinCode.Hands;JoinCode.Prompts</JccNamespaceRoots>
    <!-- 不纳入 module.slnx 的域内项目，必须逐个写明理由 -->
    <JccSlnxExcluded></JccSlnxExcluded>
  </PropertyGroup>
</Project>
```

项目级覆盖示例：

```xml
<!-- modules/cognition/hands_api/Hands.Api.csproj：被其他域引用，显式公开 -->
<PropertyGroup>
  <JccApiVisibility>public</JccApiVisibility>
</PropertyGroup>

<!-- modules/cognition/hands_core/Hands.Core.csproj：域内实现，保持默认 internal -->
<!-- 需要放开内部可见性时逐项目登记，未登记即视为不允许（存量走 baseline） -->
<PropertyGroup>
  <JccAllowInternalsVisibleTo>JoinCode.Hands.Tests;JoinCode.Hands.ToolHandlers.Tests</JccAllowInternalsVisibleTo>
</PropertyGroup>
```

### 3.2 透传给分析器

在根 `Directory.Build.props` 增加：

```xml
<ItemGroup>
  <CompilerVisibleProperty Include="JccDomain" />
  <CompilerVisibleProperty Include="JccLayer" />
  <CompilerVisibleProperty Include="JccAllowedDomains" />
  <CompilerVisibleProperty Include="JccApiVisibility" />
  <CompilerVisibleProperty Include="JccNamespaceRoots" />
  <CompilerVisibleProperty Include="JccAllowInternalsVisibleTo" />
  <CompilerVisibleProperty Include="JccArchitectureRules" />
</ItemGroup>
```

`ProjectContext` 增加对应字段，保持 `IAnalyzerRule` 签名不变（所有规则仍从 `ProjectContext` 取上下文）。

### 3.3 为什么契约落在 props 而不是 YAML

| 方案 | 优点 | 缺点 | 结论 |
|------|------|------|------|
| 域级 `Directory.Build.props` | MSBuild 原生、构建与分析器共用、无翻译层 | 可读性弱于 YAML | **采用** |
| `module.yaml` + 生成 props | 人类可读、可 schema 校验 | 双源必然漂移；构建前需生成步骤 | 不采用 |
| 每个 csproj 内联属性 | 最精确 | 202 个项目重复维护，改域契约要改 202 处 | 不采用 |

### 3.4 与 `module.slnx`、README 的关系

- `module.slnx`：域的"编译清单"，必须覆盖域内全部 csproj，或把项目显式写入 `JccSlnxExcluded` 并注明理由（`JCC12010`）。slnx 允许额外包含域外构建依赖。
- `README.md` / `CHANGELOG.md` / `docs/`：域的"自包含文档"，必须存在（`JCC12011`）。
- `Directory.Build.props`：域的"契约"，声明依赖白名单/API 可见性/命名空间根。
- 三者缺一不可：slnx 保证能编译，文档保证能交接，契约保证不越界。

---

## 4. 规则目录（JCC12xxx）

> 编号约定：`JCC12xxx` 为架构规则段，避免与现有代码级规则混号。级别为"新代码生效级别"。
> 存量抑制分两条路：**门禁规则**（12002-12005、12009-12012）走 `baseline.json` 只减不增；**分析器规则**（12001、12006-12008、12013）先用 `.editorconfig` 把 severity 降为 `suggestion`，清账后逐条翻 `error`（不使用 pragma）。

| ID | 名称 | 执行面 | 级别 | 判定 | 存量处理 |
|----|------|--------|------|------|----------|
| `JCC12001` | 域契约缺失/非法 | 分析器 + 门禁 | Error | `JccDomain` 为空、与目录名不一致、`JccAllowedDomains` 含未知域 | 新增即错；现有 18 域先补齐 |
| `JCC12002` | 跨域依赖未授权 | 门禁 | Error | 生产 code 边 `from→to` 且 `to ∉ JccAllowedDomains(from)`；粒度=域对 | baseline 按生产 474 条跨域边逐域对登记；新增域对即 Error |
| `JCC12003` | 跨域引用目标非 public | 门禁 | Error | 跨域 code 边的目标项目 `JccApiVisibility != public` | 先给 51 个实际对外项目标 public，其余进 baseline |
| `JCC12004` | 生产项目引用测试项目 | 门禁 | Error | 非测试项目 → `*.tests`/`test/` 项目 | 现有 1 条（Vault.Testing.Common）标记为测试辅助例外 |
| `JCC12005` | 生产域级循环依赖 | 门禁 | Error | 生产域图 DFS 检出环，打印完整路径 | 15 条环进 baseline，P4 逐条消 |
| `JCC12006` | 公开类型越过 API 可见性 | 分析器 | Warning→Error | `JccApiVisibility=internal` 的项目声明 `public` 类型（排除生成代码） | 51 个对外项目标 public 后，其余 131 个声明公开类型的项目先进报告，再逐步 internal 化 |
| `JCC12007` | IVT 未登记/生产互开 | 分析器 | Error | 程序集 `InternalsVisibleTo` 不在 `JccAllowInternalsVisibleTo`；或生产→生产互开 | 96 个项目（modules 口径）随时间翻 `error` 清账 |
| `JCC12008` | 命名空间根不符 | 分析器 | Warning→Error | 类型命名空间不以 `JccNamespaceRoots` 任一前缀开头 | 先以 Info 上架，按域校准前缀 |
| `JCC12009` | 目录命名违规 | 门禁 | Warning | 项目内目录段不匹配 `^[a-z0-9_]+$`（`private` 白名单） | 176 处进 baseline，P4 批量改名 |
| `JCC12010` | `module.slnx` 未登记项目 | 门禁 | Error | 域内 csproj 未出现在 `module.slnx`，且不在 `JccSlnxExcluded`；slnx 允许含域外构建依赖 | 4 域缺口进 baseline；tools 的 13 个分组项目先声明排除理由 |
| `JCC12011` | 模块文档齐备 | 门禁 | Warning | `README.md`/`CHANGELOG.md`/`docs/` 缺失，或 README 项目表条目数 ≠ csproj 数 | 不存在缺失，存在内容漂移（abstractions 2/43） |
| `JCC12012` | Hub 入度预算 | 门禁 | Warning | 单个项目 code 入度超过 baseline 预算 | `Abstractions` 88、`Infrastructure` 53 先冻结（生产口径；含测试 108/94） |
| `JCC12013` | `private/` 层次违规 | 分析器 | Info | `private/` 目录下声明 `public` 类型 | 存量按基线；P3 后随域清理 |

### 规则实现要点

- **分析器规则**统一放 `modules/generators/aot_safety.generator/rules/architecture/`，每条规则一个文件，沿用 `[AnalyzerRule]` 特性与 `IAnalyzerRule` 接口；由 `ArchitectureRules.cs` 聚合注册。
- **测试项目豁免**：`ProjectType=test`（`*.tests` / `test/`）不要求 `JccDomain`，12001/12006/12007/12008 对测试项目跳过；图模型中 test 域默认 `*` 允许依赖任意域。
- **非交付工具豁免**：`non_deliverables_tools/`、`build/`、`scripts/` 不在契约范围，12001 只对 `modules/`、`native_tui/`、`native_gui/` 下的项目生效。
- **生成代码豁免**：以 `GeneratedCodeAttribute`、`obj/`、`*.g.cs` 为准；生成代码不参与 API/命名空间规则。
- **`JCC12002/12003/12004/12005/12009/12010/12011/12012` 只在门禁实现**：这些规则要么需要跨项目图，要么是纯结构检查（目录命名、slnx、文档），放门禁可保证单一实现、全仓一致。
- **修复建议（CodeFix）**：`JCC12007` 提供"加入 `JccAllowInternalsVisibleTo`"的 fix；`JCC12009` 在门禁报告中直接给出建议目录名（不自动改，避免误伤 csproj 路径）。其余规则先不做自动修复。

---

## 5. 仓库门禁设计

### 5.1 位置与目录树

```
non_deliverables_tools/architecture/
  ArchitectureGate.ps1        # 入口：参数、编排、退出码
  README.md                   # 使用说明（本地/CI/维护基线）
  baseline.json               # 存量基线（只减不增）
  private/                    # 内部实现，按职责一个文件
    GraphModel.ps1            # csproj/slnx/props 解析 → 项目图
    RuleContract.ps1          # JCC12001
    RuleGraph.ps1             # JCC12002/12004/12005/12012
    RuleModule.ps1            # JCC12003/12010/12011
    RuleLayout.ps1            # JCC12009
    RuleBaseline.ps1          # 基线比较/豁免到期
    Report.ps1                # markdown/json 输出
```

### 5.2 图模型（关键：三类边分开）

| 边类型 | 判定 | 用途 |
|--------|------|------|
| `code` | `ProjectReference` 且无 `OutputItemType="Analyzer"` | 架构依赖判定 |
| `analyzer` | `OutputItemType="Analyzer"` | 仅统计构建期工具耦合，不参与架构判定 |
| `test` | 项目名 `*.tests` 或路径含 `\test\` | 允许跨域；生产→测试单独判定 |

必须暴露以下过滤参数，避免指标失真：

- `-IncludeTests`：默认 false（生产图）；
- `-IncludeAnalyzer`：默认 false；
- `-Domains a,b,c`：只检查指定域（本地增量用）；
- `-Changed`：只检查 git 改动涉及的域（pre-push 用）。

### 5.3 检查清单

1. 契约：18 域 props 存在（16 modules + native_tui + native_gui）、`JccDomain` 与目录一致、allowlist 域名合法。
2. 图：生产 code 边逐条 ∈ allowlist；目标项目 ∈ `apiProjects`；无环。
3. 模块：域内 csproj ⊆ `module.slnx` ∪ `JccSlnxExcluded`；slnx 允许含域外构建依赖；README/CHANGELOG/docs 存在；README 表格行数 = csproj 数。
4. 边界：生产→测试引用为 0（白名单除外）。
5. 预算：每个项目 code 入度 ≤ baseline；新增跨域边必须带 allow 条目。
6. 基线：门禁规则每条违规计数 ≤ baseline；allow 条目必须有 `reason` + `adr` + `expires`，过期即 Error。分析器规则不在 baseline 内，靠 `.editorconfig` severity 控制。

### 5.4 基线格式

```json
{
  "version": 1,
  "updated": "2026-11-21",
  "apiProjects": ["modules/abstractions/abstractions/Abstractions.csproj", "..."],
  "ruleCounts": {
    "JCC12002": { "abstractions": 32, "cognition": 6 },
    "JCC12003": { "abstractions": 32 },
    "JCC12009": { "modules/cognition/brain.context_core": 1 }
  },
  "hubBudget": {
    "modules/abstractions/abstractions/Abstractions.csproj": 88,
    "modules/abstractions/infrastructure/Infrastructure.csproj": 53
  },
  "allow": [
    {
      "rule": "JCC12004",
      "from": "modules/storage/vault.testing.common",
      "to": "test/unit/testing.common",
      "reason": "测试辅助项目被 Vault 测试复用；不属于生产依赖",
      "adr": "0161",
      "expires": "2027-03-31"
    }
  ]
}
```

### 5.5 输出与退出码

- stdout：每条违规一行 `RULE | project | detail`；
- `-OutputMarkdown <path>`：生成人读报告（按域分组 + 环路径 + 基线对比）；
- `-UpdateBaseline`：只允许在 P0/P4 显式执行，CI 中禁用；
- 退出码：`0` 通过，`1` 违规，`2` 配置/解析错误。

### 5.6 CI 接入

新增 `.github/workflows/ci-architecture.yml`（由 `ci.yml` 调用）：

```yaml
architecture:
  runs-on: ubuntu-latest
  steps:
    - uses: actions/checkout@v4
    - run: pwsh ./non_deliverables_tools/architecture/ArchitectureGate.ps1 -Mode enforce -OutputMarkdown artifacts/architecture-report.md
    - uses: actions/upload-artifact@v4
      if: always()
      with:
        name: architecture-report
        path: artifacts/architecture-report.md
```

门禁放在编译之前（只解析 XML，秒级），失败即整条 CI 红灯，不浪费构建配额。

---

## 6. 分阶段实施

> 每个阶段遵守：编译 → 单测 → git commit；阶段内小步提交，禁止一次性大改。

### P0 基线冻结（只读）— 已完成（2026-11-21）

**改动**：
- `non_deliverables_tools/architecture/ArchitectureGate.ps1`：单脚本实现 `Report` / `Baseline` / `Validate`，解析 259 个 csproj，区分 code / analyzer / test 边。
- `non_deliverables_tools/architecture/baseline.json` v0：冻结 15 条环、474 条生产跨域边、26 个 slnx 缺口、176 个目录名、51 个 API 项目、99 个 IVT 项目。
- `non_deliverables_tools/architecture/README.md`：用法、规则子集、基线纪律、统计口径。
- 人读报告写入 `.xxx/analysis/architecture-gate-report.txt`（临时证据，不入库）。

**验收**：
- `ArchitectureGate.ps1 -Mode Report` 退出码 0，不修改任何源码；
- `ArchitectureGate.ps1 -Mode Validate` 对 v0 基线 PASS；
- 报告数字与第 1 章一致；测试项目已排除出生产图（703 边 / 474 跨域）；
- 基线路径统一 `/`、`repoRoot="."`，可在 CI 复用。

### P1 契约落地 — 已完成（2026-11-21）

**改动**：
- 18 份契约文件：14 份新增 + 4 份追加（abstractions / generators / native_tui / native_gui 已有 props，只追加属性，不覆盖原内容）；
- 根 `Directory.Build.props` 增加 6 个 `CompilerVisibleProperty`，供 P2 的 `ProjectContext` 读取；
- allowlist 先留空，P3 按域对评审后填入；不在 P1 把现有跨域边直接写成"允许"。

**验收**：
- `pwsh non_deliverables_tools/scripts/Build-AllDomains.ps1 -NoAnalyzers` 全通过（solution + native_tui + native_gui）；
- 门禁报告 `contracts 18/18`、`JCC12001 = 0`，基线已同步冻结为 0；
- `JccDomain` 与目录不一致时门禁报 Error。

**遗留（P1.5）**：
- 18 个域当前统一 `JccApiVisibility=internal`；51 个实际对外项目还需要在 csproj 覆盖 `public`，否则 P2 的 `JCC12006` 会把存量全部报出来。
- `JccNamespaceRoots` 暂不填：各域真实命名空间高度分散，先做 P4 归一，避免把现状写成契约。

### P2 编译期规则

**改动**：
- `rules/architecture/`：`ModuleContractRule`(12001)、`PublicApiBoundaryRule`(12006)、`IvtAllowlistRule`(12007)、`NamespaceRootRule`(12008)、`PrivateLayerRule`(12013)；
- `ArchitectureRules.cs` 聚合注册；
- `modules/generators/aot_safety.tests/architecture/` 增加 fixture 测试（每条规则正例/反例各 1）；
- `.editorconfig` 先把 5 条新分析器规则统一设为 `suggestion`（只报告、不阻断存量），P4 起逐条翻 `error`。

**验收**：
- `dotnet build modules/generators/module.slnx -c Debug` 通过；
- 测试证明 5 条分析器规则均能触发；故意在 fixture 中写 `public` 类型于 `internal` 项目 → 规则命中；
- 全仓构建通过（存量违规以 `suggestion` 呈现，不阻断）；

### P3 仓库门禁

**改动**：
- `RuleGraph.ps1`：12002/12004/12005/12012；
- `RuleModule.ps1`：12003/12010/12011；
- `RuleLayout.ps1`：12009（目录命名，含建议新名）；
- `ArchitectureGate.ps1` 接入 baseline 比较与 allow 到期；
- `.github/workflows/ci-architecture.yml` + `ci.yml` 调用；
- `README.md` 使用说明。

**验收**：
- 注入一条 `cognition → services` 未授权引用（不在 allowlist、无 allow 条目）→ 门禁 Error 并指出 from/to；
- 构造一个 `expires` 已过期的 allow 条目 → 门禁 Error；
- 撤销注入 → 绿灯；
- PR 上 CI 架构 job 可见且阻断。

### P4 债务清偿

**改动（按顺序，逐项 ADR）**：
1. 补齐域内 slnx 缺口：abstractions 6 个（Brain/Composition/Guard/Memory/Perception/Primitives.Tools.Tests）、generators 1 个（AotHandler.Generator）、perception 4 个（Eyes/Eyes.Tests/Vision/Vision.Tests）；tools 的 13 个 `cli_nodes/groups/*` + sample + onnx 测试先决定"登记或排除"，排除项写入 `JccSlnxExcluded` 并注明打包流程。
2. 生成 `README.md` 项目表（脚本从 csproj 生成，替换手写表）。
3. 拆 `Infrastructure`：PithosDB/Plugins.Contracts/Downloader 适配层分别下沉到 persistence/plugins/storage 域；`Infrastructure` 只保留 DI/Options/Hosting/Logging。
4. 消 `abstractions ↔ async_primitives/plugins/storage` 环；消 `storage ↔ composition`、`apps ↔ native_tui` 环。
5. 176 个目录名的批量改名（脚本先单域验证再推广）。

**验收**：
- `slnx 16/16`（域内项目全部覆盖或显式排除）；
- 生产域环 = 0（或仅剩带 `expires` 的显式 allow）；
- `Infrastructure` code 入度下降到域内共享层合理区间，出度只含 abstractions/async/collections；
- 全仓 `Build-AllDomains.ps1` 通过。

### P5 严格模式

**改动**：
- `.editorconfig` 把 5 条分析器规则全部翻 `error`；
- baseline 中门禁规则计数清零（Hub 预算/allow 除外）；
- 门禁 `-Mode enforce` 在 `ci.yml` 默认阻断，`-UpdateBaseline` 仅本地可用。

**验收**：
- 新 PR 任意一条架构违规 → CI 红灯；
- 全仓 0 baseline 违规（Hub 预算/allow 除外）；
- 文档：`docs/implemented/adr/0161` + 本 DSG 转入 `implemented/`。

---

## 7. 目录树（拟新增 / 修改）

```
modules/
  <domain>/Directory.Build.props              # 新增 ×16（modules 域）
native_tui/Directory.Build.props              # 新增 ×1
native_gui/Directory.Build.props              # 新增 ×1
modules/
  generators/aot_safety.generator/
    ArchitectureRules.cs                      # 新增（聚合注册）
    rules/architecture/
      ModuleContractRule.cs                   # 新增 JCC12001
      PublicApiBoundaryRule.cs                # 新增 JCC12006
      IvtAllowlistRule.cs                     # 新增 JCC12007
      NamespaceRootRule.cs                   # 新增 JCC12008
      PrivateLayerRule.cs                     # 新增 JCC12013
  generators/aot_safety.tests/
    architecture/                             # 新增 fixture 测试
non_deliverables_tools/architecture/
  ArchitectureGate.ps1                        # 新增
  README.md                                   # 新增
  baseline.json                               # 新增（P0 生成）
  private/
    GraphModel.ps1                            # 新增
    RuleContract.ps1                          # 新增
    RuleGraph.ps1                             # 新增
    RuleModule.ps1                            # 新增
    RuleLayout.ps1                            # 新增
    RuleBaseline.ps1                          # 新增
    Report.ps1                                # 新增
.github/workflows/
  ci-architecture.yml                         # 新增
  ci.yml                                      # 修改：调用 architecture job
Directory.Build.props                         # 修改：CompilerVisibleProperty
.editorconfig                                 # 修改：JCC12xxx severity（P2/P5）
```

---

## 8. 验收表（四列强制）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|----------|--------|--------|--------|
| 18 个域 `Directory.Build.props` 契约属性（16 modules + 2 UI） | `ProjectContext`、门禁、CI | ❌ | ❌（需 ADR 0080 手动 exe/门禁验收） |
| `CompilerVisibleProperty` 透传 | `aot_safety.generator` | ❌ | ❌ |
| `ProjectContext` 域字段 | 5 条编译期规则 | ❌ | ❌ |
| `JCC12001/12006/12007/12008/12013` 分析器规则 | `dotnet build`、IDE | ❌ | ❌ |
| `JCC12002/12003/12004/12005/12009/12010/12011/12012` 门禁规则 | `ArchitectureGate.ps1` | ❌ | ❌ |
| `baseline.json` + 只减不增比较 | 门禁、CI | ❌ | ❌ |
| `ci-architecture.yml` | PR CI | ❌ | ❌ |
| 模块 README 生成器 | P4 文档同步 | ❌ | ❌ |

> 后两列为强制钩子：`已实现` 防止"写了规划没实现"；`已验收` 必须走真实运行（门禁注入违规、分析器 fixture、全仓编译），禁止只写单测。

---

## 9. 决策队列（已定稿）

> 2026-11-21 用户明确授权施工方自行决定，不再逐项征求选择。以下为定稿，按此施工。

| 序号 | 决策 | 选项 | 定稿 |
|------|------|------|------|
| D1 | `Infrastructure` 的最终归属 | A 拆成多个能力包留在 abstractions；B 独立为第 17 域；C 保留并长期豁免 | **B 先行，A 随后**：先承认它是共享基础设施层，再逐步拆适配器 |
| D2 | 公开 API 边界强度 | A 51 个对外项目标 `public`，其余 131 个声明公开类型的项目逐步 `internal` 并加规则；B 只标记不强制；C 仅 SDK 对外 | **A**：边界必须有准入，否则 `internal` 形同虚设 |
| D3 | 门禁阻断时机 | A PR 全阻断；B 只阻断新增违规（存量报告）；C nightly 报告 | **B → P5 转 A**：先保证不恶化，再清零 |

---

## 10. 风险与对策

| 风险 | 影响 | 对策 |
|------|------|------|
| allowlist 变成"随手加一行" | 契约退化 | allow 条目必须带 `reason` + `adr` + `expires`；过期报 Error；PR 模板要求贴 ADR |
| 182 个项目公开类型存量过大 | 规则噪音淹没真实违规 | P2 先 `suggestion`，按域分批翻 `error`；优先处理跨域被引用的 51 个项目 |
| 目录改名 176 处误伤 csproj/slnx 路径 | 构建断裂 | 用脚本先单域验证，改名后立即 `Build-AllDomains -Changed`；禁止 PowerShell 批量改源码（ADR 0087） |
| `Infrastructure` 拆分引发 DI 注册漂移 | 运行时故障 | 拆分类似"换实现"，必须先加 E2E/冒烟测试；每次只移一个适配器 |
| 门禁与分析器规则重复实现、语义分叉 | 两套真相 | 同一 ID 只在一个执行面实现；`JCC12001` 双面规则共享同一份域名校验常量，禁止各写一套 |
| MSBuild 属性跨域继承意外生效 | 误判 | 域 props 显式 Import 上层；门禁按路径推导项目域，并校验域 props 的 `JccDomain` 与目录名一致 |

---

## 11. 不做的事（Out of Scope）

- 不做全仓 `public → internal` 一次性改造；
- 不做运行时代码重构、不改 DI 生命周期；
- 不引入 YAML/JSON 形式的模块契约清单（`baseline.json` 只存违规计数，不承担契约）；
- 不把架构规则塞进第三方分析器（如 NDepend）；
- 不以"层级号"作为唯一判定依据。

---

## 12. 施工记录

- 2026-11-21：P0 完成。`ArchitectureGate.ps1` + `baseline.json` v0 + `README.md` 入库；`-Mode Validate` 对基线 PASS。
- 2026-11-21：P1 完成。18 份域契约（14 新增 + 4 追加）落地；`contracts 18/18`；全仓 `Build-AllDomains -NoAnalyzers` 通过；基线把 `JCC12001` 冻结到 0。
- 决策：P0 不按原计划拆 `GraphModel.ps1` / `RuleGraph.ps1` / `RuleLayout.ps1` 三文件，先用单脚本闭环；等 P2 规则数量上来再按职责拆分，避免过早抽象。
- 口径修正：P0 首次运行发现测试项目分类正则写错（把 `test` 路径漏判），修正后生产边从 728 回到 703、跨域从 491 回到 474、环从 16 回到 15；同时纠正 `JCC12011`（文档齐备）与 `JCC12013`（`private/` 层次）的规则映射，二者不再被误记为 IVT / 命名空间。
- 发现：各域真实命名空间高度分散（如 storage 的 `Core.Goal`、`Core.Scheduling`，tools 的 `McpToolDispatch`），P1 不猜 `JccNamespaceRoots`；命名空间契约留到 P4 归一后再填。

## 13. 附录：指标口径与复现

```powershell
# 架构门禁：报告 / 基线 / 校验
pwsh non_deliverables_tools/architecture/ArchitectureGate.ps1 -Mode Report
pwsh non_deliverables_tools/architecture/ArchitectureGate.ps1 -Mode Baseline
pwsh non_deliverables_tools/architecture/ArchitectureGate.ps1 -Mode Validate

# 历史诊断脚本（只读，保留在 .xxx/analysis 作为本轮证据）
pwsh .xxx/analysis/dependency-graph.ps1 -RepoRoot D:\project\w4 -OutFile .xxx/analysis/dependency-graph.txt
pwsh .xxx/analysis/structure-audit.ps1
pwsh .xxx/analysis/analyzer-rules.ps1
```

口径说明：

- code 边 = `ProjectReference` 且不含 `OutputItemType="Analyzer"`；
- analyzer 边 = 含 `OutputItemType="Analyzer"`；
- 测试项目 = 项目名以 `.tests` / `.test` 结尾，或路径段含 `test` / `tests`；
- 生产图 = 排除测试项目与 `test/` 目录；
- 入度/出度均按 code 边统计；
- 公开类型数为 `public class/record/interface/enum/struct` 声明数（近似值，用于基线，不用于精确 API 清单）。

> 注：P0 已用单脚本 + `baseline.json` 落地；`.xxx/analysis/` 脚本仅作为本轮分析证据留存，不再作为主入口。
