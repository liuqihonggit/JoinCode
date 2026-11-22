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
| 规则数量 | 103 条（新增 5 条架构规则：12001/12006/12007/12008/12013） | 代码级规则强，架构级规则 0 → 5 |
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
- ✅ 51 个实际对外项目已在 csproj 覆盖 `JccApiVisibility=public`（2026-11-21，P4 前置；51 文件，全仓带分析器构建通过）。其余项目继续继承域默认 `internal`。
- `JccNamespaceRoots` 暂不填：各域真实命名空间高度分散，先做 P4 归一，避免把现状写成契约。

### P2 编译期规则（完成）

**已落地**：
- `rules/architecture/ArchitectureDomainCatalog.cs`：18 个合法域的唯一常量源 + 契约项目路径判断 + 逗号/分号清单拆分（分析器与门禁共享口径）；
- `ModuleContractRule`（`JCC12001`）：域契约缺失 / 未知域 / allowlist 含未知域；跳过测试项目与非交付工具项目；定位到首个语法 token；`IsCompilationEnd`；
- `PublicApiBoundaryRule`（`JCC12006`）：`JccApiVisibility=internal` 下不得声明有效 public 的命名类型（含嵌套可见性链）；
- `IvtAllowlistRule`（`JCC12007`）：扫描所有 `[assembly: InternalsVisibleTo]` 语法，生产程序集目标必须在 `JccAllowInternalsVisibleTo` 登记，`.Tests/.Test/.Tests.*` 豁免；
- `NamespaceRootRule`（`JCC12008`）：类型命名空间必须落在 `JccNamespaceRoots` 任一前缀（按 `.` 边界匹配，`JoinCode.LlmX` 不会被 `JoinCode.Llm` 误放行）；
- `PrivateLayerRule`（`JCC12013`）：`private/` 目录下的 public 类/结构/接口/枚举/record 报违规；
- `ProjectContext` 扩展：`ProjectDirectory`、`Domain`、`AllowedDomains`、`ApiVisibility`、`NamespaceRoots`、`AllowInternalsVisibleTo`、`SlnxExcluded`，由 `build_property.*` 注入；
- `.editorconfig`：`JCC12001=warning`（缺失契约立即阻断），`JCC12006/12007/12008/12013=suggestion`（观察期只报告存量）。

**验收事实**：
- 单测：`ModuleContractRuleTests` 6/6 + `ArchitectureRulesTests` 14/14，共 20/20 通过；
- 真实负向探针（每条规则都在真实 csproj 触发，不是 fixture 假绿）：
  - `JCC12001`：`-t:Rebuild -p:JccDomain=` → `error JCC12001: 缺少 JccDomain`；
  - `JCC12006`：`Abstractions.Configuration` 临时把规则升 warning → 17 个 public 类型命中（`VendorModelEntry`/`HubConfigJsonContext`/`ModelCatalog` 等）；
  - `JCC12007`：`Abstractions.Exceptions` 的生成 IVT `JoinCode.Plugins.Infrastructure` 未登记 → 命中；
  - `JCC12008`：`-p:JccNamespaceRoots=ZZZ` → 18 个类型命中，报告实际命名空间 `JoinCode.Abstractions.Configuration.Llm`；
  - `JCC12013`：临时 `private/PrivateLayerProbe.cs` 声明 public → 命中 `private/ 目录却声明为 public`；
- 全仓 `Build-AllDomains.ps1`（**带分析器**）四个 target 全 True。

**四条必须记住的工程事实**：
1. 生成的 `*GeneratedMSBuildEditorConfig.editorconfig` 中只有 `build_property.ProjectDir`，**没有** `build_property.MSBuildProjectDirectory`；`ProjectContext` 必须读前者。
2. `dotnet build` 默认增量不会重跑分析器；负向验收必须 `-t:Rebuild`，否则会得到"0 警告"的假阴性。
3. `DiagnosticSeverity.Info/suggestion` 不会出现在 `dotnet build` 输出；真实验收必须临时把目标规则升为 warning/error，或等 P5 全局升 error。
4. `JCC12007` 不能只读 `Compilation.Assembly.GetAttributes()`：生成 `AssemblyInfo.cs` 在 `obj/` 下，会被 `GeneratedCodeAnalysisFlags.None` 的生成代码过滤；必须直接扫描 `Compilation.SyntaxTrees` 的 `[assembly: ...]`，并把诊断落在首个非 `obj/` 源文件上。

### P3 仓库门禁（完成）

**已落地**：
- `ArchitectureGate.ps1` 接入 `allowlist.json`：`from/to/reason/adr/expires` 五字段校验，未知域、重复、非法日期、过期即 Validate 失败；
- baseline 增加 `crossDomainEdges`（474 个生产域对），Validate 同时做"计数只减不增 + 域对集合只减不增"：新增域对不在基线、也不在有效 allowlist → `JCC12002 新增跨域依赖未授权`；
- `ci-architecture.yml`（`workflow_call` + `workflow_dispatch`，ubuntu `pwsh` 跑 Validate），`ci.yml` 新增 `architecture` job；
- `README.md` 补 allowlist schema、基线纪律与 CI 用法。

**验收事实**：
- 注入 `collections → cognition` 未授权引用 → Validate exit 1：`JCC12002 增长 474→475`、`JCC12005 增长 15→18`、`JCC12002 新增跨域依赖未授权: collections->cognition`；撤销后 PASS；
- 构造 `expires=2020-01-01` 条目 → exit 1：`allowlist 条目已过期`；改成 `2099-12-31` → PASS；
- 正向允许：新增无环边 `guard->llm` 时，无 allowlist → exit 1（新增域对 + 计数 474→475）；带有效 allowlist → PASS（计数按允许边豁免到 474，域对被授权）；
- 正常仓库 `-Mode Validate` PASS；`-Mode Baseline` 已重冻结含 `crossDomainEdges`。

**边界**：allowlist 只豁免 `JCC12002` 的新增边与计数；**不豁免 `JCC12005` 环**——引入成环依赖仍失败。长期依赖仍应进基线，allowlist 只做有到期日的临时例外。

**决策**：暂不按原计划拆 `RuleGraph.ps1` / `RuleModule.ps1` / `RuleLayout.ps1`；当前规则数仍少，单脚本闭环更利于一致性。等 P4 债务清偿需要按规则族独立测试时再拆。

### P4 债务清偿（进行中）

**已完成 0. API 准入**：51 个被跨域引用的项目在 csproj 覆盖 `JccApiVisibility=public`，其余保持 `internal`；全仓带分析器构建通过。

**已完成 0.1 无效跨域引用清理**：删除 `Abstractions -> AsyncLock` 与 `Infrastructure -> Plugins.Contracts` 两条未被源码使用的引用；生产跨域边 474 → 472，生产边 703 → 701；全仓 `Build-AllDomains.ps1` 四 target True；门禁 Validate PASS。域环仍为 15（这两条不在环路径上）。

**已完成 0.2 模块 README 项目表**：`Update-ModuleReadme.ps1` 为 16 域生成 202 行项目表；`JCC12011` 从仅检查文件存在扩展为检查行数；负向探针（删 1 行）确认 `1/2` 漂移会 exit 1。

**已完成 0.3 遥测插件归位**：`TelemetryPlugin` 从 `abstractions/infrastructure_telemetry` 移入 `plugins/plugins.infrastructure/builtin/telemetry`（插件实现归插件域）；`Infrastructure.Telemetry` 移除 `Plugins.Contracts` 引用并声明 `JccApiVisibility=public`（被 plugins 域引用）；`Plugins.Infrastructure` 显式引用 `Infrastructure.Telemetry`。生产跨域边 472 → 478，其中 6 条是**传递依赖显式化**（见 0.4），非新增耦合；域环仍 15（未触及环路径）。

**已完成 0.4 传递依赖显式化**：删 `Infrastructure -> Plugins.Contracts` 后，`llm/core`、`services/dream`、`services/bridge`、`perception/code_index`、`apps/engine` 这几个直接使用 `IWorkflowPlugin` / `WorkflowPluginBase` / `PluginContext` / `PluginResourceBase` 的项目原先靠传递引用编译；现各自显式声明 `Plugins.Contracts`，`apps/engine` 另加 `Plugins.Infrastructure`（`McpInitModule` 编译期引用 `TelemetryPlugin`）。暴露口径问题：`solution` 增量构建会掩盖传递引用断裂，只有 `native_tui` / `native_gui` 独立 target 才报错；修复后全量 `-t:Rebuild` 通过（0 警告 0 错误）。

**已完成 0.5 `JCC12003` 落地**：门禁里该规则此前硬编码 0（等于没校验）。改为真实计算：跨域 code 边目标项目的 `JccApiVisibility`（项目级覆盖 > 域契约默认 > `internal`）必须为 `public`；负向探针把 `Infrastructure.Telemetry` 降为 `internal` → 报 1 条 `[plugins->abstractions]`，恢复后 PASS。

**已完成 0.6 Infrastructure 拆分第一刀（GitHub 客户端出迁）**：
- 新增 `modules/tools/github_client/GitHubClient.csproj`（`AssemblyName=JoinCode.GitHub.Client`），承接原 `abstractions/infrastructure/io/process/` 下的 `GitHubApiClient`、`GitHubApiClient.PortCheck`、`GitHubApiJsonContext`；只引用 `Abstractions`、`Abstractions.Primitives.Interfaces`、`Downloader`，不再拖入整个 Infrastructure。
- `Infrastructure` 移除 `Downloader` 引用与 Downloader 全局 using；原先靠传递引用使用下载器的 6 个生产项目（`hands`、`hands_core`、`hands_web`、`mcp`、`bridge`、`code_index`）与 3 个测试项目改为显式声明 `Downloader`。
- 测试随实现就近：`GitHubApiClientTests` / `GitHubApiClientPortCheckTests` 从 `test/unit/infra.tests/io/process/` 迁入 `modules/tools/github_client_tests/`（目录名下划线，符合 JCC12009），新项目 `GitHubClient.Tests` 51/51 通过；不再向跨域测试项目开 `InternalsVisibleTo`（仅对本项目 `.Tests` 程序集开）。
- `gh` 手工 `new GitHubApiClient(...)`，不经 DI 容器；`[Register]` 仍随程序集生成，主引擎无 `IGitHubApiClient` 解析点，迁移不改运行期注册语义（已由 `jcc-gh.exe alias list` 真实构造验证）。
- 效果：生产跨域边 478 → 486（`abstractions` −1、`cognition` +3、`mcp/perception/services` 各 +1、`tools` +3），全部是"把原本经 Infrastructure 传递的依赖显式画进图里"，不是新增耦合；生产域环 **15 → 8**（`abstractions ↔ storage` 的 7 条环随 Infrastructure 去掉 Downloader 消失）；`JCC12009` 保持 176。

**已完成 0.7 门禁测试口径修正（`JCC12004` 清零）**：
- 根因：门禁只用"项目名 `.tests` 结尾 / 路径含 test 段"启发式判定测试项目，`modules/storage/vault.testing.common`（已声明 `ProjectType=test`）被误判为生产项目，导致 `JCC12004 生产→测试` 长期挂 1。
- 修正：`Test-IsTestProject` 优先读取 csproj 的 `ProjectType`（新增带缓存的 `Get-DeclaredProjectType`，避免每条边重复解析），名字/路径启发式降为兜底。
- 效果：`JCC12004 1 → 0`；被误判项目从生产图剔除后，生产边 716→713、生产跨域边 486→485；基线重冻结，Validate PASS。

**已完成 0.8 公开 API 禁元组规则（`JCC10011`）**：
- 规则：`PublicTupleApiRule`（`AnalyzerId=CodeOrganization`、`Id=JCC10011`、`Category=CodeStyle`、`Severity=Info`），`SymbolAction` 覆盖方法/属性/字段/事件；递归查泛型实参、数组元素（深度上限 6）；可见性要求自身与所有外层类型均为 public/protected/protected-or-internal；跳过隐式成员、属性访问器、非 ordinary 方法体与测试项目。
- 测试：`PublicTupleApiRuleTests` 8 用例（公开方法返回元组 / 公开方法元组参数 / 公开属性嵌套 `List<(int,string)>` / 公开字段 → 报；internal 方法 / internal 类型内公开方法 / 具名 record / 测试项目 → 不报），8/8 绿。
- 真实构建探针：`JCC10011` 临时升 `warning`（`TreatWarningsAsErrors` 表现为 error）→ `Structura.csproj` 报 4 条 `SkipList.Range/RangeFrom/PrefixRange/Enumerate`；确认规则在真实构建链路命中（不是 fixture 假绿）。
- 债务清理：`SkipList` 4 个公开签名由 `IEnumerable<(TKey Key, TValue Value)>` 改为 `IEnumerable<KeyValuePair<TKey, TValue>>`（唯一消费方是同模块测试，`.Key`/`.Value` 访问不变）；`Structura.Tests` 401/401 绿，collections 域在 `JCC10011=warning` 下零命中。
- 收口：`.editorconfig` 还原为 `suggestion`（观察存量），P5 清账后升 error。
- 债务口径与清偿（2026-11-22）：按 20 个 target（16 域 `module.slnx` + `native_tui` + `native_gui` 两测试项目 + `host.tests`）临时升 `warning` 后 `-t:Rebuild` 精确盘点，全仓存量 **79 处 / 32 项目**（增量构建不重跑分析器，会把存量严重低估）。按域分五批清偿：mcp/guard/apps（`a7bbfc59b`）、composition/services/llm/native_gui（`3341efa00`）、storage（`74da2d5af`）、perception（`fb219f4f0`）、abstractions+cognition+tools（`8f12a7d6b`，新增 28 个具名 record）；随后清掉非交付工具 `jcc_audit_ast_cli` 残留 11 处（新增 10 个 record，工具测试 61/61 绿）。最终 `JoinCode.slnx` 218 项目在 `JCC10011=warning` 下 `-t:Rebuild -m:1` 得 **0 命中 / 0 错误**；`native_tui`、`native_gui` core/host、`host.tests` 亦 0 命中；`.editorconfig` 由 `suggestion` 升为 `warning` 防新增（P5 再统一升 `error`）。

**已完成 0.9 `storage ↔ composition` 环清零（2026-11-22）**：
- 根因：`storage/clock` 通过 `Composition` 传递引用消费 cognition 的 `IEmptyResponseTracker` 与 guard 的 `ClusterPlanApprovalHook*`，使 `storage -> composition` 成立，叠加 `composition -> storage` 形成 6 条生产域环。
- 契约上移：`IEmptyResponseTracker` 迁入 `Abstractions.Brain.Context`（`modules/abstractions/brain/context/loop`），实现 `EmptyResponseTracker` 仍留 cognition；`IClusterPlanApprovalHookManager` / `ClusterPlanApprovalHookContext` / `ClusterPlanApprovalHookResult` 迁入 `Abstractions.Hooks`（`abstractions.primitives.pipeline`），实现 `ClusterPlanApprovalHookManager` 仍留 `Guard.Hooks`。
- `Clock` 删除 `Composition` 引用（不再依赖 guard 或 cognition 的传递引用）；`JCC12005 8 → 2`、`JCC12002 485 → 484`、`JCC12003 0`。全仓 218 项目 `-t:Rebuild -m:1` 0 警告 0 错误，`Build-AllDomains` 四 target 全 True；`Clock.Tests` 519/519、`Guard.Hooks.Tests` 598/598；全量 60 个单元测试项目 24678 通过 / 0 失败（日志 `.xxx/logs/full-tests-final3`）。此前满载首轮暴露 `Infra.Utils` 的 `SingleDelayCap` 时序断言过紧（150ms），放宽到 400ms（仍低于未限速 500ms）后复跑 561/561。
- 当时剩余 2 条环：`abstractions ↔ async_primitives` 与 `apps ↔ native_tui`（前者已在 0.10 清零）。

**已完成 0.10 `abstractions ↔ async_primitives` 环清零（2026-11-23）**：
- 根因：`Abstractions.Utils` / `Infrastructure` 系项目依赖 `BclBridge`、`AsyncLock`、`Transport.Contracts`，而三者又依赖 `Abstractions.Primitives`，形成域级双向边。
- 域归属修正：`BclBridge`、`AsyncLock`、`Transport.Contracts` 从 `async_primitives` 迁入 `abstractions`；测试目录同步为 `bcl_bridge_tests` / `async_lock_tests`，契约目录改为 `transport_contracts`；`async_primitives` 只保留 `Transport.Impl` 及测试。
- 同步改动：30+ 个消费方 csproj、`JoinCode.slnx` / `Dev` / `Pack` / `Stryker` 四个 slnx、CI 单元与 Flaky 分片、`JCC4008` 锁原语存量基线路径、两个域 README 项目表。
- 指标：`JCC12005 2 → 1`、`JCC12002 484 → 469`、`JCC12009 175 → 173`、`JCC12003 0`；全仓 218 项目 `-t:Rebuild -m:1` 0 警告 0 错误；`BclBridge` 41/41、`AsyncLock.Tests` 546/546、`Transport.Impl.Tests` 218/218 通过（全量测试交由 CI）。
- 剩余 1 条环：`apps ↔ native_tui`（`apps/cli` 引用 `NativeTui.Host`，后者引用 `apps/engine`）。

**后续改动（按顺序，逐项 ADR）**：
1. ✅ 补齐域内 slnx 缺口（`827e8fca0`，26→0；abstractions/generators/perception 全部登记，tools 的 13 个 `cli_nodes/groups/*` 与 sample/onnx 测试直接纳入，无排除项）。
2. ✅ 生成 `README.md` 项目表（`Update-ModuleReadme.ps1`，16 域已按域内 csproj 重建；`JCC12011` 校验行数）。
3. 拆 `Infrastructure`：PithosDB/Plugins.Contracts/Downloader 适配层分别下沉到 persistence/plugins/storage 域；`Infrastructure` 只保留 DI/Options/Hosting/Logging。（进行中：GitHub 客户端已出迁 tools 域，Downloader 依赖已摘除，环 15→8）
4. 消 `abstractions ↔ async_primitives/plugins/storage` 环；消 `storage ↔ composition`、`apps ↔ native_tui` 环。（进行中：`storage ↔ composition` 6 条清零，`abstractions ↔ async_primitives` 清零，环 8→1；仅剩 `apps ↔ native_tui`）
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
| 18 个域 `Directory.Build.props` 契约属性（16 modules + 2 UI） | `ProjectContext`、门禁、CI | ✅ | ✅ 全仓带分析器编译通过 + 18/18 契约 |
| `CompilerVisibleProperty` 透传 | `aot_safety.generator` | ✅ | ✅ 真实 `GeneratedMSBuildEditorConfig` 可见 `ProjectDir` / `JccDomain` |
| `ProjectContext` 域字段 | 5 条编译期规则 | ✅ | ✅ 6 个单测 + `-p:JccDomain=` 真实构建报错 |
| `JCC12001` 分析器规则 | `dotnet build`、IDE | ✅ | ✅ `-t:Rebuild -p:JccDomain=` 报 `error JCC12001`；全仓无误报 |
| `JCC12006/12007/12008/12013` 分析器规则 | `dotnet build`、IDE | ✅ | ✅ 真实 csproj 探针分别命中（12006 17 类型 / 12007 生产 IVT / 12008 18 类型 / 12013 private probe） |
| `JCC12002/12003/12004/12005/12009/12010/12011/12012` 门禁规则 | `ArchitectureGate.ps1` | ✅ | ✅ 注入新边/过期 allowlist 均 exit 1，撤销后 PASS |
| `baseline.json` + 只减不增比较 | 门禁、CI | ✅ | ✅ 计数 + `crossDomainEdges` 域对双层比较；P3 重冻结 |
| `ci-architecture.yml` | PR CI | ✅ | ✅ 已接入 `ci.yml` `architecture` job（待 PR 首跑验证） |
| 模块 README 生成器 | P4 文档同步 | ✅ `Update-ModuleReadme.ps1` | ✅ 16 域已生成；删 1 行后 `JCC12011` 1/2 报错、exit 1，重跑恢复 PASS |
| `Infrastructure` 拆分第一刀（GitHub 客户端出迁 tools） | `tools/gh`、全仓生产图 | ✅ `GitHubClient.csproj` + `github_client_tests` 随迁 | ✅ 全仓四 target True；`jcc-gh.exe alias list` 真实构造客户端；门禁环 15→8 |
| `JCC12004` 测试项目口径（`ProjectType=test` 优先） | 门禁分类器 | ✅ `Get-DeclaredProjectType` 缓存 | ✅ `JCC12004 1→0`、`JCC12002 486→485`，Validate PASS |
| `JCC10011` 公开 API 禁元组 | `dotnet build`、IDE | ✅ `PublicTupleApiRule` + 8 fixture | ✅ 全仓 `-t:Rebuild` 采集 79 处，交付代码清零；非交付工具残留 11 处亦清零；`warning` 级 0 命中，工具测试 61/61 |

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
- 2026-11-21：P2 第一块完成（`JCC12001`）。`ArchitectureDomainCatalog` + `ModuleContractRule` + `ProjectContext` 域字段 + 6 个测试；6/6 绿；真实负向探针报 `error JCC12001`；全仓带分析器构建四 target 全 True。
- 2026-11-21：P2 完成。补齐 `JCC12006/12007/12008/12013` 四条规则 + 14 个 fixture 测试（含 `JCC12001` 合计 20/20）；四条规则均在真实 csproj 上负向命中；`.editorconfig` 设 `JCC12001=warning`、其余 `suggestion`；全仓带分析器构建通过。
- 关键发现（JCC12007）：生成 `AssemblyInfo.cs` 位于 `obj/`，`GeneratedCodeAnalysisFlags.None` 会过滤生成代码上的诊断；改为扫描 `Compilation.SyntaxTrees` 的 `[assembly: InternalsVisibleTo]`，并把诊断落到首个非 `obj/` 源文件。
- 关键发现（可见性）：`Info/suggestion` 诊断不会出现在 `dotnet build` 输出；真实负向验收必须临时升 warning/error，否则会误判"规则没生效"。
- 2026-11-21：P2 收尾验收。`ArchitectureGate.ps1 -Mode Validate` PASS；全仓 `Build-AllDomains`（带分析器）四 target 全 True；`jcc.exe --version` 启动正常（JoinCode v1.0.0.0 / .NET 10.0.9）。
- 2026-11-21：P4 前置（API 准入）完成。51 个被跨域引用项目覆盖 `JccApiVisibility=public`；其余项目保持 internal；全仓带分析器四 target True；门禁 Validate PASS。
- 2026-11-21：P4 第一块完成。26 个 `module.slnx` 缺口全部登记（abstractions 6、generators 1、perception 4、tools 15），四个域级 slnx 均独立构建通过；门禁 `JCC12010 26 → 0`，基线重冻结。
- 2026-11-21：P4 第二块完成。清理 2 条源码未使用的跨域引用（`Abstractions -> AsyncLock`、`Infrastructure -> Plugins.Contracts`）；生产跨域边 474 → 472；全仓四 target True；门禁 Validate PASS。
- 2026-11-21：P4 第三块完成。`Update-ModuleReadme.ps1` 从 csproj 生成 16 域 README 项目表（共 202 行项目）；`JCC12011` 增加行数校验；删除 collections 一行负向探针 → exit 1（1/2），重跑恢复 PASS。
- 2026-11-21：P3 完成。`allowlist.json` 五字段校验 + `baseline.crossDomainEdges` 域对集合 + 新增边阻断；注入 `collections->cognition` 与过期 allowlist 均 exit 1，撤销后 PASS；`ci-architecture.yml` 接入 `ci.yml`。
- 2026-11-21：P4 第四块完成（遥测插件归位 + 传递依赖显式化）。`TelemetryPlugin` 移入 `plugins.infrastructure/builtin/telemetry`；`Infrastructure.Telemetry` 去掉 `Plugins.Contracts`、声明 public；5 个生产项目补 `Plugins.Contracts` 显式引用，`apps/engine` 补 `Plugins.Infrastructure`。全量 `JoinCode.slnx -t:Rebuild` 0 警告 0 错误；`Build-AllDomains` 四 target 全 True；门禁 `JCC12003` 探针命中后恢复 PASS，基线重冻结（生产跨域边 478、环 15）。
- 2026-11-21：P4 第五块完成（`JCC12003` 从占位变真实规则）。负向探针：目标项目 `JccApiVisibility=internal` → `[plugins->abstractions]` 1 条；恢复 public → 0 条。
- 2026-11-21：P4 第六块完成（Infrastructure 拆分第一刀）。`GitHubApiClient` 及其 JSON 上下文迁入 `modules/tools/github_client`（`JoinCode.GitHub.Client`），测试迁入 `modules/tools/github_client_tests`（51/51 绿）；`Infrastructure` 摘除 `Downloader`，6 个生产项目 + 3 个测试项目显式声明下载器依赖。门禁环 15→8、生产跨域边 478→486（显式化）；全仓 `Build-AllDomains`（含分析器）四 target True；`jcc.exe --version` 与 `jcc-gh.exe alias list`（真实构造客户端）均通过；门禁 Validate PASS，基线重冻结。
- 工程事实（迁移与 DI）：把 `[Register]` 类型移出 Infrastructure 前必须确认消费方式。`gh` 是手工 `new`（不走容器），主引擎无 `IGitHubApiClient` 解析点，因此新程序集无需接入任何注册入口；若存在容器解析点，则必须在宿主侧显式调用新程序集的 `Add...AutoRegisteredServices`，否则编译通过、运行期缺服务。
- 工程事实（测试可见性）：跨程序集迁移后，测试对 `internal` 成员（`ExtractHostPort`/`IsPortReachableAsync`/`BuildNetworkErrorMessageAsync`/`IsZipPrefix`）的访问不能靠"给旧测试项目开 IVT"糊过去——按就近原则把测试迁到被测模块，IVT 只开给 `*.Tests` 目标（`JCC12007` 对测试程序集豁免）。
- 工程事实（目录命名）：新增测试目录一律用 `github_client_tests` 下划线形式；`.tests` 目录名在 `JCC12009` 口径下属于存量违规，新增即会让计数 +1，不要一边清账一边加账。
- 工程事实（API 准入 > 细粒度引用）：跨域引用必须指向 `JccApiVisibility=public` 的项目。实测把 `GitHubClient` 的 `Abstractions` 门面替换为 7 个细粒度 primitives 项目后，编译通过但 `JCC12003` 从 0 变 5（5 个目标非 public），跨域边 486→491。结论：门面仍存在时，跨域消费走 public 门面；"拆门面"必须先把目标项目纳入 API 准入评审，不能靠消费方单方面细粒度化。
- 工程事实（`storage/clock` 对 Composition 的依赖本质是契约错位，2026-11-22 已解除）：`Clock.csproj → Composition` 表面是真实依赖（`Core.Context`、`Core.Hooks`、`IEmptyResponseTracker`），但实际只需 cognition/guard 的两个契约——`IEmptyResponseTracker` 与 `ClusterPlanApprovalHook*`。把契约分别下沉到 `Abstractions.Brain.Context` 与 `Abstractions.Hooks` 后，`Clock` 摘除 `Composition` 可 0 警告编译（实现仍留 cognition/guard）。原判"必须上移 Goal 引擎"过重，正确做法是先把被消费的契约下沉。
- 2026-11-21：P4 第七块完成（门禁测试口径修正）。`Test-IsTestProject` 优先读 `ProjectType=test`（带缓存），`vault.testing.common` 不再被算作生产项目；`JCC12004 1→0`、生产跨域边 486→485，基线重冻结，Validate PASS。
- 2026-11-21：P4 第八块完成（`JCC10011` 公开 API 禁元组）。规则 + 8 fixture 全绿；真实 `Structura` 构建命中 4 条并清理为 `KeyValuePair`，该域 warning 级别零命中、401/401 测试通过；`.editorconfig` 收口为 `suggestion`。
- 2026-11-22：P4 第九块完成（`JCC10011` 全仓存量归零）。按域五批把交付代码 79 处公开元组改为具名 `record`（跨批新增 DTO 三十余个），非交付工具 `jcc_audit_ast_cli` 残留 11 处同批清零（新增 10 个 record）；`JoinCode.slnx -t:Rebuild -m:1` 在 `warning` 级 0 命中 0 错误；`.editorconfig` 升 `warning` 防新增（P5 再统一升 `error`）。
- 2026-11-22：P4 第十块完成（`storage ↔ composition` 环清零）。`IEmptyResponseTracker` 契约下沉到 `Abstractions.Brain.Context`，`ClusterPlanApprovalHook*` 下沉到 `Abstractions.Hooks`；`Clock` 删除 `Composition` 引用后 0 警告编译。门禁 `JCC12005 8→2`、`JCC12002 485→484`、`JCC12003 0`；全仓 218 项目串行重建 0 警告 0 错误、`Build-AllDomains` 四 target 全 True；`Clock.Tests` 519/519、`Guard.Hooks.Tests` 598/598；全量 60 项目 24678 通过 / 0 失败（`full-tests-final3`）。顺带把满载下过紧的 150ms 时序上限放宽到 400ms（仍低于未限速 500ms），`Infra.Utils` 复跑 561/561。
- 2026-11-23：P4 第十一块完成（`abstractions ↔ async_primitives` 环清零）。`BclBridge`、`AsyncLock`、`Transport.Contracts` 三项基础库从 `async_primitives` 域迁入 `abstractions` 域，测试/目录同步改为 `bcl_bridge_tests`、`async_lock_tests`、`transport_contracts`；`async_primitives` 只保留 `Transport.Impl` 及测试。门禁 `JCC12005 2→1`、`JCC12002 484→469`、`JCC12009 175→173`、`JCC12003 0`；全仓 218 项目串行重建 0 警告 0 错误；`BclBridge` 41/41、`AsyncLock.Tests` 546/546、`Transport.Impl.Tests` 218/218 通过（全量交由 CI）；剩余 `apps ↔ native_tui` 1 条环。
- 工程事实（债务盘点）：`dotnet build` 增量不重跑分析器，新规则的全仓存量必须按域 `-t:Rebuild` 采集；用增量全仓构建统计会把存量严重低估。
- 工程事实（全量采集的并行陷阱）：`JoinCode.slnx -t:Rebuild -m`（默认并行）会在重建时先清掉依赖项目的 `ref` 程序集，导致下游 CS0006（实测 645 个错误）；要一次采集全仓，必须 `-t:Rebuild -m:1` 串行按依赖序重建，或按域分别 Rebuild。
- 工程事实（分类口径）：判断测试项目不能只靠名字/路径启发式。`ProjectType=test` 是构建期事实（决定测试包引用与 `IsTestProject` 语义），门禁必须以它为准；否则纯测试辅助库（名字不含 `.tests`）会被算进生产图，既误报 `JCC12004`，也让生产边/入度虚高。
- 关键发现（增量构建掩盖）：`solution` target 用增量构建时，删掉被他人传递依赖的 `ProjectReference` 不会立即报错；`native_tui` / `native_gui` 独立 target 才暴露 `CS0246`。跨域引用变更后必须至少跑一次 `-t:Rebuild`。
- 工程事实（口径）：传递引用显式化会让门禁计数上升（472→478），这是把原本隐形的依赖画进图里，不是新增耦合；清账进度应按"域对集合 + 环数"看，不能只看边计数。
- 工程事实：`JCC12002` 只靠计数会在"删一条旧边、加一条新边"时漏检；必须同时冻结域对集合。
- 工程事实：全仓脚本 solution target 带 `--no-restore`；出现 `NETSDK1004` 资产缺失时先 `dotnet restore build/sln/JoinCode.slnx`，不要误判为代码问题。
- 关键发现：`ProjectContext` 读 `build_property.MSBuildProjectDirectory` 得到空串，生成的 `GeneratedMSBuildEditorConfig.editorconfig` 里只有 `build_property.ProjectDir`；改读 `ProjectDir` 后规则才在真实构建链路生效。
- 关键发现：`dotnet build` 增量不会重跑分析器，负向验收必须 `-t:Rebuild`，否则会误判"规则没生效"。分析器规则改为 Warning 后，因全仓 `TreatWarningsAsErrors` 在构建中表现为 error。
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
