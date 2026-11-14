# TASK082: 工具节点 CLI 化与目录节点化重组 — 剩余任务清单

> 📍 状态: proposed ｜ 创建: 2026-11-15 ｜ 关联: [ADR 0157](../adr/0157-tool-exposure-data-driven-node-cli.md)、[DSG048](../design/DSG048-node-plugin-inheritance-architecture.md)、[DSG051](../design/DSG051-command-prompt-node-registry-integration.md)
> 用户诉求原文:「每个 mcp 工具需要注入提示词，参数描述提示词太多，首次提示词巨大，所以我想拆成 CLI，只留下 读/写/编译/bash」

---

## 1. 目标与验收基线

| 目标 | 基线（实测证据） | 目标值 |
|------|-----------------|--------|
| 首轮 `tools` 字段工具数 | 506 工具全量 schema = 466KB 巨型请求（`McpToolDispatchAttribute.cs:18`） | ≤ 4（读/写/编译/bash） |
| 工具描述存放位置 | LLM 上下文 / `tool_groups`（真实 API 忽略） | CLI `--help` + Profile YAML，查完即弃 |
| 每个工具可独立运行 | 需会话 + LLM 调度 | `jcc tool <id> --json '{...}'` 脱离会话 |
| `tool-dispatch` 消费点 | 零消费（`DSG048-P6:21-28`） | NodeRegistry 实际驱动 |
| exe 数量 | 6（ADR 0149 要求减少） | 不增加 |

---

## 2. 剩余任务（按执行顺序）

| # | 任务 | 关键文件 | 编译 | 测试 | commit |
|---|------|---------|------|------|--------|
| 1 | 备份 tag + ADR 0157 + 本任务清单 | `docs/proposed/adr/0157-*.md`、本文件 | — | — | ✅ 待提交 |
| 2 | 新增 `ToolKind.Cli`；注入策略改由 Profile YAML 驱动；base 定默认、4 个 System 例外、子 profile 省略即继承 | `ToolKind.cs`、`McpToolDispatchGenerator.cs`、`resources/profiles/*.yaml`、`NodeProfileResolver.cs` | ⬜ | ⬜ | ⬜ |
| 3 | 新增 `tool` 子命令 + 统一节点入口；每个工具 `--help` 自描述；按需检索复用 `mcp_search` | `CliSubCommand.cs`、`FlatSubCommandRouter.cs`、`modules/apps/cli/core/commands/core/` | ⬜ | ⬜ | ⬜ |
| 4 | `ToolsSection` 瘦身：两阶段/三级下钻说明 → 3 行 CLI 用法引导；补「首轮 tools ≤ 4」回归守卫 | `ToolsSection.cs`、`ToolKindDefaultGuardTests.cs`、新增首请求体积测试 | ⬜ | ⬜ | ⬜ |
| 5 | 接通 `tool-dispatch` 消费点（消除零消费）；16 功能域 → 节点粒度自包含目录 | `node_registry/consumers/ToolDispatchConsumer.cs`、`DataDrivenPipelineComposition.cs`、`modules/*/` | ⬜ | ⬜ | ⬜ |
| 6 | 全仓编译 + 全量测试 + 真实 exe 验收 + 四列验收表 + ADR 转 accepted | `Build-AllDomains.ps1`、ADR 0080 手动 exe 流程 | ⬜ | ⬜ | ⬜ |

---

## 3. 文档需求落地四列验收表（强制）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `ToolKind.Cli` + Profile 驱动注入策略 | `McpToolDispatchGenerator` 读取合并后 Profile 决定工具可见性 | ⬜ | ⬜ |
| `resources/profiles/base.yaml` `tool-exposure` 段 | `NodeProfileResolver` 省略即继承合并 | ⬜ | ⬜ |
| `jcc tool <id>` 统一入口 | `FlatSubCommandRouter` → `mcp_call` 路由 | ⬜ | ⬜ |
| `jcc tool <id> --help` 自描述 | `mcp_schema` 输出到 stdout | ⬜ | ⬜ |
| 按需检索 | `mcp_search` / `tool_search` 引擎 | ✅ 已有 | ⬜ 待 exe 验收 |
| `ToolsSection` 3 行 CLI 引导 | 系统提示词组装 | ⬜ | ⬜ |
| 首轮 tools ≤ 4 回归守卫 | 新增测试锁定契约，防 466KB 复活 | ⬜ | ⬜ |
| `tool-dispatch` NodeRegistry 接线 | `ToolDispatchConsumer` 生产调用 | ⬜ | ⬜ |
| 节点粒度自包含目录 | 构建脚本 + module.slnx | ⬜ | ⬜ |

> `已验收` 列必须走 [ADR 0080](../../implemented/adr/engineering_ci_test/0080-manual-exe-testing-guide.md) 真实 exe 验收（非 mock）。

---

## 4. 约束与纪律

- **禁止一次性大规模重构** — 每次移动一个模块，移动后立即编译验证，成功后提交
- **禁止删除文件** — 归档统一 `Move-Item` 到项目根 `.xxx/`，格式 `.xxx/{文件名}.{时间戳}.del`
- **禁止新增 exe 文件** — ADR 0149 + NativeAOT
- **跨域契约变更必须全仓编译** — `Build-AllDomains.ps1`（16 域 slnx + host.tests + native_gui）
- **subAgent 只编译 + 快速冒烟，全量测试由 mainAgent 执行**
- **测试路径跨平台** — 禁止 Windows 盘符字面量（JCC1018）
- **改动 csproj 需 Debug 编译通过方可提交**

---

## 5. 开工前状态

- 工作区：`git status --short` 为空（干净）
- 备份 tag：`backup/pre-node-cli-20261115072248`
- 关联提交：`e7f8c8dff`（DSG050 + DSG051 阶段3）为当前 HEAD

---

<!-- 🤖 Auto Decision: 2026-11-15 -->
<!-- 决策: 注入策略从代码特性迁移到 Profile YAML 数据驱动，不逐个修改 500+ 工具标注 -->
<!-- 原因: 差异编码为数据（DSH cordis.patch.yml + harness2 SurfaceSpec），复用已验收的 NodeProfileResolver 省略即继承；避免制造两套实现 -->
<!-- 替代方案: 逐个给 500+ handler 加 Kind=Cli 标注（改动量大、易漏、无法按 Profile 切换）-->
<!-- 验证: 待步骤 2 编译 + 测试 -->
