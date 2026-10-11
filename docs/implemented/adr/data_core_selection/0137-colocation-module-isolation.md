# 0137. 就近原则功能域隔离架构

> 📍 **导航**: [docs/](../README.md) › [adr/](README.md)
> 🔗 **上游索引**: [adr/README.md](README.md) — 修改本文档后须同步更新此索引
> 🔗 **关联**: [0103](0103-folder-restructure-semantic-grouping-flat.md)（扁平化重组）、[0081](0081-seven-layer-build-strategy.md)（七层 slnx）、[0134](0134-layered-ci-nuget-decouple-incremental.md)（分层 CI NuGet 解耦）
> 📋 **执行计划**: [DSG040](../proposed/design/DSG040-colocation-module-isolation-plan.md)

- 状态：accepted
- 日期：2026-10-11
- 决策者：项目架构组
- 取代：[0001](0001-seven-layer-slnx-isolation.md)（七层 slnx 隔离）、[0081](0081-seven-layer-build-strategy.md)（七层编译策略）

## 背景

当前项目采用七层架构（gen/lib/kit/server/llm/app/test），文档集中在 `docs/` 下。用户提出就近原则需求：

1. 工程摆放重新布置，采用就近原则
2. 一个单元是（文档-修改日志-项目代码）
3. 可以随时拆开一个单元去其他项目
4. 单独 NuGet 形式 + 单独子模块形式 + 完全隔离 + CI 缓存 + 日后可继续拆分单独项目

### 当前问题

- **文档代码分离**：改 `lib/structura` 代码要去 `docs/` 各处找相关文档
- **集中文档膨胀**：`docs/` 下 300+ 文档，难以管理
- **依赖耦合**：层间 `ProjectReference` 耦合，无法独立编译单个功能域
- **不可拆**：单仓库七层结构，无法把一个功能域整块拆走到其他仓库
- **CI 缓存粗**：按层缓存，改一个小项目要重编译整层

## 决策

采用**功能域隔离架构**：将 90+ csproj 重组为 16 个功能域单元，每个单元自包含（代码+测试+文档+修改日志），物理目录按域组织在 `modules/` 下。

### 功能域划分（16 域）

| 域 | 含义 | 包含项目 |
|----|------|----------|
| generators | 源码生成器 | gen/* 全部 |
| collections | 纯数据结构 | structura |
| persistence | LSM 持久化 | pithosdb |
| async_primitives | 异步原语+传输 | async_lock, bcl_bridge, transport |
| abstractions | 抽象接口+基础设施 | abstractions, infrastructure |
| guard | 安全守卫 | guard |
| storage | 存储+调度+下载 | vault, scheduling, clock, downloader |
| plugins | 插件系统 | plugins |
| mcp | MCP 协议+分发 | mcp, mcp_tool_dispatch |
| llm | LLM 引擎 | llm/core, agents, reasoning |
| cognition | 认知核心 | prompts, brain, hands |
| perception | 感知+索引 | eyes, vision, code_index |
| services | 服务层 | bridge, browser, dream, update, sandbox |
| composition | 应用组合 | composition, slash, pipelines |
| apps | 应用入口 | cli, gui, tui, sdk |
| tools | 辅助工具 | ctor_scanner, onnx_embedding, ... |

### 目录结构

```
modules/<domain>/
  <project>/           # 源码(扁平,继承 ADR 0103)
  <project>.tests/     # 测试(扁平)
  docs/                # 该域文档
  CHANGELOG.md         # 该域修改日志
  README.md            # 该域说明
  module.slnx          # 该域独立解决方案
```

### 依赖隔离

- 域间依赖：`ProjectReference` → `PackageReference`（NuGet 包）
- 域内依赖：保持 `ProjectReference`
- 双模式：`UsePackedComponents` 切换（已有基建，ADR 0134）

## 考虑的替代方案

### 方案 B: 七层内按域聚合

保持 `lib/kit/server/...` 物理结构，层内按域建子目录。

**放弃理由**：功能域跨层时无法物理聚合（如 cognition 跨 kit+llm），无法真正隔离，无法整块拆走。

### 方案 C: 七层+域标签

保持七层物理结构，用目录名前缀标域。

**放弃理由**：没有真正隔离，无法整块拆走，不满足用户"完全隔离"需求。

## 后果

### 正面

- 每个域可独立编译、独立打包、独立成仓库
- 文档就近，改代码时文档触手可及
- CI 按域缓存，改一个域只重建该域+下游
- 可随时 `git subtree`/`submodule` 拆走一个域

### 负面

- 大规模目录迁移（90+ 项目 git mv）
- 七层物理结构被打破（逻辑层次保留为 DAG）
- 需解耦 Clock→Composition、CodeIndex→OnnxEmbedding 等反向/跨层依赖
- CI workflow 需适配

### 缓解

- 渐进式迁移：阶段1(文档) → 阶段2(代码) → 阶段3(依赖) → 阶段4(CI)
- 每步编译验证 + git commit，可随时回退
- `UsePackedComponents` 双模式已有基建

## 验证

- [x] 阶段1: 文档就近后编译通过 (2026-10-11, 187 文档迁移到 modules/<domain>/docs/)
- [x] 阶段2: 每域代码迁移后 `module.slnx` 独立编译通过 (2026-10-11, 90+ 项目迁移到 modules/)
- [x] 阶段3: `UsePackedComponents=true` 全量编译通过 (2026-10-11, ProjectReference + PackageReference 双模式验证)
- [x] 阶段4: CI 按域 matrix 编译+测试通过 (2026-10-11, ci-unit-tests.yml 改为 14 域 matrix)
