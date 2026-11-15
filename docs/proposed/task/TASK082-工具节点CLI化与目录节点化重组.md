# TASK082: 去生成器 + YAML 基础工具 + 全量独立 CLI.exe + CLI 搜索下钻

> 📍 状态: proposed（v3 重排）｜ 创建: 2026-11-15 ｜ 关联: [ADR 0157 v3](../adr/0157-tool-exposure-data-driven-node-cli.md)、[DSG048](../design/DSG048-node-plugin-inheritance-architecture.md)、ADR 0149
> 2026-11-15 后续指令（v3，取代 v2 的「540 个独立 exe + 描述搬到新 YAML」）: ① 大部分 mcp 特性不需要改 ② 按每个分组单独一个 CLI.exe，bash 可调用 ③ 复用现有 mcp 说明制造 -h
> 更早四点指令: ① 扔掉工具源码生成器 ② yml 注册基础工具 ③ 其余转独立 CLI.exe 各自 -h ④ MCP 分组是错误层、CLI 搜索下钻才正确

---

## 1. 目标与验收基线

| 目标 | 基线（实测证据） | 目标值 |
|------|-----------------|--------|
| 首轮 `tools` 字段工具数 | 506 工具全量 schema = 466KB 巨型请求（`McpToolDispatchAttribute.cs:18`） | 4（读/写/编译/bash） |
| 基础工具注册方式 | `[McpToolDispatch]` 特性 + 源码生成器按程序集扫描 | `resources/tools/base.yaml` |
| 工具描述存放位置 | C# 特性 / LLM 上下文 / `tool_groups`（真实 API 忽略） | **特性原封不动**，编译期只读扫描渲染 `-h` |
| 其余工具形态 | 宿主进程内 handler | **12 个功能大类，每组一个独立 exe**（546 工具），组内工具为子命令 |
| 发现机制 | MCP `tool_search` 三级 `map[]` 下钻（错误层） | CLI 目录树 + 搜索（正确层） |
| MCP 分组 | `tool_groups` 自定义字段 | 退役 |
| 交互宿主 exe 数 | 2（ADR 0149） | 2（不变）；工具层 exe 数另行增加 |

> **2026-11-16 修正（步骤 8 落地时发现）**：`base.yaml` 原登记的第 3 个基础工具 `code_execution` 在 546 个内核工具中**并不存在**（凭空命名）。步骤 8 接上后，LLM 会拿到一个永远「工具不存在」的槽位。编译/构建本就由 `bash` 覆盖，故替换为真实高频的 `file_edit`（局部查找替换，与 `file_write` 的整体覆写互补）。首轮 `tools` 仍是 4 个，但这 4 个现在都是真能调用的。

---

## 2. 剩余任务（按执行顺序）

| # | 任务 | 关键文件 | 编译 | 测试 | commit |
|---|------|---------|------|------|--------|
| 1 | 备份 tag + ADR v1 + 任务清单 | `backup/pre-node-cli-20261115072248`、`211f4f379` | — | — | ✅ |
| 2 | ADR 0157 v2/v3 修订 + 本清单重排 | `docs/proposed/adr/0157-*.md`、本文件 | — | — | ✅ `55578f676` |
| 3 | CLI 节点契约层 + `base.yaml` 注册 4 个基础工具 | `src/cli_nodes/`、`resources/tools/base.yaml` | ✅ | ✅ 13/13 | ✅ `da319bfcf` |
| 4 | 垂直切片 `jcc-rg` + CLI 搜索下钻 `tool find/tree/help/base` | `modules/tools/rg/`、`CliToolCommand.cs` | ✅ | ✅ 真实 exe | ✅ `da319bfcf` |
| 5 | 依赖墙收敛 + AOT 成本实测 | `modules/tools/cli_nodes/cli_node.props`、`sample/` | ✅ | ✅ 13/13 | ✅ `6a1a3154b` |
| 6 | 只读扫描器从现有特性生成 51 组索引与 `-h` 文本 + `jcc-tool-git.exe` 完整样板 | `scripts/migrate_mcp_tools_to_cli_nodes.py`、`groups/git/` | ✅ | ✅ 真实 exe | ✅ `a15c38b8a` |
| 7a | `group_map.yaml` 把 51 碎组归并为 12 大类，重切出各组 exe（`-h` + 转发宿主）；旧碎组归档 `.xxx/` | `resources/tools/group_map.yaml`、`--emit-exe`、`groups/*/Program.cs` | ✅ 12 组全编译 | ✅ 真机 | ✅ `f2ca1154b`+`741b1f4e7` |
| 7b | 逐组做实本地实现（混合模式：本地优先 + 转发兜底，HANDCRAFTED 保护手工文件） | `groups/git/`、`groups/file/private/FileCommands.cs`、`groups/shell/`、`groups/tools/` | ✅ 4 组 | ✅ 真机 | ✅ |
| 7c | **进程隔离执行器**：`jcc tool call <组> <工具> [参数]` 由 jcc 启动分组 exe、透传参数、回传 stdout/stderr/退出码；不初始化引擎 DI | `CliToolCommand.cs`（`RunCallAsync` / `ResolveGroupExe`） | ✅ | ✅ 真实 exe 七连验 + 反查/别名/超时补充验证 | ✅ |
| 7d | **斜杠命令独立 exe**：`jcc-tool-slash.exe`（99 命令 / 15 分类）+ `jcc tool call slash <命令>`；`-h` 来自 `[ChatCommand]`/`[ChatCommandArg]` | `scripts/generate_slash_cli_node.py`、`groups/slash/` | ✅ | ✅ 真实 exe（含 Git Bash 命令名归一化） | ✅ |
| 8 | 切换主请求链路：首轮 tools 只读 `BaseToolRegistry`；退役 `tool_groups` / 两阶段加载 / `ToolsSection` 三级下钻 | `BuildBaseTools`（3 个 QueryService）、`resources/tools/base.yaml` | ✅ | ✅ 9 个单测（锁定「非基础工具不进 tools」）+ 契约层 7/7 | ✅ |
| 9 | 全部消费方切换后归档生成器项目到 `.xxx/` + 修复引用 + 目录节点化重组 | `modules/generators/mcp_tool_dispatch.generator/`、16 域 slnx | ⬜ | ⬜ | ⬜ |
| 10 | 全仓编译 + 全量测试 + 真实 exe 验收 + 四列验收表 + ADR 转 accepted | `Build-AllDomains.ps1`、ADR 0080 | ✅ 部分 | ⬜ | ⬜ |

---

## 3. 文档需求落地四列验收表（强制）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `resources/tools/base.yaml` 4 个基础工具 | `jcc tool base` 输出 4 条 | ✅ | ✅ 真实 exe |
| `BaseToolManifest` DTO + YamlDotNet Loader | 内嵌资源加载，AOT 兼容 | ✅ | ✅ 13 单测全绿 |
| `ICliToolNode` / `CliToolDescriptor` 契约 | 各独立工具 exe 共用 | ✅ | ✅ 编译 0 警告 |
| `node.yaml` 参数描述单一数据源 | `jcc-rg.exe -h` 自描述输出 | ✅ | ✅ 真实 exe |
| `jcc-rg.exe` 垂直切片 | 独立运行 + 真实搜索 | ✅ | ✅ 真实 exe |
| `jcc tool find <q>` / `tree` / `help` / `base` | CLI 目录树下钻 | ✅ | ✅ 真实 exe |
| 进程隔离执行 `jcc tool call <组> <工具>` | jcc 启动分组 exe 真实执行 + 回传结果 | ✅ | ✅ 真实 exe（file_read / git_status，退出码 0） |
| 首轮 tools ≤ 4 回归守卫 | `BaseToolRegistryTests` 锁定契约 | ✅ | ✅ 单测 |
| 首轮 `tools` 字段改读 `BaseToolRegistry` | 主请求链路切换 | ✅ | ✅ 9 个单测 + `jcc tool base` 真机 |
| MCP `tool_groups` 退役 | 生产代码检索无残留（三个 provider 不再写 `request.ToolGroups`） | ✅ | ✅ 编译 0 警告 |
| 生成器项目归档 | `.xxx/` + 引用修复 | ⬜ | ⬜ 步骤 8 |
| 节点粒度自包含目录 | module.slnx + 构建脚本 | ⬜ | ⬜ 步骤 8 |

> `已验收` 列必须走 [ADR 0080](../../implemented/adr/engineering_ci_test/0080-manual-exe-testing-guide.md) 真实 exe 验收（非 mock）。

---

## 4. 垂直切片实测成本基线（2026-11-15）

Debug framework-dependent 构建，真实产物统计（非估算）：

| 指标 | 实测值 |
|------|--------|
| `jcc-rg.exe` apphost | 159 KB |
| `jcc-rg.dll` | 106 KB |
| 依赖 DLL 数 | **81** |
| 输出目录总大小 | **16 MB** |
| `rg.csproj` 首次编译 | 1m33s（含依赖树重建） |
| `JoinCode.csproj` 首次编译 | 6m27s（含整个引擎重建） |
| 主 CLI 增量编译 | 51s |
| 契约层单元测试 | 13/13 通过，290ms |
| 全仓编译 `Build-AllDomains.ps1`（首次） | 6m7s，全部通过（JoinCode.slnx） |
| 全仓编译复核（分组拆分后） | 8m32s，全部通过 |
| 分组扫描器产出 | 5248 个 .cs / 546 工具 / **51 分组** / 102 个文件（help.txt + node.yaml） |
| `jcc-tool-git.exe` | 10 个 git 工具真实可用，`-h` 与 `<工具> -h` 均来自现有特性 |
| 陈旧分组目录 | 幂等移到 `.xxx/` 归档（未删除） |

**收敛方案（已实现）**：`modules/tools/cli_nodes/cli_node.props` 共享配置 + `modules/tools/cli_nodes/sample/` 最小样板。工具 exe 只引契约层、关闭全仓分析器与 Structura 全局引用、AOT 改为发布阶段显式开关。

| 指标 | 基线（引 Infrastructure） | 收敛后（clean publish） | 变化 |
|------|--------------------------|------------------------|------|
| 依赖 DLL 数 | 81 | **5** | −94% |
| 输出目录体积 | 16 MB | **653 KB** | −96% |
| 构建依赖项目数 | 80+ | **2** | −97% |
| 首次编译 | 1m33s | 9.8s | −89% |
| 增量编译 | — | **1.94s** | 540 个串行约 17 分钟 |

根因三处叠加：`Infrastructure.csproj:17-95`（20+ ProjectReference + 5 包 + 2 生成器）、`Directory.Build.props:148-152`（全局 Structura）、`Directory.Build.props:64`（Release 全局 AOT）。

**AOT 单文件体积本环境不可测**：链接阶段 `LNK1181: advapi32.lib` 缺失 + `reg.exe` 被安全策略拦截；未重试未绕过，待具备完整 Windows SDK 的 CI 补测。

**批量迁移硬约束**：所有工具 exe 必须 Import `cli_node.props`，禁止新增 `Infrastructure` / 引擎 / DI / 生成器引用。此前「按 540 个线性外推 8.6 GB」的估算在收敛后不再成立（约 540 × 653 KB ≈ 340 MB 框架依赖产物）。

---

## 5. 约束与纪律

- **禁止一次性大规模重构** — 每次移动一个模块，移动后立即编译验证，成功后提交
- **禁止删除文件** — 归档统一 `Move-Item` 到项目根 `.xxx/`，格式 `.xxx/{文件名}.{时间戳}.del`；移走后修复 XML `<see cref>` 与 `GlobalUsings.cs` 引用
- **工具 exe 禁止引用整个引擎** — 只允许 `contracts` + 必要 abstractions，避免 540 个 exe 各自拖入 DI 容器
- **跨域契约变更必须全仓编译** — `Build-AllDomains.ps1`（16 域 slnx + host.tests + native_gui）
- **subAgent 只编译 + 快速冒烟，全量测试由 mainAgent 执行**
- **测试路径跨平台** — 禁止 Windows 盘符字面量（JCC1018），用 `Path.Combine(Path.GetTempPath(), ...)`
- **JSON 处理用 DTO + JsonContext 双向转换**，禁止手写 JSON 拼接
- **改动 csproj 需 Debug 编译通过方可提交**

---

## 6. 开工前状态

- 工作区：`git status --short` 为空（干净）
- 备份 tag：`backup/pre-node-cli-20261115072248`
- 关联提交：`e7f8c8dff`（DSG050 + DSG051 阶段3）→ `211f4f379`（ADR v1 + 本清单 v1）

---

<!-- 🤖 Auto Decision: 2026-11-15 v2 -->
<!-- 决策: 参数描述从 C# 特性迁到 YAML；基础 4 工具由 base.yaml 注册进协议原生 tools 字段；其余工具全部独立 exe + -h；发现机制放 CLI 层 -->
<!-- 原因: 用户指出 MCP/AI 供应商协议不提供分组能力，tool_groups 与三级 map[] 下钻属于在错误层模拟；CLI 目录树 + 搜索是原生能力，且探索结果查完即弃不占常驻上下文 -->
<!-- 替代方案: v1 复用 mcp_call/mcp_schema/mcp_search 且不新增 exe — 被用户否决，工具描述仍以 MCP schema 形式存在，未真正外置 -->
<!-- 风险: 540 个 NativeAOT 独立 exe 编译时间/体积非线性增长 — 先垂直切片实测单 exe 成本再决定批量策略 -->
<!-- 验证: 待步骤 3-5 编译 + 测试 + 真实 exe 实测 -->
