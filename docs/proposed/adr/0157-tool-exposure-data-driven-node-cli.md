# ADR 0157: 去生成器 + 基础工具 YAML 注册 + 全量工具独立 CLI.exe + CLI 搜索下钻

- 状态：proposed（v2 修订，取代同编号 v1；v1 的「复用 mcp_call、不新增 exe、Profile 只做开关」方案作废）
- 日期：2026-11-15
- 关联：ADR 0149（减少 exe 数量 — **本次显式收窄适用范围**）、ADR 0137（功能域就近隔离）、DSG048（节点插件继承架构）、DSG018（工具渐进式暴露 — **本次判定为错误层**）
- 上游用户指令（2026-11-15 原文）：
  1. 我想扔掉工具的源码生成器
  2. 我认为可以通过 yml 来注册基础工具
  3. 除了基础之外的全部转为独立的 CLI.exe 工具，这样可以各自被 `-h` 帮助说明让 LLM 自行按需阅读
  4. 分班再分组，是 AI 供应商协议本身不提供，所以 mcp 使用这个做法是错误的，但是 CLI 层面则可以通过搜索功能下钻探索，此时才是正确的

---

## 1. 核心洞察（第 4 点）

**MCP / AI 供应商协议只提供扁平的 `tools` 数组，不提供分组、分级、下钻。** 项目现有的 `tool_groups` 自定义字段、提示词里教 LLM 的 `list_groups` → `map[组]` → `map[组][工具]` 三级下钻，都是在**错误的层**模拟一个协议不存在的能力。症状即证据：

| 症状 | 原始证据 |
|------|---------|
| 真实 API 直接忽略分组字段 | `modules/llm/docs/task/TASK010-ResponsesAPI支持任务.md:108`：「`tool_groups` + `tool_description_request` 是自定义协议，真实 DeepSeek API 忽略这两个字段」 |
| 占位符/延迟加载只在单一供应商实现 | `FilterDeferredTools` 仅见于 `AnthropicQueryService.cs:138` |
| 下钻能力要靠提示词"教"出来 | `ToolsSection.cs:33-40` 用 8 行系统提示词解释三级语法 |
| 补回的 schema 永久滞留上下文 | `ToolsSection.cs:36` 要求 LLM 直取 schema 后调用 |

**CLI / 文件系统原生拥有路径树与搜索**：`ls`、`find`、`grep`、`<tool> -h` 是协议无关的，LLM 用已有的 bash 工具即可按需探索，探索结果查完即弃、不占常驻上下文。因此在 CLI 层做下钻是正确的，在 MCP 层做分组是错误的。这与 harness2「差异编码为数据 + 靠目录树构造性复用」、DSH「Service/Context 不靠特性标注」同源。

---

## 2. 现状核实（原始证据）

| 事实 | 证据 |
|------|------|
| 466KB 巨型请求事故 | `McpToolDispatchAttribute.cs:18`；防复活守卫 `ToolKindDefaultGuardTests.cs:5-10` |
| 参数描述写在 C# 特性上，由生成器扫描成 schema | `McpToolParameterAttribute.cs:4-28`（`Description` / `Required` / `DefaultValue` / `EnumValues` / `WellKnown` / `Aliases`） |
| 工具生成器按**当前程序集**扫描全命名空间 | `McpToolDispatchGenerator.cs:13-41`：`CompilationProvider` + `VisitNamespaces` → `GenerateRegistrationCode`，凡跨 exe 边界即失效 |
| 生成器产物 | `GeneratedToolHandlerRegistration_*.g.cs`（`AddMcpToolDispatchSingletons` / `RegisterAllMcpToolDispatchAsync`），消费点 `ServiceRegistration.Mcp.cs:20-28` |
| 全仓规模 | 约 540 个 `[McpTool]` 方法 / 约 90 个 `[McpToolDispatch]` handler 类；另有 838 处 `[Register]` 属 DI 生成器，**不在本次移除范围** |
| 已有独立 exe 样板 | `modules/tools/rg/rg.csproj:4-13`（`OutputType=Exe` + `IsAotCompatible` + `ExcludeJccAnalyzers`，仅引 3 个 abstractions）；`modules/tools/gh/gh.csproj` 同构 |
| 已有 `-h` 自描述机制 | `CliSubCommand.cs:83,89` 的 `SelfHelp = true`（rg / gh 两个子命令自带帮助） |
| 已有 YAML 基础设施 | `NodeProfileLoader.cs:44` 使用 YamlDotNet `YamlStream`；`resources/profiles/*.yaml` 已内嵌资源化 |
| 已有工具 schema DTO | `modules/abstractions/abstractions.primitives.tools/src/ToolSchema.cs:6-50`（`ToolSchema` / `ToolSchemaProperty`） |
| 目录现状 | 16 功能域就近隔离（ADR 0137），非「一节点一目录」自包含 |

---

## 3. 决策

### 3.1 四层分离

| 层 | 内容 | 对 LLM 可见性 |
|----|------|--------------|
| **基础工具层**（4 个） | 读 / 写 / 编译 / bash，由 `resources/tools/base.yaml` 声明注册 | 以完整 schema 进入协议原生 `tools` 字段 |
| **CLI 工具层**（其余全部） | 每个工具一个独立 exe + `node.yaml` 自描述 + `-h` | 完全不进提示词，LLM 用 bash 按需 `ls` / 搜索 / `-h` |
| **发现层** | 目录树 + 关键词搜索下钻，**CLI 层实现，不进 MCP** | 按需查询，查完即弃 |
| **描述数据层** | 参数描述从 C# 特性迁到 YAML（单一数据源） | 永不注入 |

### 3.2 逐条落地

| 用户指令 | 决策 |
|---------|------|
| ① 扔掉工具源码生成器 | 移除 `McpToolDispatchGenerator` / `[McpTool]` / `[McpToolDispatch]` / `[McpToolParameter]` 链路。工具改为**自己声明参数契约**（`node.yaml` + `ICliToolNode`），不再由中央生成器按程序集扫描。**渐进式**：先切基础工具与垂直切片，验证后分批迁移，最后归档生成器项目到 `.xxx/` 并修复引用（禁止直接删除） |
| ② yml 注册基础工具 | 新增 `resources/tools/base.yaml`，用 YamlDotNet 反序列化为 `BaseToolManifest` DTO（复用 `ToolSchema` 类型），经 `BaseToolRegistry` 提供 4 个工具的完整 schema。与 `NodeProfile` 一样作为内嵌资源，AOT 兼容 |
| ③ 其余全部转独立 CLI.exe | 每个工具一个自包含目录：`node.yaml`（描述 + 参数 + 示例）+ `cli/`（独立 exe 入口）+ handler + 测试。`-h` 从 `node.yaml` 生成。LLM 通过 bash 调用，`-h` 输出即按需阅读的说明 |
| ④ CLI 搜索下钻 | 移除 MCP 的 `tool_groups` / `tool_search` 三级下钻 / 两阶段加载对工具的适用；改为 `jcc tool find <query>` / `jcc tool tree`（基于目录树 + `node.yaml` 元数据）。**不在提示词里教语法**，LLM 用 bash 自然探索 |

### 3.3 对 ADR 0149 的显式收窄

ADR 0149:18「减少 exe 数量 — 不要搞那么多 exe」在**交互宿主**语境下仍然成立（`jcc.exe` + `NativeGui.Host.exe` 保持），但**不适用于工具层**：工具 exe 不是用户交互宿主，而是能力的物理封装单元。本次决策把工具层从「宿主内进程」改为「独立可执行文件」，exe 数量显著增加。记录为对 0149 适用范围的收窄，而非整体废弃。

### 3.4 不做什么

- **不删除文件** — 生成器项目归档到 `.xxx/`，引用同步修复
- **不动 `[Register]` DI 生成器** — 838 处属服务注册，与工具 schema 是两回事
- **不新增 MCP 分组能力** — `tool_groups` 相关路径随工具迁移一并退役

---

## 4. 工程代价与缓解（如实记录）

| 风险 | 量级 | 缓解 |
|------|------|------|
| NativeAOT 独立 exe 编译时间/体积 | 540 个 exe，非线性增长 | **先垂直切片实测单 exe 成本**（编译秒数 / 体积 / 依赖数），用真实数据决定批量策略；工具 exe 只允许引用 contracts + 必要 abstractions，禁止引用整个引擎 |
| 跨工具共享服务断裂 | 现工具普遍注入 `IFileSystem` / `ITelemetryService` / `MiddlewarePipeline` / `IProfileManager` | 工具 exe 内自建最小依赖；共享逻辑下沉到契约层；禁止把引擎 DI 容器搬进工具 exe |
| 安全守卫失效 | Shell 15 中间件 / `DangerousCommandCatalog` / 路径守卫原本在宿主进程 | 独立 exe 的调用仍经 bash 工具，宿主侧守卫不变；工具 exe 自身补最小输入校验 |
| 生成器移除导致全仓编译失败 | 16 域 slnx + 4 个 Analyzer 引用 | 每批迁移后立即 `Build-AllDomains.ps1` 全仓编译；归档前先切换所有消费方 |
| 小模型不会用 CLI 探索 | 行为退化 | 系统提示词保留 3 行自然语言引导（不是语法教程）；基础 4 工具覆盖高频操作 |

---

## 5. 替代方案（考虑过但放弃）

| 方案 | 放弃原因 |
|------|---------|
| v1：复用 `mcp_call` / `mcp_schema` / `mcp_search`，不新增 exe，Profile 只做开关 | 用户否决。工具描述仍以 MCP schema 形式存在，未真正外置；且沿用了被判定为错误层的 MCP 分组 |
| 只把 `FilterDeferredTools` 推广到全部供应商 | 仍在错误的层修修补补，schema 会随 `tool_search` 往返累积 |
| 每工具一个独立 csproj 但共享宿主 exe（薄壳） | 不满足「独立 CLI.exe + 各自 `-h`」；跨 exe 时生成器仍失效，问题只解决一半 |
| 保留生成器，仅把描述移到 YAML | 生成器按程序集扫描的本质约束未变，独立 exe 仍无法注册 |

---

## 6. 实施路径（渐进式，每步编译 → 测试 → commit）

| 步骤 | 内容 | 状态 |
|------|------|------|
| 1 | 备份 tag + ADR v1 + TASK082（已完成 `211f4f379`） | 已完成 |
| 2 | **本 ADR v2 修订 + TASK082 任务重排** | 进行中 |
| 3 | 契约层：`ICliToolNode` / `BaseToolManifest` DTO / YamlDotNet Loader / `BaseToolRegistry` / CLI 搜索下钻 | 待办 |
| 4 | `resources/tools/base.yaml` 注册 4 个基础工具 + 单元测试 | 待办 |
| 5 | 垂直切片：改造 `jcc-rg.exe`（`node.yaml` + `-h` + 独立可运行），实测单 exe 编译/体积成本 | 待办 |
| 6 | 按工具域分批迁移其余工具（每批编译 + 测试 + commit） | 待办 |
| 7 | 切换主请求链路：首轮 `tools` 只读 `BaseToolRegistry`；退役 `tool_groups` / 两阶段加载 / `ToolsSection` 三级下钻 | 待办 |
| 8 | 目录节点化重组 + 归档生成器项目到 `.xxx/` + 修复引用 | 待办 |
| 9 | 全仓编译 + 全量测试 + 真实 exe 验收 + 四列验收表 + ADR 转 accepted | 待办 |

---

## 7. 验收标准

| 验收项 | 方法 |
|--------|------|
| 首次请求 `tools` 字段只含 4 个基础工具 | 真实 exe + debuglog 抓 `[WIRE]` 请求体 |
| 请求体字节数量级下降 | 与 466KB 事故基线对比 |
| 基础工具由 YAML 注册 | `jcc tool base --dump` 输出 4 条，与 `base.yaml` 一致 |
| 任意 CLI 工具独立运行 | `<tool>.exe <args>` 真实执行，不依赖会话 |
| 任意工具 `-h` 自描述 | `<tool>.exe -h` 输出参数定义，参数来自 `node.yaml` |
| 工具描述不进提示词 | 系统提示词快照 grep 无工具参数描述 |
| CLI 搜索下钻 | `jcc tool find <query>` / `jcc tool tree` 实测 |
| MCP 分组已退役 | 代码检索无 `tool_groups` 生产路径 |
| 全仓编译 + 全量测试 | `Build-AllDomains.ps1` + mainAgent 全量测试 |

---

## 8. 决策记录

| # | 问题 | 决策 | 理由 |
|---|------|------|------|
| 1 | 是否新增 exe | 是，工具层全量独立 exe；宿主仍 2 exe | 工具描述必须物理外置，`-h` 是协议无关的自描述 |
| 2 | 参数描述放哪 | `node.yaml` / `base.yaml`，单一数据源 | 去掉生成器后无编译期扫描，YAML 是唯一 SSOT |
| 3 | 发现机制在哪层 | CLI 目录树 + 搜索，不在 MCP | 供应商协议不提供分组；CLI 原生有路径与 grep |
| 4 | 生成器如何处理 | 渐进退役，归档到 `.xxx/`，不删除 | 禁止删除文件；需先切换全部消费方 |
| 5 | 批量迁移策略 | 先垂直切片实测成本，再按域分批 | 540 个 AOT exe 成本未知，禁止盲目全铺 |
