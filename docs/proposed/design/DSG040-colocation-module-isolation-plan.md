# 就近原则功能域隔离迁移计划

> 📍 **导航**: [docs/](../README.md) › [design/](README.md) | **前置**: [adr/](../adr/README.md)
> 🔗 **上游索引**: [design/README.md](README.md) — 修改本文档后须同步更新此索引

> 状态: 设计草案(待用户确认)
> 创建: 2026-10-11
> 关联 ADR: [0137](../implemented/adr/data_core_selection/0137-colocation-module-isolation.md)(proposed)
> 取代: 无(继承 [0103](../implemented/adr/architecture/0103-folder-restructure-semantic-grouping-flat.md) 扁平化成果,在其基础上叠加功能域隔离)
> 范围: 90+ csproj 重组为 16 功能域单元 + 文档就近迁移 + 依赖隔离(ProjectReference→PackageReference)

---

## 1. 背景与动机

### 1.1 用户需求

> 1. 工程摆放重新布置,采用就近原则
> 2. 一个单元是(文档-修改日志-项目代码)
> 3. 可以随时拆开一个单元去其他项目
> 4. 单独 NuGet 形式 + 单独子模块形式 + 完全隔离 + CI 缓存 + 日后可继续拆分单独项目

### 1.2 当前问题

| 问题 | 具体表现 |
|------|---------|
| 文档代码分离 | 改 `lib/structura` 代码要去 `docs/` 各处找相关文档;想把 structura 拆到别的项目,得在 docs 满世界搜 |
| 集中文档膨胀 | `docs/` 下 136 ADR + 50 DSG + 40 TASK + bugfix/fix/handoff/plan/refactor/reports = 300+ 文档,难以管理 |
| 文档分类混乱 | `docs/task` 与 `docs/tasks` 重复;根 `task/` 与 `docs/task/` 重复;fix/bugfix 重叠 |
| 依赖耦合 | 层间 `ProjectReference` 耦合,无法独立编译单个项目;`Composition` 依赖几乎所有东西 |
| 不可拆 | 当前单仓库七层结构,无法把一个功能域整块拆走到其他仓库 |
| CI 缓存粗 | 当前按层缓存,改一个小项目要重编译整层 |

### 1.3 与 ADR 0103 的关系

ADR 0103 完成了"语义分组 + 组内扁平"(消除 `src/`+`tests/` 双层)。本方案**继承**扁平化成果,在其基础上叠加:
- 功能域聚合(从七层 → 十六域)
- 文档就近(从集中 `docs/` → 各域 `docs/`)
- 依赖隔离(ProjectReference → PackageReference)

---

## 2. 功能域划分

### 2.1 划分原则

1. **高内聚**: 功能紧密相关的项目放一起
2. **低耦合**: 域间依赖最少(理想: DAG,无环)
3. **可独立**: 每个域可独立编译(依赖其他域的 NuGet 包)
4. **可复用**: 每个域有明确的对外价值,拆走后可被其他项目复用
5. **就近**: 域的文档、修改日志、代码在同一目录

### 2.2 依赖分析摘要

基于 csproj ProjectReference 提取的依赖图(详见附录 A):

```
零依赖层:  Structura, PithosDB.Core, BclBridge, Transport.Impl, Browser, Llm, 所有生成器
基础层:    Abstractions, AsyncLock, Downloader, Infrastructure, Vault, Guard, Scheduling
中间层:    Agents, Reasoning, Mcp, Prompts, McpToolDispatch, Hands, Brain, Bridge, Dream, Vision, Eyes, CodeIndex
组合层:    Composition, Slash, Pipelines  (依赖几乎所有东西)
应用层:    JoinCode(CLI), JoinCodeGui, JoinCodeTui, Sdk
```

**架构问题(迁移时需修复):**
- `Clock` → `Composition`(反向依赖,需解耦)
- `CodeIndex` → `OnnxEmbeddingExe`(跨层依赖 tool,需改为 NuGet 包引用或接口注入)
- `Composition`/`Slash` 依赖几乎全部(组装层,天然重耦合)

### 2.3 十六功能域

| # | 域名 | 含义 | 包含项目 | 对外依赖(域级) |
|---|------|------|----------|----------------|
| 1 | `generators` | 源码生成器 | gen/* 全部(13 csproj) | 无 |
| 2 | `collections` | 纯数据结构 | structura + tests | 无 |
| 3 | `persistence` | LSM 持久化 | pithosdb + tests | 无 |
| 4 | `async_primitives` | 异步原语+传输 | async_lock, bcl_bridge, transport.contracts, transport.impl + tests | generators |
| 5 | `abstractions` | 抽象接口+基础设施 | abstractions, infrastructure + tests | generators, async_primitives, persistence |
| 6 | `guard` | 安全守卫 | guard + guard.{config,hooks,security}.tests | generators, abstractions |
| 7 | `storage` | 存储+调度+下载 | vault, scheduling, clock, downloader + tests | generators, abstractions, llm(agents) |
| 8 | `plugins` | 插件系统 | plugins.contracts, plugins.infrastructure, plugins.tests | generators, abstractions |
| 9 | `mcp` | MCP 协议+分发 | mcp, mcp_tool_dispatch + tests | generators, abstractions, guard |
| 10 | `llm` | LLM 引擎 | llm/core, agents, reasoning + tests | generators, abstractions |
| 11 | `cognition` | 认知核心 | prompts, brain, hands + tests | generators, abstractions, guard, storage, plugins |
| 12 | `perception` | 感知+索引 | eyes, vision, code_index + tests | generators, abstractions, tools(onnx) |
| 13 | `services` | 服务层 | bridge, browser, dream, update, sandbox_satellite + tests | generators, abstractions |
| 14 | `composition` | 应用组合 | composition, slash, pipelines + tests | 几乎所有域(组装层) |
| 15 | `apps` | 应用入口 | cli(JoinCode), gui, tui, sdk | composition + 几乎所有域 |
| 16 | `tools` | 辅助/非交付工具 | ctor_scanner, onnx_embedding, onnx_embedding_verify, simd_vector_verify, scripts | 各异 |

### 2.4 域间依赖 DAG

```
generators ─────────────────────────────────────────────┐
                                                          │
collections     persistence                               │
                                                          │
async_primitives ──→ abstractions ──→ guard              │
                      abstractions ──→ storage            │
                      abstractions ──→ plugins            │
                      abstractions ──→ mcp                │
                      abstractions ──→ llm                │
                      abstractions ──→ cognition          │
                      abstractions ──→ perception         │
                      abstractions ──→ services           │
                                                          │
llm ──→ storage(scheduling 依赖 agents)                   │
                                                           │
cognition ──→ composition                                 │
perception ──→ composition                                 │
services ──→ composition                                   │
mcp ──→ composition                                       │
                                                           │
composition ──→ apps                                      │
tools ──→ perception(onnx_embedding)                      │
```

---

## 3. 目录结构方案

### 3.1 方案对比

| 方案 | 描述 | 优点 | 缺点 |
|------|------|------|------|
| A: 新建 `modules/` 顶层 | 废弃七层,全部按功能域放 `modules/` 下 | 最彻底隔离,可整块拆走 | 改动最大,打破 ADR 0081 七层 |
| B: 七层内按域聚合 | 保持 `lib/kit/server/...`,层内按域建子目录 | 改动小,保留七层 | 域跨层时无法物理聚合(如 cognition 跨 kit+llm) |
| C: 七层+域标签 | 保持七层物理结构,用目录名前缀标域 | 改动最小 | 没有真正隔离,无法整块拆走 |

### 3.2 推荐方案: A — `modules/` 顶层

```
D:/project/w1/
  modules/                         # 新顶层:功能域单元
    generators/
      enum_metadata.generator/     # 扁平(继承 ADR 0103)
      fsm.generator/
      ...所有 gen/* 项目...
      enum_metadata.generator.tests/
      fsm.generator.tests/
      docs/                        # 该域文档(从 docs/ 迁入)
      CHANGELOG.md                 # 该域修改日志
      README.md                    # 该域说明
      module.slnx                  # 该域解决方案(独立编译)
      module.csproj.props          # 该域公共属性

    collections/
      structura/
      structura.tests/
      docs/
      CHANGELOG.md
      README.md
      module.slnx

    persistence/
      pithosdb/
      docs/
      CHANGELOG.md
      README.md
      module.slnx

    async_primitives/
      async_lock/
      async_lock.tests/
      bcl_bridge/
      bcl_bridge.tests/
      transport.contracts/
      transport.impl/
      transport.impl.tests/
      docs/
      CHANGELOG.md
      README.md
      module.slnx

    ...其余 12 个域同结构...

  # 保留的顶层目录
  docs/                             # 全局文档(跨域 ADR、项目级 README、全局索引)
    adr/                            # 架构决策记录(全局,136 个)
    design/                         # 跨域设计文档(如本文件)
    reference/                      # 全局参考
    README.md                       # 全局文档索引
  build/                            # 构建脚本+顶层 slnx
    sln/
      JoinCode.slnx                 # 顶层解决方案(引用所有 module.slnx)
    build.ps1
    publish.ps1
  test/                             # 跨域测试(integration/e2e/mock/benchmark)
    integration/
    mock/
    benchmarks/
    aot/
  libs/                             # 第三方库(Avalonia Dock 等)

  # 保留的顶层文件
  AGENTS.md
  README.md
  LICENSE
  Directory.Build.props            # 全局公共属性
  Directory.Build.targets          # 全局公共目标
  global.json
  nuget.config
  .gitignore
```

### 3.3 单元内部结构(每个功能域)

```
modules/<domain>/
  <project_a>/                     # 源码项目(扁平,无 src/ 子层)
    ProjectA.csproj
    *.cs
    GlobalUsings.cs
  <project_a>.tests/               # 测试项目(扁平,与源码同层)
    ProjectA.Tests.csproj
    *.cs
  <project_b>/
  <project_b>.tests/
  docs/                             # 该域文档
    design/                         # 该域设计文档
    task/                           # 该域任务清单
    bugfix/                         # 该域 bug 修复记录
    handoff/                        # 该域交接文档
  CHANGELOG.md                      # 该域修改日志(按版本记录变更)
  README.md                         # 该域说明(功能、依赖、用法)
  module.slnx                       # 该域解决方案(仅含本域项目)
  module.csproj.props               # 该域公共 csproj 属性(可选)
```

### 3.4 选择方案 A 的理由

1. **完全隔离**: 每个域物理自包含,可 `git subtree`/`submodule` 整块拆走
2. **独立编译**: 每个域有 `module.slnx`,可独立 `dotnet build`
3. **NuGet 包化**: 每个域可 `dotnet pack`,对外发 NuGet 包
4. **CI 缓存**: 每个域独立缓存,改一个域只重建该域+下游
5. **七层保留为逻辑标签**: 依赖层次仍在(域间 DAG),只是物理目录不按层组织

---

## 4. 文档迁移策略

### 4.1 文档分类与去向

| 文档类型 | 当前位置 | 迁移去向 | 理由 |
|----------|----------|----------|------|
| ADR(架构决策) | `docs/adr/` (136) | **保留原位** | ADR 是全局架构决策,跨域 |
| 跨域设计 | `docs/design/DSG0xx` | **保留原位** | 跨域设计(如本文件)属全局 |
| 域级设计 | `docs/design/DSG0xx` | 迁到对应域 `docs/design/` | 单域设计就近 |
| 任务清单 | `docs/task/TASK0xx` | 迁到对应域 `docs/task/` | 单域任务就近 |
| Bug 修复 | `docs/bugfix/` | 迁到对应域 `docs/bugfix/` | 单域 bug 就近 |
| 修复记录 | `docs/fix/` | 迁到对应域 `docs/fix/` | 单域修复就近 |
| 交接文档 | `docs/handoff/` | 迁到对应域 `docs/handoff/` | 单域交接就近 |
| 重构计划 | `docs/refactor/` | 迁到对应域 `docs/refactor/` | 单域重构就近 |
| 工作计划 | `docs/plan/` | 迁到对应域 `docs/plan/` | 单域计划就近 |
| 报告 | `docs/reports/` | 迁到对应域 `docs/reports/` | 单域报告就近 |
| 参考 | `docs/reference/` | **保留原位** | 全局参考 |
| 中文文档 | `docs/zh-CN/` | 按内容迁到对应域或保留 | 视内容而定 |
| 根 `task/` | `task/` | 迁到对应域 `docs/` | 与 `docs/task` 合并去重 |

### 4.2 文档→域映射规则

由于 300+ 文档无法逐个分析,采用以下规则:

1. **按文件名关键词映射**: 文件名含 `structura`→`collections`,含 `pithos`→`persistence`,含 `guard`→`guard`,含 `mcp`→`mcp`,含 `gui`→`apps`,含 `cli`→`apps`,含 `agent`→`llm`,含 `brain`→`cognition`,含 `eye`→`perception`,含 `code_index`→`perception`,含 `bridge`→`services`,含 `browser`→`services`,含 `composition`→`composition`,含 `pipeline`→`composition`,含 `transport`→`async_primitives`,含 `async_lock`→`async_primitives`,含 `vault`→`storage`,含 `scheduling`→`storage`,含 `clock`→`storage`,含 `downloader`→`storage`,含 `plugin`→`plugins`,含 `prompt`→`cognition`,含 `hand`→`cognition`,含 `slash`→`composition`,含 `dream`→`services`,含 `vision`→`perception`,含 `update`→`services`,含 `sandbox`→`services`,含 `generator`→`generators`,含 `aot`→`generators`,含 `enum`→`generators`
2. **无法映射的保留原位**: 全局性文档(如 `DSG001-architecture-index`)保留在 `docs/`
3. **跨域文档保留原位**: 涉及多个域的文档保留在 `docs/`,在各域 README 中加链接
4. **迁移后更新链接**: 文档内引用路径需同步更新

### 4.3 CHANGELOG.md 初始化

每个域新建 `CHANGELOG.md`,从 git log 提取该域项目的提交历史:
```markdown
# CHANGELOG — <domain>

## [Unreleased]

## [历史] — 从单仓库迁移
- 从 lib/kit/server/... 迁移到 modules/<domain>/
- 完整 git 历史见主仓库 git log
```

---

## 5. 依赖隔离方案

### 5.1 ProjectReference → PackageReference

当前域间依赖是 `ProjectReference`(源码引用)。迁移后改为 `PackageReference`(NuGet 包引用):

```xml
<!-- 当前: lib/guard/Guard.csproj -->
<ProjectReference Include="..\abstractions\Abstractions.csproj" />

<!-- 迁移后: modules/guard/Guard.csproj -->
<PackageReference Include="JoinCode.Abstractions" Version="1.0.0" />
```

### 5.2 域内依赖保持 ProjectReference

同一域内的项目间依赖保持 `ProjectReference`(如 `guard` 域内 `Guard.csproj` → `Abstractions.csproj` 如果同域)。

### 5.3 NuGet 包发布流程

```
1. 域独立编译: dotnet build modules/<domain>/module.slnx
2. 域打包:     dotnet pack modules/<domain>/module.slnx -o ./nuget-feed
3. 下游引用:   dotnet add modules/<下游>/ package JoinCode.<Domain>
```

### 5.4 开发模式 vs 发布模式

| 模式 | 域间引用 | 用途 |
|------|----------|------|
| 开发(本地) | ProjectReference(源码) | 改代码即生效,无需打包 |
| CI/发布 | PackageReference(NuGet) | 隔离编译,缓存友好 |

通过 `Directory.Build.props` 的条件切换:
```xml
<PropertyGroup Condition="'$(UsePackedComponents)' == 'true'">
  <!-- PackageReference 模式 -->
</PropertyGroup>
<PropertyGroup Condition="'$(UsePackedComponents)' != 'true'">
  <!-- ProjectReference 模式 -->
</PropertyGroup>
```

> 已有基建: `UsePackedComponents` 双模式已在 ADR 0134 中落地,112 处引用。

### 5.5 需解耦的架构问题

| 问题 | 当前 | 解耦方案 |
|------|------|----------|
| Clock → Composition(反向) | `Clock` 依赖 `Composition` | 提取 Clock 需要的接口到 `abstractions`,Clock 依赖接口而非 Composition |
| CodeIndex → OnnxEmbeddingExe(跨层) | `CodeIndex` 依赖 `tool/onnx_embedding` | OnnxEmbedding 打 NuGet 包,CodeIndex 改 PackageReference;或提取接口注入 |
| Composition 依赖几乎所有 | 天然(组装层) | 保留,Composition 就是"组装所有域"的层,依赖多是合理的 |

---

## 6. 分阶段迁移步骤

### 阶段 0: 准备(不动代码)

1. 写 ADR 0137(proposed → accepted after 阶段1)
2. 写本设计文档(DSG040)✅
3. git 备份当前状态
4. 确认功能域划分(用户确认)

### 阶段 1: 文档就近(不动代码,只移文档)

1. 在 `modules/<domain>/docs/` 下建目录结构
2. 按映射规则迁移 `docs/` 下的域级文档到各域
3. 更新文档内引用路径
4. 更新 `(已删除)` 索引
5. 合并 `docs/task` + `docs/tasks` + 根 `task/`(去重)
6. 编译验证(文档迁移不影响编译)
7. git commit

### 阶段 2: 代码迁移(建 modules/ 顶层,移代码)

按依赖顺序从底到顶,每次一个域:
1. `generators`(零依赖,最先)
2. `collections`, `persistence`(零依赖)
3. `async_primitives`(依赖 generators)
4. `abstractions`(依赖 generators + async_primitives + persistence)
5. ...按 DAG 顺序...
6. `composition`(依赖几乎所有,靠后)
7. `apps`(顶层,最后)

每域步骤:
1. `mkdir modules/<domain>/`
2. `git mv` 该域所有项目到 `modules/<domain>/`
3. 建 `module.slnx`(仅含本域项目)
4. 建 `CHANGELOG.md` + `README.md`
5. 更新 `Directory.Build.props` 路径
6. 编译验证 `dotnet build modules/<domain>/module.slnx`
7. git commit

### 阶段 3: 依赖隔离(ProjectReference → PackageReference)

1. 每域 `dotnet pack` 发本地 NuGet feed
2. 下游域改 `ProjectReference` → `PackageReference`
3. 验证 `UsePackedComponents=true` 模式编译通过
4. 解耦 Clock→Composition、CodeIndex→OnnxEmbedding
5. git commit

### 阶段 4: CI 适配

1. 更新 CI workflow:按域 matrix 编译+测试
2. 每域独立缓存 key
3. 增量检测:git diff 检测改动域,只触发该域+下游
4. git commit

### 阶段 5: 子模块化准备(可选,未来)

1. 每域可独立 `git subtree split` 成独立仓库
2. 主仓库用 `git submodule` 引用
3. 或用 NuGet 包引用(不拆仓库)

---

## 7. 风险与回退

| 风险 | 缓解 |
|------|------|
| 大规模 git mv 导致历史断裂 | `git mv` 保留历史;每步 commit 可回退 |
| 路径引用断裂(GlobalUsings/cref) | 每步编译验证;用脚本批量修复路径 |
| ProjectReference→PackageReference 版本地狱 | 统一版本号策略;开发模式用 ProjectReference |
| CI workflow 大改 | 阶段4 单独处理,前3阶段 CI 不受影响 |
| 文档链接失效 | 阶段1 后用脚本检测断链 |

**回退策略**: 每阶段独立 commit,任何阶段失败可 `git reset` 到上一阶段。阶段1(纯文档)和阶段2(代码迁移)互不依赖,可独立回退。

---

## 附录 A: 完整依赖图

```
零依赖: Structura, PithosDB.Core, BclBridge, Transport.Impl, Browser, Llm, 所有生成器

Abstractions → {generators}
AsyncLock → {generators}
BclBridge → {}
Clock → {Abstractions, Infrastructure, Composition(!), Vault, Scheduling, generators}
Downloader → {generators}
Guard → {Abstractions, Infrastructure, BclBridge, generators}
Infrastructure → {BclBridge, PithosDB.Core, generators}
Plugins.Contracts → {generators}
Plugins.Infrastructure → {generators}
Scheduling → {Abstractions, Infrastructure, Agents, generators}
Structura → {}
Transport.Contracts → {generators}
Transport.Impl → {}
Vault → {Abstractions, Infrastructure, generators}

Agents → {Abstractions, Infrastructure, generators}
Brain → {Abstractions, Infrastructure, Prompts, generators}
Bridge → {Abstractions, generators}
Browser → {}
CodeIndex → {OnnxEmbeddingExe}
Dream → {generators}
Eyes → {Abstractions, CodeIndex, generators}
Hands → {Abstractions, Infrastructure, Plugins.Infrastructure, Guard, Scheduling, generators}
Llm → {generators}
Mcp → {generators}
McpToolDispatch → {Abstractions, Infrastructure, Guard, generators}
Pipelines → {Composition, Dream}
Prompts → {Abstractions, Infrastructure, generators}
Reasoning → {Abstractions, Infrastructure, Agents, generators}
Slash → {Infrastructure, Bridge, Mcp, Brain, Guard, Hands, Eyes, Vision, Vault, Scheduling, McpToolDispatch, Agents, Transport.Impl, Reasoning, generators}
Vision → {Abstractions, generators}

Composition → {Infrastructure, Bridge, Mcp, Brain, Guard, Hands, Eyes, Vision, Vault, Scheduling, McpToolDispatch, Agents, Transport.Impl, Reasoning, Slash, generators}

JoinCode → {Composition, Clock, Dream, Browser, Pipelines, PithosDB.Core, generators}
JoinCodeGui → {JoinCode, Scheduling, BclBridge, Avalonia}
JoinCodeTui → {JoinCode, generators}
Sdk → {generators}
```

---

## 附录 B: 文档→域映射表(主要)

| 文档 | 域 |
|------|-----|
| DSG004-flatten-restructure | 全局(保留) |
| DSG006~012 ComputerUse/Desktop | apps |
| DSG013 UnifiedResilience | 全局(保留) |
| DSG014 TransportFallback | async_primitives |
| DSG015 ContextCompaction | cognition |
| DSG016 LLM-OutputLoop | llm |
| DSG017 GUI重构 | apps |
| DSG018 工具渐进式暴露 | cognition |
| DSG019 多模态隐喻 | perception |
| DSG020 命令拦截架构 | guard |
| DSG021 rg-engine | tools |
| DSG022 RangeDownloader | storage |
| DSG023 worktree-merge | 全局(保留) |
| DSG024 bridgemain-split | services |
| DSG025 fix-config-loading | abstractions |
| DSG026 window-shake | apps |
| DSG027 cli_arg_io | apps |
| DSG028 subagent-stall | llm |
| DSG029 inmemory-index-store | persistence |
| DSG030 vector-code-index | perception |
| DSG031 gui-topbar | apps |
| DSG032 vscode-style-gui | apps |
| DSG033 actor-mailbox-leak | async_primitives |
| DSG034 actor-refactor | async_primitives |
| DSG035 dock-layout | apps |
| DSG036 vecidx5 | perception |
| DSG037 interaction-tool-name | cognition |
| DSG038 deadlock-elimination | async_primitives |
| DSG039 startup-param-architecture | apps |

(完整映射在阶段1 执行时逐文件确认)
