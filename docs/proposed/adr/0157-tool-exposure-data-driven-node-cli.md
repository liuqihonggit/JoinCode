# ADR 0157: 工具暴露策略数据化与节点 CLI 化 — 首提示词瘦身

- 状态：proposed
- 日期：2026-11-15
- 关联：DSG048（节点插件继承架构）、DSG051（command/prompt 纳入 NodeRegistry）、DSG018（工具渐进式暴露）、ADR 0149（减少 exe 数量）、ADR 0137（功能域就近隔离）
- 上游：用户诉求「540+ MCP 工具的参数描述注入首次提示词，首提示词巨大；拆成 CLI，只留下 读/写/编译/bash」

---

## 1. 背景与问题

### 1.1 用户真实诉求

不是"架构不优雅"，而是**首次请求被工具 schema 撑爆**。手段是把工具拆成 CLI，目标是让工具描述退出 LLM 常驻上下文，首轮只保留 读 / 写 / 编译 / bash 四个工具。

### 1.2 现状核实（原始证据）

| 事实 | 证据 |
|------|------|
| 466KB 巨型请求事故已修复 | `modules/abstractions/abstractions.primitives.attributes/src/McpToolDispatchAttribute.cs:18`：默认 `Kind` 由 System 改 Mcp，避免 506 工具全量 schema 466KB 请求被 10s 超时撞断 |
| 有防复活回归守卫 | `modules/mcp/mcp_tool_dispatch.tests/handlers/ToolKindDefaultGuardTests.cs:5-10` 锁定三契约 |
| `ToolKind` 三分类 + 两阶段加载 | `modules/abstractions/abstractions.primitives.utils/src/constants/tool_meta/ToolKind.cs:7-18`（System / Mcp / OnError / Slash） |
| 系统提示词教 LLM 三级下钻 | `modules/cognition/prompts/sections/tools/ToolsSection.cs:33-40`（`list_groups` → `map[组]` → `map[组][工具]`） |
| 核心白名单与用户想留的 4 个高度重合 | 全仓显式 `Kind = ToolKind.System` 仅 7 处：File / Search / CodeExecution / Agent / ToolSearch / ModelSearch / Interaction；`ToolKindDefaultGuardTests.cs:8` 注明对齐 Claude Code 核心集 Read/Write/Edit/Grep/Glob/Bash/Task |
| **真实 API 路径上两阶段加载失效** | `modules/llm/docs/task/TASK010-ResponsesAPI支持任务.md:108`：「`tool_groups` + `tool_description_request` 是自定义协议，真实 DeepSeek API 忽略这两个字段」；`FilterDeferredTools` 仅实现于 `modules/llm/core/Adapters/LLM/QueryServices/Anthropic/AnthropicQueryService.cs:138` |
| 每次 `tool_search` 补回的 schema 永久滞留上下文 | `ToolsSection.cs:36` 要求 LLM 直取 schema 后调用，多轮累积 |
| **CLI 基础设施已全部存在** | `modules/abstractions/abstractions.primitives.utils/src/constants/cli_ansi/CliSubCommand.cs:41-78`：`mcp_call` / `mcp_list` / `mcp_schema` / `mcp_search` / `mcp_serve` / `slash_call` / `slash_list` / `slash_schema` |
| 运行期节点化已验收 | DSG048 §6 P1-P8 全部完成：`NodeRegistry` / `NodeProfileResolver`（省略即继承）/ `DataDrivenPipelineComposition` / 4 个 Profile YAML / `--dump-nodes` / `--dump-profile` |
| **`tool-dispatch` 消费点零消费** | `docs/proposed/design/DSG048-P6-implementation-plan.md:21-28`：YAML 定义了消费点，生产代码从未调用 `GetNodesForConsumer("tool-dispatch")`，INodeConsumer 零实现 |
| 目录仍非节点粒度 | ADR 0137 的 16 功能域就近隔离，无「一节点一目录」自包含结构 |

### 1.3 问题的准确定位

两阶段加载解决的是「**首次**请求不带全量 schema」，但没有解决「工具描述始终是 LLM 上下文里的对象」：要么靠 `tool_groups`（真实 API 忽略）、要么靠 `tool_search` 往返补 schema（补回即常驻）。要让描述彻底退出上下文，必须把它变成**可按需查询、查完即弃的外部数据**——这正是 CLI 自描述 + 节点检索能提供的。

---

## 2. 决策

### 2.1 注入策略从代码特性迁移到 Profile 数据（SSOT）

新增 `ToolKind.Cli`。**不再逐个修改 500+ 工具的特性标注**，而是把「哪些工具对 LLM 可见」编码进 `resources/profiles/*.yaml`：

```yaml
# base.yaml — 默认所有节点走 CLI（省略即继承）
tool-exposure:
  default: cli
  system:                    # 唯一进入首轮 tools 字段的 4 个
    - file-read
    - file-write
    - code-execution
    - bash
```

子 profile 用 `excludes` / `overrides` 覆写差异，未提及的从 base 继承。策略由 `NodeProfileResolver` 合并后交给生成器消费，与 DSG048 §4.2「消费点驱动、节点本身无固有 Kind」一致。

### 2.2 工具实现零改动，只改暴露层

复用已存在的 `mcp_call` / `mcp_schema` / `mcp_search`，新增统一节点入口 `jcc tool <node-id> [args]`：

| 能力 | 复用 | 说明 |
|------|------|------|
| 每个工具独立运行 | `mcp_call` 路由 | `jcc tool <id> --json '{...}'` 脱离会话单独调用 |
| 参数自描述 | `mcp_schema` | `jcc tool <id> --help` 输出参数定义，描述永不进提示词 |
| 按需发现 | `mcp_search` / `tool_search` 引擎 | 关键词 + 分组下钻，查完即弃 |
| 节点元数据 | `NodeRegistry` | 接通 `tool-dispatch` 消费点，消除零消费 |

### 2.3 不新增 exe 文件

遵守 ADR 0149「减少 exe 数量」与 NativeAOT 硬约束。每个节点是**逻辑上独立可运行的 CLI 单元**（独立入口 + 独立参数契约 + 独立描述），而非独立可执行文件。物理进程仍收敛在 `jcc.exe`。

### 2.4 首提示词瘦身

`ToolsSection` 的两阶段加载/三级下钻冗长说明，替换为 3 行 CLI 用法引导。首次请求 `tools` 字段只含 4 个 System 工具。

### 2.5 目录节点化重组（第二阶段）

16 功能域 → 节点粒度自包含目录（handler + cli + profile 声明 + 测试），描述数据由 Profile 继承链管理。渐进式：每次移动一个模块后立即编译验证并提交。

---

## 3. 理由

1. **不复写 500+ 工具实现** — 工具是 `[McpTool]` 方法，CLI 化只是换一个调用入口；重写会制造两套实现，违反「消除两套实现」原则
2. **差异编码为数据** — DSH `cordis.patch.yml` + harness2 `SurfaceSpec` 的共同原则；策略调整只需改 YAML，无需重新编译 500 个标注
3. **省略即继承** — base 定默认、子 profile 只写差异，正是「节点皆可继承」的数据形态
4. **复用既有组件** — AGENTS.md「万物皆 node，万物皆插件，禁止重复造轮子」；`mcp_call`/`mcp_schema`/`mcp_search`/`NodeRegistry` 已是现成节点设施
5. **接通已知断点** — `tool-dispatch` 零消费是 DSG048 自己登记的缺口，本次顺手闭合
6. **AOT 兼容** — 编译期源码生成器扫描 + 运行期数据配置，不引入反射加载

---

## 4. 替代方案（考虑过但放弃）

| 方案 | 放弃原因 |
|------|---------|
| 每个工具一个独立 exe 文件 | NativeAOT 下编译时间/体积/跨进程协议/源码生成器按程序集扫描机制全部重做；直接推翻 ADR 0149 |
| 只把 Anthropic 的 `FilterDeferredTools` 推广到 OpenAI/DeepSeek/Responses | 改动最小，但 schema 仍会在多轮 `tool_search` 后累积，且拿不到「工具可单独运行与分发」的收益 |
| 保持两阶段加载，仅修 `tool_groups` 兼容性 | `TASK010:113` 已记录「保留现状，不修改代码」的设计决策；真实 API 忽略该字段，问题未解 |
| 先做真实请求实测定位再定范围 | 用户明确选择直接全拆；但 exe 实测仍作为验收阶段的强制环节（ADR 0080） |

---

## 5. 代价与风险

| 风险 | 缓解 |
|------|------|
| LLM 需学会用 CLI 调工具，小模型可能退化 | 提示词保留 3 行用法引导；`tool_search` 兜底；exe 实测验证 |
| `ToolKindDefaultGuardTests` 等守卫测试因新 Kind 失败 | 同步更新测试，锁定「首轮 tools ≤ 4」新契约，防 466KB 复活 |
| 移除 `tool_groups` 后 MCP 远程工具发现路径变化 | 保留 `mcp_search` 检索入口，远程工具仍可发现 |
| Profile 驱动后策略散落两处（特性 + YAML） | 生成器以 YAML 为唯一数据源，特性 `Kind` 降级为编译期提示，冲突时 YAML 优先并诊断告警 |
| 目录重组触碰大量文件 | 渐进式迁移，每次一个模块 + 编译 + commit；禁止一次性大规模重构 |

---

## 6. 实施路径（渐进式，每步编译 → 测试 → commit）

| 步骤 | 内容 | 状态 |
|------|------|------|
| 1 | ADR 0157 + TASK082 任务清单 + 备份 tag | 进行中 |
| 2 | `ToolKind.Cli` + Profile 驱动注入策略 + 守卫测试更新 | 待办 |
| 3 | `jcc tool <node-id>` 统一入口 + `--help` 自描述 + 按需检索 | 待办 |
| 4 | `ToolsSection` 瘦身 + 首请求体积回归 | 待办 |
| 5 | 接通 `tool-dispatch` 消费点 + 节点粒度目录重组 | 待办 |
| 6 | 全仓编译 + 全量测试 + 真实 exe 验收 + 验收表 + ADR 转 accepted | 待办 |

---

## 7. 验收标准

| 验收项 | 方法 |
|--------|------|
| 首次请求 `tools` 字段只含 4 个工具 | 真实 exe + debuglog 抓 `[WIRE]` 请求体 |
| 请求体字节数量级下降 | 与 466KB 事故基线对比 |
| 任意工具可独立运行 | `jcc tool <id> --json '{...}'` 真实执行 |
| 任意工具可查参数 | `jcc tool <id> --help` 输出 schema |
| 工具描述不进提示词 | 系统提示词快照 grep 无工具参数描述 |
| `tool-dispatch` 非零消费 | `jcc --dump-nodes` 显示消费点节点 |
| 全仓编译 + 全量测试 | `Build-AllDomains.ps1` + mainAgent 全量测试 |
