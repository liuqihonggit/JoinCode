# TASK082: 去生成器 + YAML 基础工具 + 全量独立 CLI.exe + CLI 搜索下钻

> 📍 状态: **completed**（2026-11-17 验收通过，PR #682）｜ 创建: 2026-11-15 ｜ 关联: [ADR 0157 v3](../adr/cli_jcc/0157-tool-exposure-data-driven-node-cli.md)、[DSG048](../../proposed/design/DSG048-node-plugin-inheritance-architecture.md)、ADR 0149
> 2026-11-15 后续指令（v3，取代 v2 的「540 个独立 exe + 描述搬到新 YAML」）: ① 大部分 mcp 特性不需要改 ② 按每个分组单独一个 CLI.exe，bash 可调用 ③ 复用现有 mcp 说明制造 -h
> 更早四点指令: ① 扔掉工具源码生成器 ② yml 注册基础工具 ③ 其余转独立 CLI.exe 各自 -h ④ MCP 分组是错误层、CLI 搜索下钻才正确

---

## 1. 目标与验收基线

| 目标 | 基线（实测证据） | 目标值 |
|------|-----------------|--------|
| 首轮 `tools` 字段工具数 | 506 工具全量 schema = 466KB 巨型请求（`McpToolDispatchAttribute.cs:18`） | 4（读/写/编译/bash） |
| 基础工具注册方式 | `[McpToolDispatch]` 特性 + 源码生成器按程序集扫描 | `resources/tools/base.yaml` |
| 工具描述存放位置 | C# 特性 / LLM 上下文 / `tool_groups`（真实 API 忽略） | **特性原封不动**，编译期只读扫描渲染 `-h` |
| 其余工具形态 | 宿主进程内 handler | **12 个功能大类，每组一个独立 exe**（640 工具 = 541 MCP 工具 + 99 斜杠命令），组内工具为子命令 |
| 发现机制 | MCP `tool_search` 三级 `map[]` 下钻（错误层） | CLI 目录树 + 搜索（正确层） |
| MCP 分组 | `tool_groups` 自定义字段 | 退役 |
| 交互宿主 exe 数 | 2（ADR 0149） | 2（不变）；工具层 exe 数另行增加 |

> **2026-11-16 修正（步骤 8 落地时发现）**：`base.yaml` 原登记的第 3 个基础工具 `code_execution` 在 546 个内核工具中**并不存在**（凭空命名）。步骤 8 接上后，LLM 会拿到一个永远「工具不存在」的槽位。编译/构建本就由 `bash` 覆盖，故替换为真实高频的 `file_edit`（局部查找替换，与 `file_write` 的整体覆写互补）。首轮 `tools` 仍是 4 个，但这 4 个现在都是真能调用的。

> **口径说明（三个数字别混）**：`640` = 13 组 `node.yaml` 的 `tool_count` 之和（12 个 MCP 分组 541 + 斜杠命令 99），是当前唯一有效的总数口径；`541` = 补收 2 个真实工具后的 MCP 工具数；`546` = 扫描器**原始命中条数**（同一工具名多处登记会重复计入，实测多 7 条）。本文旧行里出现的 `546` 均指扫描原始量，不是总数。

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
| 7b2 | **续做 4 组**：`search`（glob + grep 族，rg 优先/内置扫描兜底）、`code`（10 个 notebook_*）、`web`（download_file / web_to_markdown）、`system`（sleep / sleep_until / get_process_info）→ 8 组共 52/546 本地实现 | `groups/*/private/*.cs` | ✅ 13 组 0 警告 | ✅ 真机（含续传 MD5、nbformat 4.5 校验、两通道 grep 对比） | ✅ |
| 7b3 | **入口统一**：13 组共用契约层 `CliNodeRunner` + `CliNodeDefinition`，各组 `Program.cs` 退化为声明；顺带支持短选项别名、未知工具近似名建议 | `src/cli_nodes/CliNodeRunner.cs`、`groups/*/Program.cs` | ✅ | ✅ 契约层 18/18 | ✅ |
| 7b4 | **`-h` 分档 + slash 收敛**：≤20 个工具给全文，超过只给分组索引 + `-h <关键字>` 检索（github 70 KB→2.3 KB）；slash 从手写 127 行入口收进 `CliNodeRunner`（新增 `HostVerb` / `NameNormalizer` 声明项） | `CliNodeRunner.cs`、`groups/slash/Program.cs` | ✅ | ✅ 契约层 28/28 | ✅ |
| 7b5 | **github 172 个改走 delegate**：`jcc-gh --call-tool` 直调入口 + `group_map.yaml` 的 `delegates` 段（单一数据源）；装配时把 `jcc-gh` 及依赖放 `tools/` 根共享一份（20 MB 不按组复制） | `GhSubCommand.cs`、`CliNodeDefinition.DelegateExecutable`、`Pack-ToolNodes.ps1` | ✅ 172/172 | ✅ 真机（`jcc tool call github gh_auth_status` 退出 0） | ✅ |
| 7b6 | **补做剩余可做项**：`file_apply_patch`（含逐行诊断 / BOM / CRLF 保真）、agent 组 `worktree_find_git` / `worktree_list_all`；修掉「`---` 开头的多行 patch 被当选项」的解析 bug | `groups/file/private/FilePatchCommands.cs`、`groups/agent/private/AgentCommands.cs` | ✅ 227/638 | ✅ 真机 | ✅ |
| 7c | **进程隔离执行器**：`jcc tool call <组> <工具> [参数]` 由 jcc 启动分组 exe、透传参数、回传 stdout/stderr/退出码；不初始化引擎 DI | `CliToolCommand.cs`（`RunCallAsync` / `ResolveGroupExe`） | ✅ | ✅ 真实 exe 七连验 + 反查/别名/超时补充验证 | ✅ |
| 7d | **斜杠命令独立 exe**：`jcc-tool-slash.exe`（99 命令 / 15 分类）+ `jcc tool call slash <命令>`；`-h` 来自 `[ChatCommand]`/`[ChatCommandArg]` | `scripts/generate_slash_cli_node.py`、`groups/slash/` | ✅ | ✅ 真实 exe（含 Git Bash 命令名归一化） | ✅ |
| 8 | 切换主请求链路：首轮 tools 只读 `BaseToolRegistry`；退役 `tool_groups` / 两阶段加载 / `ToolsSection` 三级下钻 | `BuildBaseTools`（3 个 QueryService）、`resources/tools/base.yaml` | ✅ | ✅ 9 个单测（锁定「非基础工具不进 tools」）+ 契约层 7/7 | ✅ |
| 9 | ~~全部消费方切换后归档生成器项目~~ → **改为长期保留**；目录节点化（`tool find/tree/help/base`）已在步骤 4/7d 落地 | `modules/generators/mcp_tool_dispatch.generator/`（保留）、`CliToolCommand.cs` | — | — | ✅ 目标改写（第十七轮证据） |

> **2026-11-16 阻断说明（步骤 9 前提不成立）**：`McpToolDispatch` 生成器产出的进程内 handler 注册表**正是转发兜底的依赖**。实测无 Hub 时 `jcc mcp_call` 直接报「Hub 服务不可达」，`JCC_ALLOW_DIRECT_FALLBACK=1` 下 `jcc mcp_list` 返回 `total_count: 0`。因此该生成器在「转发路径退役」之前不能归档；步骤 9 拆成两步：先逐组做实到不再需要转发，再归档生成器。
| 10 | 全仓编译 + 全量测试 + 真实 exe 验收 + 四列验收表 + ADR 转 accepted | `Build-AllDomains.ps1`、ADR 0157 | ✅ CI 绿（PR #682） | ✅ CI 绿（PR #682） | ✅ ADR 转 accepted |

> **2026-11-17 第十七轮补记（步骤 9 目标需改写）**：步骤 9 的前提是「逐组做实到不再需要转发」，
> 即 host 数量能降到 0。第十七轮验伪后 host 250 已是**闭合边界**（全部有代码证据，不是没做完），
> 转发路径因此**不会退役**。推论：`McpToolDispatch` 生成器产出的进程内 handler 注册表是**持久组件**，
> 不是过渡产物 —— 步骤 9 的「归档生成器」应改写为「长期保留 + 只读扫描器与它保持同一份特性来源」，
> 否则下一轮还会有人重开这个"未完成任务"。

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
| `ToolsSection` 三级下钻文案退役 | 提示词只推荐 `base.yaml` 真实声明的工具 + CLI 四步通道 | ✅ | ✅ 4 单测（不含 Glob/Grep/list_groups/tool_search） |
| 生成器项目归档 | `.xxx/` + 引用修复 | ⬜ | ⬜ 步骤 8 |
| 节点粒度自包含目录 | `Pack-ToolNodes.ps1` 装配 `tools/<组>/`（exe+deps+node.yaml+help.txt）；jcc 经 `JCC_CLI_TOOLS_DIR` 或自身 `tools/` 直接消费 | ✅ | ✅ 真机（13 组 / 9045 KB / 23.6s 装配） |

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

### 2026-11-16 第三轮补记（host 链路 + 目录 id）

| # | 任务 | 关键文件 | 编译 | 测试 | commit |
|---|------|---------|------|------|--------|
| 7b7 | **修 host 工具全不可达**：`ToolNameFilter` 只注册本次工具 + 元命令直连兜底 + `SkipProviderConfig`（LoadConfig 1282→约 200ms，单次 host 调用 2.5s→1.4s）；`requiredTool` 命中编译期注册表即跳过插件等待 | `CommandLineOptions.cs`、`EngineApplicationBuilder.cs`、`McpCommand.cs` | ✅ | ✅ 真机 host 六组抽样 | 本轮 |
| 7b8 | **目录 id 对齐运行时名**：生成器按 `[EnumValue]` 解析，修掉 33 个假 id（含 base.yaml 三条 → 首轮 tools 只剩 bash）；file 组本地键与提示词/测试同步 | `migrate_mcp_tools_to_cli_nodes.py`、`groups/*/node.yaml`、`resources/tools/base.yaml`、`ToolsSection.cs` | ✅ 13 组 | ✅ 声明对账 + 真机读写闭环 | 本轮 |
| 7b9 | gh `-q` for api + jq 写错不再静默回退整份 JSON（退出 2 + 原因）；git 分支探测 stderr 不再污染工具输出 | `GhCommandResolver.cs`、`SimpleJqEvaluator.cs`、`SessionIdFactory.cs` | ✅ | ✅ gh.tests 787/787 | 本轮 |

> 步骤 9（归档生成器）前提仍不成立，但**理由变了**：`mcp_tool_dispatch.generator` 现在还得提供
> `RegisterAllMcpToolDispatchAsync` 给 `mcp_call` 的进程内注册用；缺了它，host 工具照样查不到。
~~另有一处能力边界：`mcp_list` 全量只能注册 368 个工具，余下 171 个不可达~~ **（此判断已被证伪）**：
`mcp_list` 的 368 是「非 gh 的真实工具数」，先前所谓 171 个洞是**假 id 造成的统计假象**——
目录写 `code_index_*`，注册表叫 `search_*`，两边对不上被误读成缺工具。按 `[EnumValue]` 修名后，
目录 541 个 MCP 工具中 172 个走 delegate（jcc-gh），其余 369 个除 `test_tool`（无 `[McpTool]` 的本地诊断工具）
外**全部在注册表里**。能力边界只存在于「能否在本 exe 内零依赖实现」（228/640 隔离），
不存在于「能否被调用」。

### 2026-11-16 第四轮补记（架构归属：Agent 功能留 jcc 内）

| # | 任务 | 关键文件 | 编译 | 测试 | commit |
|---|------|---------|------|------|--------|
| 7b10 | **纠正隔离框架**：按用户心智模型，Agent 功能（config/memory/permission/session/slash）留 jcc 内，`mode: host` 直接在当前进程执行 mcp_call，不再起分组 exe + jcc 两层；外部工具（gh/rg 等）才是独立 exe | `CliToolCommand.cs`、`CliToolSearch.cs`、`CliToolHelp.cs`、`CliToolNodeTests.cs` | ✅ | ✅ 契约层 33/33 + 真机三路径 | 本轮 |

> 分界从「隔离 vs 转发（35% 没做完）」改为「独立 exe 工具（228）vs Agent 功能（412）」。
> 数字不变，语义变了：后者是正确归属而非未完成的隔离。总数仍 640。

### 2026-11-16 第五轮进行中：media 组可外部调用工具独立 exe

用户要求：「要继续独立成 exe，不然我的调用隔离岂不是打破了吗？并且做完隔离之后要测试」。
media 组 60 个工具当前全为 `host`（Program.cs 纯转发器，无 private/）。目标：甄别其中纯 Windows API / BCL
可零依赖落地的工具（鼠标/键盘/窗口/进程/截图），做成独立 exe 本地实现；依赖引擎共享状态的（录制/宏/观察/视觉）
继续留 jcc 内。做完必须真机测试。

| # | 任务 | 状态 |
|---|------|------|
| 7b11 | 甄别 60 工具引擎依赖，产出可独立候选清单 | 进行中 |
| 7b12 | private/ 本地 handler + Program.cs HANDCRAFTED + node.yaml/help.txt 同步（红测试先行） | 待办 |
| 7b13 | 编译 + 部署 + 真机测试 + 对账 + commit | 待办 |

#### 第五轮完成记录（media 下沉）

| # | 任务 | 关键文件 | 编译 | 测试 | commit |
|---|------|---------|------|------|--------|
| 7b11 | 甄别 60 工具依赖：23+ 纯 P/Invoke/BCL 可下沉；vision LLM / 场景撤销栈 / voice-vcr 单例 / SkiaSharp 原生库必须留 jcc 内 | （分析，无代码） | — | — | 本轮 |
| 7b12 | `private/MediaNative.cs`（user32/gdi32 P/Invoke 独立声明，x64 INPUT=40B）+ `private/MediaCommands.cs`（20 个本地 handler）+ Program.cs HANDCRAFTED；生成器反推 mode/local_tool_count | `groups/media/private/*`、`Program.cs`、`node.yaml`、`help.txt` | ✅ 0 警告 | ✅ 见下 | 本轮 |
| 7b13 | 部署 + 真机测试 + 对账 | — | ✅ | ✅ | 本轮 |

**下沉的 20 个**：进程 3（list/kill/start_process）+ 输入注入 8（mouse_click/move/drag, key_press, type_text, right_click_menu, drag_with_hover, multi_click）+ 窗口 4（list/focus/move/close_window）+ 感知覆盖 5（wait_for_idle, show_desktop_overlay, list_macros, measure_length, measure_ratio）。

**留 jcc 内的 40 个**：vision LLM 9（image_*, desktop_detect, detect_ui_elements, find_element, temporal_aggregate, optimize_steps...）、跨调用状态 6（场景 look/zoom/status/click + undo/history）、宏录制 4、voice 4、vcr 3、SkiaSharp 截图/四叉树 9、失配的 measure_depth 等。

**真机验证**（独立 exe，不经 jcc）：measure_length 3-4-5→5.00px、measure_ratio 1920x1080→16:9、list_processes/list_windows 返回真实系统数据、mouse_move 光标移动成功、错误路径退出码 2 带 Rust 风格提示。**完整链路**（jcc tool call media）：measure_length→10.00px、list_windows 真实数据、tool tree 显示「独立 exe 实现 20/60」。契约层 33/33、声明对账通过（248/640，38%）。依赖墙未破：exe 只引契约层 + 自带最小 P/Invoke，不引引擎。

### 2026-11-16 第六轮进行中：SkiaSharp 工具下沉可行性评估（只评估不改码）

用户要求评估 media 组剩余约 9 个 SkiaSharp 依赖工具（screenshot / measure_depth / quadtree_build/paint/render/zoom/neighbor/to_screen_rects / screen_indicate / temporal_stable_contour / look_at_cursor）
是否值得下沉独立 exe。核心问题：引入 SkiaSharp 原生库的体积/依赖墙代价，是否小于「留在 jcc 内」的收益。
产出：依赖清单 + 体积实测 + 纯 BCL 替代路径 + 明确结论。不改动生产代码。

#### 第六轮完成记录：SkiaSharp 工具下沉评估（只评估，未改生产代码）

**体积实测**（决定性的数字）：
- 当前 media 独立 exe：`jcc-tool-media.dll` 70KB / apphost 159KB，整个输出目录 **873KB / 11 文件**
- `libSkiaSharp.dll` 单个 **12MB**（+ 516KB 托管 `SkiaSharp.dll`），即引入后体积 **涨约 15 倍**
- 依赖墙：`cli_node.props` 只允许引契约层；SkiaSharp 是原生库，违反「工具 exe 只引 contracts」硬约束

**按真实源码把 9 个 SkiaSharp 工具再切两类**（关键发现：不是铁板一块）：

| 类别 | 工具 | SkiaSharp 用到什么 | 结论 |
|---|---|---|---|
| **A 纯计算（4）** | quadtree_neighbor / quadtree_paint / quadtree_to_screen_rects / quadtree_build | `quadtree_build` 仅 1 处 `SKBitmap.Decode` 取宽高；其余 3 个完全不碰图像（neighbor 只做网格邻居计算） | **可下沉**，用仓库现成 `PngHeaderParser.ParseDimensions`（纯 BCL 读 PNG IHDR 16-23 字节）替换那一行；需移植 QuadtreeEncoder 149 行纯 C# + QuadtreeModels |
| **B 真渲染（5）** | quadtree_render / screen_indicate / quadtree_zoom / measure_depth / screenshot / look_at_cursor | `SKSurface`/`SKPaint` 画格子、`SKBitmap.GetPixel` 逐像素分析、`SKImage.Encode` 编 PNG | **不下沉**：功能上能离线，但必须带 12MB 原生库；为一个截图工具把 exe 从 873KB 撑到 13MB 不划算，留 jcc 内复用已有 SkiaSharp |

**另一个发现**：`show_desktop_pulse`（透明窗口脉冲动画，纯 user32 RegisterClassEx/CreateWindowEx + GDI，无 SkiaSharp 无状态）本轮甄别时被漏掉，是第 21 个可下沉工具，与已下沉的 show_desktop_overlay 同类。

**结论**：SkiaSharp 那批里 **4 个（A 类）值得下沉**——代价只是移植 149 行纯 C# 算法 + 换一行尺寸读取，不引原生库；**5 个（B 类）不该下沉**——12MB 原生库的体积税远超收益。「能不能独立」的答案是能，「该不该独立」的答案是分情况——这正是依赖墙存在的意义：它逼我们区分能力与代价。

**后续（未执行）**：若采纳，下一步是下沉 A 类 4 个 + 补 show_desktop_pulse（共 +5，独立 exe 248→253），B 类 5 个永久留 jcc 内并在 node.yaml 注明 SkiaSharp 硬边界理由。

### 2026-11-16 第七轮进行中：下沉 A 类 4 个 quadtree 纯计算 + show_desktop_pulse

用户采纳评估结论。目标：独立 exe 248→253。quadtree_neighbor/paint/to_screen_rects/build 移植 QuadtreeEncoder 纯 C# 算法（149 行）+ QuadtreeModels，用 PngHeaderParser 换掉 build 唯一的 SKBitmap.Decode；show_desktop_pulse 移植 PulseNativeMethods + DesktopPulseOverlay（纯 user32+GDI）。B 类 5 个渲染工具保持 host。做完真机验证。

#### 第七轮完成记录（四叉树纯计算下沉）

**做了**：quadtree_build / quadtree_paint / quadtree_to_screen_rects / quadtree_neighbor 4 个下沉独立 exe。
`private/QuadtreeCodec.cs` 移植 QuadtreeEncoder 149 行纯 C# 网格算法（剔除 [Register] DI 与 SkiaSharp），
`quadtree_build` 的 SKBitmap.Decode 用纯 BCL PngDimensions（读 PNG IHDR 16-23 字节）替换。
JSON 用 System.Text.Json 源生成上下文（RectJsonContext，DTO 双向转换，ADR 0133）+ JsonPropertyName 保持小写 key 与引擎一致。
独立 exe 248→252（media 20→24）。

**show_desktop_pulse 暂缓**（评估说可做，实际未做，如实记录）：它是纯 user32+GDI，但实现是
DesktopPulseOverlay（184 行）+ PulseNativeMethods（203 行）的透明分层窗口 + 后台消息循环 + 跨线程 WM_CLOSE，
属于 AGENTS.md 明确警告的 timing/flaky 高风险区。为单个动画工具引入跨线程消息循环不划算，
本轮先不做；若后续要做，须按 flaky 规范做 16 次重试 + 确定性拆分。

**边界 bug 修复**：quadtree_build 非法 base64 原抛未捕获 FormatException（堆栈 + exit 127），
已改为 Rust 风格参数错误（exit 2 + 原因 + 正确写法）——测试抓到的真实缺陷。

**验证**：独立 exe 实跑 quadtree_build(8x8 PNG depth=1→4格)/neighbor(L0.0 E→L0.1)/paint(alpha=0.50)/
to_screen_rects(4 矩形小写 JSON key) 全对；错误路径 exit=2 无堆栈；jcc 全链路 tool call + tool tree 显示 24/60；
B 类 5 个（screenshot/render/zoom/measure_depth 等）仍 host；契约层 33/33；0 警告 0 错误。

### 2026-11-16 第八轮进行中：攻坚 show_desktop_pulse（flaky 高风险工具下沉）

用户要求处理第七轮暂缓的 show_desktop_pulse。它是透明分层窗口 + 后台消息循环 + 跨线程 WM_CLOSE 的动画工具。
按 AGENTS.md flaky 规范：**确定性验证 + 时序验证拆分**——把半径插值/颜色解析/生命周期状态等纯逻辑
拆成 internal static 方法做确定性测试；窗口消息循环单独隔离；重试 16 次验证时序稳定性。目标独立 exe 252→253。

#### 第八轮完成记录（show_desktop_pulse 攻坚，flaky 工具下沉）

**做了**：show_desktop_pulse（透明分层窗口脉冲动画）下沉独立 exe，独立 exe 252→253（media 24→25）。

**flaky 根因与对策**（AGENTS.md 时序 bug 规范）：
- 引擎原版用 `Task.Run(消息循环) + await Task.Delay(duration) + 外部 PostMessage(WM_CLOSE)`，
  竞态在于 Close() 时 `_hwnd` 可能还没赋值（duration 极短/取消），PostMessage 发不出而 GetMessage 无退路→线程永久卡死。
- 对策：**主线程直接跑消息循环**，WM_TIMER 到期自动 `WM_CLOSE→WM_DESTROY→PostQuitMessage`，循环自己终止。
  没有后台线程、没有跨线程 PostMessage，从根上消除竞态。
- 确定性拆分：`PulseLogic.ComputeRadius/Validate/Geometry/ParseColor` 是 internal static 纯函数，与窗口解耦。

**测试抓到的两个真实缺陷**：
1. `FillRect` 误标 gdi32.dll → EntryPointNotFoundException（它其实在 user32.dll）。修正后动画正常。
2. （第七轮）非法 base64 抛未捕获 FormatException → 已修。

**验证（确定性 + 时序两层）**：
- 半径公式边界：t=0→max(120)、t=0.5→min(30)、t=1→max、超界钳制，Python 复算 C# 同公式全 OK。
- 参数校验错误路径（min>=max / duration=0）exit=2 带 Rust 风格提示。
- **时序稳定性 16 次重复**：duration=100ms × 16，全部 exit=0 无卡死（0 失败/16）。
- 短动画真跑 exit=0 耗时 400~600ms；jcc 全链路 tool call + tool tree 显示 25/60，3 次重复全 exit=0。
- 契约层 33/33、声明对账全局 253/640（39%）、编译 0 警告 0 错误。

### 2026-11-16 第九轮进行中：再凿 media 剩余 35 个 host 工具

用户要求用「看似必须留宿主其实能下沉」的思路复核剩余 host 工具。方法：逐个读引擎实现，
挑战之前的 HOST-ONLY 判定——重点看三类误判可能：①依赖可替换为纯 BCL；②"跨调用状态"其实可文件化；
③LLM 类里有本地算法可剥离。先列清单，逐个判定，能零依赖/低依赖下沉的做掉，做完真机验证。

#### 第九轮完成记录：再凿 35 个 host —— 克制结论（只评估，几乎未下沉）

**Explore 深挖 + 主代理逐条复核，得到三个反直觉发现**：
1. `screenshot`/`look_at_cursor` 的 Skia 只做 `SKImage.Encode`（底层 GDI BitBlt 已拿裸 BGRA）——理论上可用纯 BCL PNG writer 替换，不必带 12MB 原生库。
2. `measure_depth`/`temporal_stable_contour` 是纯 `GetPixel` 数值计算，所在类虽注入 IQueryService 但方法体内一次没用——不是 LLM 类。
3. `IUndoStack.Push` / `IMacroRecorder.RecordOperation` 在 desktop 生产代码里无调用方（注：`EffectScope._undo.Push` 是插件系统的另一个栈，非 desktop）——undo/录制工具当前恒返回"空"。

**主代理没有照单全收，逐条挑战后做了克制决策**——因为"能下沉"≠"该下沉"，判断标准是**会不会制造第二份真相源**：

| 判定 | 工具 | 理由 |
|---|---|---|
| **不下沉（伪状态降级有分叉风险）** | undo_last_action / get_operation_history / start/stop_recording / start_observation | 当前恒空是事实，但一旦宿主接上 Push/RecordOperation 写入方，exe 里的固定结果就与宿主分叉。为 +5 个隔离率埋下数据不一致的雷，违背 ADR 0157「第二份真相源」铁律 |
| **不下沉（静态数据快照分叉）** | desktop_scene_menu | 数据来自 `[Scenario]` 源码生成器生成的静态注册表；exe 内硬编码快照后，宿主改特性就不同步。违背「硬编码变委托/唯一数据源」架构规范 |
| **不下沉（真 Skia 矢量渲染）** | quadtree_render / screen_indicate / desktop_look | `SKSurface+SKPaint+CreateDash+IsAntialias` 抗锯齿虚线，GDI 重写有像素级差异；12MB 换渲染质量，此场景合理 |
| **不下沉（硬依赖）** | LLM 9 + voice 4 + vcr 3 | vision LLM / Actor 单例含 API Key / 全局 HTTP DelegatingHandler 中间件，契约层无法提供，无解 |
| **唯一真有价值的候选** | desktop_scene_status | 纯读 `~/.jcc/scenarios/{id}.json` + JSON 拼装，只读不写无写冲突，DTO 协议清楚，下沉零分叉 |

**结论**：这轮没有为了数字强行下沉。35 个里 34 个有真实的分叉风险或硬依赖，只有 desktop_scene_status 是干净候选。
隔离率的边界在这里不是技术问题，而是**架构诚实问题**——把有状态/有分叉风险的工具硬塞进 exe，数字好看但系统变坏。

**实际做了**：desktop_scene_status（+1，253→254）下沉。它只读 `~/.jcc/scenarios/{sceneId}.json`
（宿主 desktop_look/zoom 写），零写冲突；DTO PascalCase 与宿主 DesktopSceneStateJsonContext 一致，
输出 raw string 逐字照抄宿主。用真实场景文件对比：`jcc tool call`（独立 exe）与 `jcc mcp_call`（宿主）
逐字段语义一致 —— 零分叉的硬证据。

**踩到的坑（重要）**：加完 handler 忘记重跑生成器 → node.yaml 的 mode 仍是 host、local_tool_count 没涨，
`jcc tool call` 按 node.yaml 路由到宿主，参数格式报错「未知选项 --sceneId」。exe 本身 `--list-local` 已有该工具，
但主链路路由表没同步。**铁律：加/删本地 handler 后必须重跑 migrate_mcp_tools_to_cli_nodes.py**，
否则 exe 与 node.yaml 的 mode 漂移，调用链静默走错路。

**其余 34 个保持 host 是正确归属**，判据已记录供后续复核。

### 2026-11-16 第十轮完成记录：media 60/60 全量下沉（目标对齐：jcc 轻）

**用户明确目标纠正**：此前我以「exe 零依赖 / 分叉风险」为由只下沉 26/60，用户指出真正目标是
**减轻 jcc 负担——工具搬进 exe、exe 只用 BCL 不引引擎库，jcc 不必再为工具初始化引擎**，并要求一次性做完。
第九轮的"克制结论"作废：分叉风险在「下沉后 tool call 不再调宿主」的前提下不成立（文件化后是单一真相源）。

**一次性下沉剩余 34 个，media 60/60 全 local、零 host**。三块纯 BCL 基础设施：
- `BclPng.cs`：纯 BCL PNG 编解码（ZLibStream + filter 反滤波 + CRC32），替代 SkiaSharp 的 Decode/Encode。
- `MediaLlm.cs`：BCL HttpClient 直调 OpenAI-compatible `/chat/completions`，读 `~/.jcc/settings.json` 的
  `current.profile` → vendor 的 endpoint/model/apiKeyEnvVar，密钥从环境变量取。不引任何 LLM SDK。
- `MediaStateCommands.cs`：undo 栈/录制/场景/voice/vcr 状态全部文件化到 `~/.jcc/media_state/`，跨进程持久化。

**能力边界（如实记录，不谎称完全等价）**：
- `voice_start/stop/status`：状态文件化，但真实麦克风录音仍需外部录音源写入 wav 文件；本 exe 提供会话状态管理 + voice_transcribe（BCL HttpClient 调 Whisper `/audio/transcriptions`）。
- `vcr_record/playback/status`：cassette 文件管理与模式状态下沉；但**全局 HTTP 拦截中间件**（VcrHttpHandler 在宿主 HTTP 管道）本质无法跨进程——本 exe 管理 cassette 与模式，实际流量拦截仍需宿主侧配合。这是进程边界的物理限制，不是库依赖问题。
- LLM 类：`temporal_aggregate` 用纯文本 prompt；vision 类用 `image_url: data:image/png;base64,...`。
  真机验证到 API 层（DeepSeek 返回认证错误证明链路通），因环境 key 无效未能验证成功响应。

**验证**：exe `--list-local` 报 60；真机 screenshot(3.5MB PNG)/measure_depth(纯色图 std=0)/quadtree_render/undo↔click↔history 闭环/recording 闭环/VCR 闭环/菜单/环境状态全对；
LLM 链路（配置解析→profile 切换→HTTP→错误提示）全通；jcc 全链路 tool tree 显示「全部 60 个独立 exe 实现」，代表工具全 exit=0；
契约层 33/33、声明对账 60/60、编译 0 警告 0 错误。settings.json 测试期间临时切换已恢复。

### 2026-11-16 第十一轮起：全组 host 清零计划（TODO 表驱动）

用户要求：持续推进，每组做完一个 commit，不停下问。目标：把 352 个 host 工具按组逐个甄别下沉，
按「jcc 轻」优先——工具进 exe、exe 只用 BCL、状态文件化。当前分布（640 工具，独立 exe 288）：
slash 99 / system 69 / agent 57 / search 39 / mcp 25 / code 22 / shell 21 / tools 16 / web 3 / file 1。

| # | 组 | host 数 | 状态 |
|---|---|---|---|
| 28 | shell | 21 | 进行中 |
| 29 | code | 22 | 待办 |
| 30 | tools | 16 | 待办 |
| 31 | system | 69 | 待办 |
| 32 | agent | 57 | 待办 |
| 33 | search | 39 | 待办 |
| 34 | mcp/web/file | 29 | 待办 |
| 35 | slash | 99 | 待办 |

### 2026-11-16 第十一轮收官：全组下沉总账

用户要求 TODO 表驱动、每组做完 commit 不停下。本轮完成 #28~#35 共 8 组，加之前 media 全量，合计：

| 组 | 下沉前 local | 下沉后 local | 总数 | 本轮提交 |
|---|---|---|---|---|
| shell | 6 | 11 | 27 | 31233f87c |
| code | 10 | 22 | 32 | bcc613cfe |
| tools | 2 | 9 | 18 | fd0ea6916 |
| system | 3 | 21 | 72 | c67463dd4 |
| agent | 2 | 23 | 59 | 879d4437e |
| search | 8 | 14 | 47 | 41023d8b0 |
| mcp | 0 | 7 | 25 | c9f2f50b4 |
| file | 12 | 13 | 13 | c9f2f50b4 |
| slash | 0 | 5 | 99 | 本轮 |
| media | 26 | 60 | 60 | 0855a78f0 |

**总账**：独立 exe **288 → 370/640（58%）**，host 352 → 270。git：全 173 delegate（本就不在注册表）。

**剩余 270 host 的物理/架构边界（已逐组甄别，不是遗漏）**：
- 活进程/会话态：agent 系 36、slash 会话命令 94、mcp 连接类 18、task_stop 类
- 需宿主索引模型：search 符号/graph 33（索引在 LSM-Tree SSTable，exe 读不了；create_index 需 native 解析器、semantic 需向量模型）
- 需宿主配套改造（写类改了宿主内存不认）：config_set/permission 写/brief_mode/memory 写/cron 写等，已列入 backlog
- SkiaSharp 像素渲染 / LLM 会话流 / TUI 回调

**顺带修复的既有回归**：`jcc tool call slash <cmd>` 对 host 命令错走 mcp_call（应走 slash_call），
根因是 CliToolCommand 的「host 进程内快路径」把 slash 也包含了。改为 slash 组跳过快路径、走 exe 的 HostVerb。
另加 `CliNodeDefinition.HostNameNormalizer`（转发宿主时去前导 '/'）。

**slash 生成器改造**：原 generate_slash_cli_node.py 硬编码 local_tool_count=0/全 host；改为扫描 private/*.cs 反推 local。

### 2026-11-17 第十二轮：拆掉「写类必须留宿主」的前置条件（permission 写类下沉）

第十一轮把 `config_set/permission 写/brief_mode` 归入 backlog，理由是「写类改了宿主内存不认」。
本轮把这条前置条件拆掉一个：**宿主按内容戳感知外部改写**，于是 permission 写类可以进 exe。

**宿主改造**（`AgentPermissionMode.cs`）：
- 原来 `EnsureRulesLoadedAsync` 用 `Interlocked` 只装载一次，之后规则只活在内置字典里 —— exe 改写 `rules.json` 后宿主看不见，
  「AI 通过 exe 收回权限」在宿主里仍然生效，这是安全问题不是体验问题。
- 改为**内容戳比对**：每次权限判定前读一次 `{gitRoot}/.jcc/permission/rules.json`（文件仅数百字节，页缓存级），
  内容与上次装载的原文不同才整体重载；`SaveRulesAsync` 落盘后把内容戳同步成刚写下的 JSON，避免自写自读白重载。
- 无持久化管道（`_persistencePipeline is null`）时**不重载** —— 那种模式下规则只活在内存，文件不是真相源，重载会误清空。
- 顺带修一致性问题：`SaveRulesAsync` 原来把相对目录 `.jcc/permission` 交给持久化管道（落到进程 CWD），
  而读取端用 `{gitRoot}/.jcc/permission`。CWD ≠ 工作区根时读写不在同一文件。新增 `ResolveRulesLocation(fs)` 读写共用。

**exe 侧**（`groups/system/private/SystemConfigCommands.cs`）：
- `permission_add_rule` / `permission_remove_rule` / `permission_clear_rules` 变 local，直写同一文件。
- schema 与宿主 `PermissionJsonContext` 逐字段对齐：camelCase、枚举**数值**（`PermissionMode` 声明序 plan/auto/ask/bypass/unattended，
  `PermissionLevel` 声明序 none/read/write/execute/admin），null 字段不落盘。
- 读写都用 `JsonNode`：读侧容忍「数值或字符串」两种历史写法，写侧只动 `agentPattern` 命中的那条，其余规则原样保留。
- `RulesPath` 原来「CWD/.jcc 优先，否则 ~/.jcc」是自造的第二真相源，改为向上找 `.git` 得工作区根，
  与宿主 `GitWorkspaceResolver.FindGitWorkspaceDir` 同语义；不在 git 工作区则明确报错而不是偷偷写到别处。

**验证**：
- `AgentPermissionManagerExternalWriteTests` 3 例（外部改写、未变不清空、外部清空）——红转绿；
  `Guard.Security.Tests` 全量 1139 通过。
- 真机：`jcc-tool-system.exe permission_add_rule` → 文件内容 `[{"agentPattern":"demo-agent","mode":2,"level":1,...}]`；
  经 `jcc tool call system permission_add_rule` 路由再写一条，既有规则原样保留；list/remove/clear 四条全部往返正确（ask/read、auto/write 名称解析无偏差）。

**总账**：system 组 local 21 → 24，host 270 → 267。

**下一批候选（同一把钥匙可继续开）**：`brief_mode`（`BriefModeService` 同样启动期装载，加内容戳即可）、
`memory_*` 写类（memdir 服务是否按需读文件待甄别）、`cron` 写类（调度器需按内容戳重建定时器，成本更高）。

### 2026-11-17 第十三轮：code 组 10 个 lsp_* 下沉（exe 自持语言服务器）

上一轮把 lsp 归为"物理边界"，理由是"绑定宿主常驻服务器进程"。这个判断经不起推敲：
**LSP 本来就是 csharp-ls 这种独立可执行进程，JSON-RPC over stdio，谁起它都一样。**
宿主能起，exe 当然能起 —— 真正绑住的是"常驻复用"，不是"能力"。

**新增 `groups/code/private/LspConnection.cs`**（纯 BCL，约 260 行）：
- `Process` 起服务器（CWD=工作区根），`Content-Length` 手写分帧，`JsonNode` 收发；
- `initialize`（processId/rootUri/空 capabilities，与宿主 `LspClient.ConnectAsync` 同参）→ `initialized` → 请求 → `shutdown`+`exit`；
- stderr 后台排空（否则管道写满服务器会卡住），退出/失败时把末尾 stderr 拼进报错 —— AI 能看见"服务器为什么没起来"；
- 服务器反向请求（`client/registerCapability` 等）一律回 `null`；服务器通知（诊断、索引进度）直接丢弃；
- 单次请求超时 180s（`JCC_LSP_REQUEST_TIMEOUT_SECONDS` 可调），initialize 用配置里的 `startupTimeoutSeconds`。

**新增 `groups/code/private/LspSupport.cs`**：读 `~/.jcc/lsp-servers.json`（与宿主 `LspConfigLoader` 同一文件，不另立真相源），
按扩展名挑服务器；缺配置/缺 command 时给可执行引导，不静默降级。

**新增 `groups/code/private/LspCommands.cs`**：10 个工具映射到标准方法（definition/implementation/references/hover/completion/
documentSymbol/workspaceSymbol/prepareCallHierarchy + 调用层级两跳）。参数 1-based → 0-based 换算、`Location` 与 `LocationLink`
两种响应形态、`CompletionList.items` 包装、`DocumentSymbol` 递归子节点，全部按 LSP 3.17 处理。

**短生命周期是刻意的**：每次调用起一个服务器、退出即关。代价是冷启动（本机 csharp-ls 实测 5s 内完成 documentSymbol），
换来的是 exe 里不存在"宿主索引态"这第二份真相源 —— 与 §ADR 0157 一致。需要跨调用复用时应由调用方批量取数，而不是让 exe 偷偷常驻。

**真机验证**（`%TEMP%\lspdemo` 双文件工程 + 本机 `csharp-ls`）：
documentSymbol 5 符号、goto_definition（User.cs:6 → Widget.cs:6:16）、hover（`Widget.Widget()`）、
find_references 2 处、workspace_symbol 1 命中、completion 25 条、prepareCallHierarchy 1 条、
incoming_calls（Use() → Twice()）、outgoing_calls（Use() → Widget(), Twice()）、goto_implementation 正常返回。
再经 `jcc tool call code lsp_document_symbols` 路由复验一遍，`-h` 自描述正常。

**总账**：code 组 local 22 → 32（32/32 全 local），host 267 → 257。

### 2026-11-17 第十四轮：agent 组 worktree 六件套下沉（git 就是真相源）

原先 6 个 `worktree_*` 留宿主的理由是"绑定 `IAgentWorktreeService` 的进程内 session 注册表"。
但 worktree 的真相源从来不是那个注册表，而是 **git 自己**：`git worktree list --porcelain` 随时能列出全部工作树。
用注册表反而会漂移（宿主重启后列表就空了，磁盘上的 worktree 还在）。

**新增 `groups/agent/private/WorktreeCommands.cs`**，全部走 git 原生命令：
- `worktree_create`：路径 `{gitRoot}/.jcc/worktrees/{safeId}`、分支 `worktree-{safeId}`（与
  `AgentWorktreeSession.GenerateBranchName/GenerateWorktreePath` 同一套命名，已有 worktree 仍可被识别）；
  分支已存在则复用，不因重复创建失败。
- `worktree_list`：`git worktree list --porcelain`，标出 `[jcc]` 与 `[主仓/其他]`。
- `worktree_status`：分支/HEAD/未提交文件数/未推送提交数 + 变更文件清单。
- `worktree_remove`：**无 force 时先查 `git status --porcelain`，脏工作树拒绝移除**并给出下一步；force 才 `--force` 并删分支。
- `worktree_cleanup`：按目录 mtime 找陈旧项；**默认 dry-run 只列候选，`--confirm yes` 才删**；脏的一律跳过；末尾 `worktree prune`。
- `worktree_merge`：`git merge --no-edit` + `-X ours/-X theirs` 映射四档策略；冲突时列出 `--diff-filter=U` 文件并
  **`git merge --abort` 回滚 target**，不让调用方接手一个半合并状态。

**踩到的坑**：第一版用 `rev-parse --show-toplevel` 定位仓库根 —— 在 worktree 内执行时它返回 worktree 自己，
再拼 `.jcc/worktrees/{id}` 直接套娃（实测报 `...alpha\.jcc\worktrees\alpha`）。改用 `rev-parse --git-common-dir`
（worktree 与主仓共享同一 .git）后正常。

**真机验证**（`%TEMP%\wtdemo` 临时仓库）：create → list（2 工作树，1 个 [jcc]）→ 在 worktree 内提交并留脏文件 →
status（1 未提交/2 未推送）→ merge 快进成功 → 无 force 移除被拒 → force 移除并删分支 →
cleanup dry-run（2 候选）→ confirm 删除 2 个 → 冲突场景：target 回滚到合并前 commit 且工作区干净。

**总账**：agent 组 local 23 → 29，host 257 → 251。

### 2026-11-17 现状总账（修正版，替代第十一轮的边界清单）

第十一轮把 270 个 host 归成"物理边界"，其中"LSP 绑定宿主进程""permission 写需宿主内存"两条都被推翻。
下面这版按**证据**分类，每类都注明为什么 exe 做不了，避免再出现"想当然的边界"。

| 组 | host | local | 归类 |
|---|---|---|---|
| slash | 94 | 5 | 会话内命令（`/clear`、`/compact`、`/agents`…），操作的是当前 LLM 会话的上下文与 TUI —— 没有会话就没有对象 |
| system | 48 | 24 | 活 agent 查询（agent_*）、analytics 内存事件流、memory 服务写、cron 调度、`ask_user_question`/`confirm_action`（TUI 回调） |
| search | 33 | 14 | 符号索引在宿主 LSM-Tree SSTable + native 解析器；graph_* 全部派生自 `ICodeIndexer`，没有索引就没有图 |
| agent | 30 | 29 | 活 agent 控制（subagent_*/team_*/task_stop）、plan 模式切换、goal 推进、cron 写 —— 都是进程内状态机 |
| mcp | 18 | 7 | MCP 客户端连接生命周期（connect/disconnect/auth/资源读取），连接对象在宿主进程里 |
| shell | 16 | 11 | 后台进程注册表（shell_background_*）、构建队列、sandbox 会话 —— 进程句柄不可跨进程 |
| tools | 9 | 9 | skill 发现/执行 + tool_create + 评分（宿主技能注册表与打分器状态） |
| web | 2 | 3 | web_browser 需浏览器实例；web_search 是 7 家 provider 的选择矩阵（第十五轮结论：不该下沉） |

合计 **local 217 + delegate 173 + host 250 = 640**；不经过宿主的 390/640 = 60.9%（脚本 `Floor` 后打印 `60%`）。
（口径说明：早期版本这里写「local 388 / 640」，是把 local 与 delegate 混算了，以三分法为准。）

**曾列为「仍可推进但收益有限」的 12 个候选 —— 第十七轮逐个验伪，全部有实证绑定：**

1. `graph_register` / `graph_unregister` / `graph_repos`（search 组 3 个）
   —— `CodeIndexerRegistry` 是**纯内存** `MapRegistry<string, RegisteredRepo>`（`modules/perception/code_index/indexing/CodeIndexerRegistry.cs`）：
   `RegisterAsync` 现场 new 一个 `CodeIndexer` + `InMemoryIndexStore`，**没有任何落盘**。
   这些条目唯一的用途是让 `graph_query/explain/path` 从**同一个** `_registry` 实例取 indexer。
   exe 写一份 JSON 也没有任何代码会读它 —— 典型的第二真相源。
2. `tool_score` / `tool_hypergraph` / `tool_score_reset`（3 个）
   —— `ToolHypergraphScorer` 的超边共享评分由**宿主定时器**每小时 sync、每 2 小时从 transitions 重建；
   健康记录虽经 `IFileSystem` 落到 `_configPath`，但宿主 Actor 里另有内存副本。
   exe 直接读文件只能给出**过期、且与路由实际用值不一致**的分数 —— 一个会撒谎的诊断工具比没有更糟。
3. `skill` / `skill_execute`（2 个）—— 走 `ISkillService` 执行，`fork` 模式还要拿 `IAgentService` 起 subagent 跑 LLM 循环。
   在 exe 里做等于把技能执行引擎重写一遍。
4. `skill_search` / `skill_recommend` / `discover_skills`（3 个）
   —— `SkillSearchService` 维护的 tag/name 内存索引覆盖「宿主发现的全部技能」（磁盘目录 + 插件/市场技能）。
   exe 自建扫描 = 第二份「有哪些技能」的答案，与 `web_search` 同理：同一决策不该有两个实现。
5. `tool_create`（1 个）—— 把模板注册进**当前会话**的工具表，没有会话就没有对象。

**结论：host 250 是闭合边界，不再有「待评估」项。**
delegate 173 也不是遗留问题：github 组的 173 个全部转发到已有的专用 `jcc-gh.exe`（`DelegateExecutable`/`DelegateVerb` 在
`groups/github/Program.cs` 里声明），只多一次进程跳转，实现本身早已独立，没有重复逻辑。
后续若要继续减负，方向不在「再拆工具」，而在宿主侧瘦身（引擎/渲染依赖是否还能从主 CLI 移出）。

### 2026-11-17 第十五轮：web_fetch 下沉（web_search 判为不该下沉）

**web_fetch**（`groups/web/private/WebCommands.cs` + 新增 `WebLlm.cs`）：
- 抓取 → HTML 转 Markdown（复用已有 `HtmlMarkdown`）→ 二级模型按 prompt 加工，模型调用走
  `WebLlm`（HttpClient 直调 OpenAI-compatible `/chat/completions`，与 CodeLlm/MediaLlm 同一套做法，
  重复是依赖墙的代价，已在文件头注明三处需同步）。
- 对齐宿主三条关键行为：**①不自动跟随跨主机跳转**（同主机最多 5 跳手动跟随，跨主机把新 URL 回给调用方 ——
  自动跟随会把"允许的域名"变成"允许它跳到任何地方"，这是 SSRF 防护的一环）；
  **②HTML 先转 Markdown 再进模型**，超长按 100k 截断；**③模型不可用时降级返回正文前 5000 字**并附失败原因，
  不让整个抓取失败。
- 一处**刻意不复刻**：宿主的"预批准域名"白名单（`PreapprovedDomains`）。那份清单在 guard 层是安全资产，
  复制到 exe 必然漂移；代价只是官方文档站点也走一次模型加工（多几百毫秒），换来清单只有一处。
- 顺带修正配置路径：settings.json 现在按 `JCC_APP_DATA_FOLDER` / `JCC_SETTINGS_FILE_NAME` 解析，
  与宿主 `AppDataPaths.FromEnvironment` 同一套环境变量语义（原先硬编码 `~/.jcc`，指向别处的多配置环境会读错文件）。

**web_search 不下沉**（保留 host）：宿主侧是 7 家 provider 的选择矩阵
（anthropic / brave / deepseek / searxng / serper / subagent / tavily，各有优先级与 availability 判定）。
在 exe 里再写一份选择逻辑，等于把"用哪家搜索"变成两处决策，配置了 tavily 的用户会突然发现 exe 路径报错。
这不是能力问题，是**同一决策不该有两个实现**。

**真机验证**：
- 降级路径：`https://example.com` → 正文 + `[LLM processing failed: 未设置环境变量 SENSENOVA_API_KEY…]`；
- 模型链路：临时配置指向 deepseek（`JCC_APP_DATA_FOLDER` 指向临时目录）→ 请求打到 API、返回
  `Authentication Fails, Your api key: ****5856 is invalid` —— 链路通、密钥无效，与上一轮 LLM 链路结论一致；
- 跨主机跳转：`httpbingo.org/redirect-to?url=example.com` → 输出 `REDIRECT DETECTED` + 新 URL + 重试参数（与宿主文案一致）；
- 同主机多跳：`httpbingo.org/redirect/2` → 跟随后取到最终响应；
- 404：`example.com/not-exist-xyz` → 明确失败与排查提示；
- 经 `jcc tool call web web_fetch` 路由复验一遍。

**总账**：web 组 local 2 → 3（web_search / web_browser 留 host），host 251 → 250。

### 2026-11-17 第十六轮：收口验收（契约对账 + 全量自描述 + 路由抽查）

**先纠一个数字错误**：此前多轮写的「local 388/640（61%）」把 `local` 和 `delegate` 混算了。
真实分布：**local 217 + delegate 173 + host 250 = 640**；「不经过宿主的工具」= 390/640（61%）。
后面一律用三分法表述，不再用笼统的"已下沉"。

**对账与部署**（⚠️ 第十九轮已纠正：本轮原本新写了 `scripts/check_cli_node_coverage.py` 和
`scripts/deploy_cli_nodes.py`，后来发现仓库里早有 `Verify-ToolNodes.ps1` 与 `Pack-ToolNodes.ps1`，
能力都是超集且已在 CI 里 —— 两个 Python 脚本属重复实现，已归档到 `.xxx/`。下面是**现有**两个 canonical 脚本：

- `non_deliverables_tools/scripts/Verify-ToolNodes.ps1` —— 声明 vs 现实对账：
  ① exe 自报 local 数与 node.yaml 一致 ② **local 工具 id 集合一致**（见第十九轮补的检查）
  ③ `tool_count` / 条目数 / `-h` 索引三者一致 ④ `-h` 体积上限 ⑤ 单工具 `-h` 能定位执行方式。
  `-DeclarationsOnly` 只验声明侧（不编译不跑 exe），CI 用这个；`-Sample` 抽无副作用工具真跑。
- `non_deliverables_tools/scripts/Pack-ToolNodes.ps1` —— 装配到 `artifacts/pack/tool-nodes/tools/<组>/`。
  **存在理由**：`jcc tool call` 判断走 exe 还是宿主，读的是**部署目录**里的 node.yaml。本轮就踩到过：
  media 组的部署清单停在 quadtree_* 还是 host 的旧版本，于是 `jcc tool call media quadtree_neighbor -h`
  错走宿主 mcp_call 报「缺少 '=' 分隔符」——源码没问题，是只编不部署。

**验收结果**：

| 项 | 结果 |
|---|---|
| 13 组契约对账（声明 vs 注册表 vs help.txt） | 全通过（217 local / 250 host / 173 delegate） |
| 217 个 local 工具 `<tool> -h` 自描述 | 0 失败 |
| 每组抽 2 个 local 工具经 `jcc tool call` 路由 | 24/24 命中 exe（github 组无 local，跳过） |
| 各组入口注释与实际分布一致 | 已修 6 处过时说明（agent/code/system/web/search/shell） |

**仍未端到端验证的部分**（如实列出，别当成已验）：
1. 写类/交互类工具的**语义正确性**只做过点验证：`permission_*`（四条命令往返）、`worktree_*`（六条全跑，含冲突回滚）、
   `web_fetch`（降级 + 跨主机跳转 + 404）。其余写类（`media` 桌面操作、`file` 写/补丁、`system` 通知与闪烁、
   `shell` 命令执行）只到"能跑通、输出合理"，没有回归用例。
2. **LLM 链路**只验证到 API 层：请求构造/端点解析/错误上抛已确认，`DEEPSEEK_API_KEY` 无效、`SENSENOVA_API_KEY` 未设置，
   所以没有任何一次真实模型返回被检查过内容质量。
3. `search` 组 14 个 local 工具（grep/glob/project_graph 等）本轮只做 `-h` 与路由抽查，未逐个跑真实查询。
4. `media` 组 60 个 local 已经过前面轮次的真机验证，但本轮未复跑（桌面操作会动到本机窗口，不适合无人值守批量跑）。

**CI 接线**：`ci-unit-tests.yml` 里已有 `tool-node-declarations` job 跑
`Verify-ToolNodes.ps1 -DeclarationsOnly`（几秒、不编译）。要做「声明 vs 现实」的全量对账需先编译 13 个 exe，
那是本地步骤：`Verify-ToolNodes.ps1`（不加 `-DeclarationsOnly`），部署则用 `Pack-ToolNodes.ps1`。
本轮原本建议"CI 追加两步 Python 脚本"，已被第十九轮否掉 —— 那两步等价于调用上面两个 canonical 脚本，
等价物本就在仓库里，不该再造一份。

### 2026-11-17 第十七轮：最后 12 个候选验伪，host 边界闭合

第十六轮虽然建了验收体系，但文档里还挂着两条**凭印象的猜测**（"graph 多仓注册表若为 JSON 文件可下沉"、
"skill 系列扫磁盘目录但拿不掉依赖"）。本轮把这 12 个逐个读实现验伪，证据写进上面的边界表，不再留"待评估"。

三处推翻了原有猜测：
1. **`graph_*` 没有文件**。`CodeIndexerRegistry` 是内存 `MapRegistry`，注册即 new 索引器实例，
   `graph_query` 等下游工具靠同一实例取 indexer。原猜测"若是 JSON 文件可下沉"前提不成立。
2. **`tool_score` 有落盘，但落盘不是真相源**。落盘的是健康记录镜像，真正参与路由的是
   `ToolHypergraphScorer` 里由宿主定时器维护的共享评分。exe 读文件会得到与路由行为不一致的数字。
   ——这条比"读不到"更值得记：**能读到 ≠ 读得对**，诊断类工具的错报比缺报更有害。
3. **`skill_search` 的索引覆盖「宿主发现的全部技能」**（磁盘 + 插件/市场），不是单纯扫目录。
   exe 自建扫描会得到一份不同的技能清单。

**边界至此闭合**：host 250 / 640 全部有代码证据；delegate 173 是一次转发到已有的 `jcc-gh.exe`。
下一轮若继续减负，对象不再是工具，而是主 CLI 自身的重依赖（引擎/渲染/状态服务）。

### 2026-11-17 第十八轮：把 jcc 自身从 154.5 MB 降到 92.8 MB（tree-sitter 语法包裁剪）

工具侧闭合后，负担只剩主 CLI 自己。先量一遍 `artifacts/bin/JoinCode/Debug/net10.0`（排除 98 MB 的 pdb）：

| 组成 | 体积 | 是否活依赖 |
|---|---|---|
| tree-sitter 语法 30 个 | **68.0 MB** | 只有 2 个在用 |
| JoinCode.\* 自有程序集 136 个 | 25.3 MB | 是 |
| SkiaSharp | 12.2 MB | 是（ShellImageOutputDetector / GDI 截图 / Vision） |
| onnxruntime | 11.9 MB | 是（实测 Phase E2 嵌入真跑了） |
| pdfium (Docnet) | 4.7 MB | 是（`read` 基础工具走 FileSpecialFormatReader 读 PDF） |
| PuppeteerSharp + WebDriverBiDi | 6.2 MB | 是（web_browser 留宿主） |
| MCP 相关 | 3.1 MB | 是 |

**结论：唯一的死重是 tree-sitter 语法包。** 生产代码里 `new Language(...)` 的全部调用点只有两处
（`grep -rn "new \(TreeSitter\.\)\?Language(\""` 全仓只有 2 条 + `new TreeSitterParser("c-sharp")` 3 条）：

- `c-sharp` —— CodeIndex 解析 `*.cs`；`CodeIndexer.CollectCsFilesRecursive` 只扫 `*.cs` / `*.md`
- `bash` —— guard.security 的 `BashAstSecurityWalker`（FAIL-CLOSED 命令检查）

其余 28 个语法（verilog 17.8 MB、razor 10.8 MB、cpp、scala、haskell、julia、ocaml、ruby、tsx、typescript、
rust、php、agda…）**从未被任何代码请求**。`TreeSitter.DotNet` 按 `"tree-sitter-" + languageId` 拼名加载，
不枚举目录，所以删掉不影响任何调用点。

**改动**（`modules/apps/cli/JoinCode.csproj`）：
1. Release 的 `EmbeddedResource` 从 `tree-sitter*.dll` 全量 glob 改成显式 3 个文件（core + c-sharp + bash）；
2. 新增 `TrimUnusedTreeSitterGrammars`（`AfterTargets="Build"`，非 Release）删掉输出目录里其余语法 —— NuGet 每次
   按 `PreserveNewest` 重新拷贝，所以必须挂在每次 Build 之后，不能只删一次。

**结果**：`154.5 MB / 343 files` → `92.8 MB / 315 files`，净减 **61.7 MB（-40%）**，编译 32 秒 0 警告 0 错误。

**验证**：
- 真机解析：临时工作区放一个 `probe.cs` → `jcc mcp_call create_index` 输出 Phase D 提取成功、符号数 76367 → 76371
  （+4 个符号来自 probe.cs），证明 `tree-sitter-c-sharp` 真的在工作，不是"文件在但加载不了"；
- 同一次调用里 Phase E2 嵌入也用上了 onnx，说明 onnx 不是死重；
- bash 语法只做**静态**验证（保留文件 + 唯一调用点），未在 jcc 内触发实际解析 —— 宿主 bash 工具需要
  `SystemActuatorRegistry.RegisterFactories()` 完整启动，`mcp_call` 直调路径起不来。

**顺带发现（未改，待定）**：`IBashSecurityValidator` → `BashPermissionChecker` 这条 bash AST 安全检查链
在 `modules/**` 里除 guard.security 自己与测试外**没有任何消费者**（`grep -rn "IBashSecurityValidator"`）。
即 FAIL-CLOSED 的 bash 检查目前可能根本没接线，与 CLI 化无关、也不是本轮引入的。这条留给人判断，
不适合顺手改 —— 要么是历史未接线，要么是有反射/配置装配路径没被我读到。

### 2026-11-17 第十九轮：撤掉我自己造的重复脚本，把唯一增量并进 canonical 版本

上一轮（第十六轮）收口时我新写了 `scripts/check_cli_node_coverage.py` 和 `scripts/deploy_cli_nodes.py`，
当时以为仓库里没有等价物。这一轮做 CI 接线时才发现：

| 我新写的 | 仓库已有的 | 差距 |
|---|---|---|
| `check_cli_node_coverage.py` | `non_deliverables_tools/scripts/Verify-ToolNodes.ps1` | 已有版本多了：`-DeclarationsOnly`、`-Sample` 无副作用抽样、`-h` 体积上限、单工具 `-h` 定位、`group_map.yaml` 双向校验 |
| `deploy_cli_nodes.py` | `non_deliverables_tools/scripts/Pack-ToolNodes.ps1` | 已有版本多了：按需编译、单组失败不影响其余、`tools/<组>/` 布局 |

而且 `Verify-ToolNodes.ps1` **早就在 `ci-unit-tests.yml` 的 `tool-node-declarations` job 里跑**（`-DeclarationsOnly`）。
也就是说我不仅造了重复，还写了一版「CI 接线建议」去接一个已经接好的东西。

**处置**：
1. 我的脚本里**唯一更强**的一点是比 local 工具的 **id 集合**（已有版本只比数量）。把这条并进 `Verify-ToolNodes.ps1`：
   数量相等但换了人（A 从 local→host、B 从 host→local）计数完全一样，`-h` 也不会报错，但工具实际跑在哪已经变了。
   比对前按 `CliNodeRunner.CanonicalToolId` 同口径 `TrimStart('/')` 归一化，否则 slash 组会假报。
2. 两个 Python 脚本按项目规范归档到 `.xxx/*.del`（不删除）。
3. 全量跑一遍 `Verify-ToolNodes.ps1 -SkipBuild`：13 组 / 640 工具 / 隔离 390（60%），新增集合检查零假报。

**教训**：收口阶段最该先做的是「搜一遍有没有现成的」，而不是「先写一个能跑的」。
判断标准本来就是我自己反复用的那条 —— 同一决策不该有两个实现 —— 结果自己踩了。

### 2026-11-17 第二十轮：把「声明 vs 现实」对账接进 CI

上一轮的教训是「先搜有没有现成的」。这一轮把那份现成的用起来：`Verify-ToolNodes.ps1` 之前只有
`-DeclarationsOnly` 进了 CI，真正的完整对账一直靠人记得在本地跑。**人记得跑的东西等于不存在**，
所以加了一个 `tool-node-reality` job（`ci-unit-tests.yml`，windows-latest，15 分钟超时）。

它在 PR 上多拦两类声明侧看不见的漂移：

1. **等量互换**：A 从 local 改成 host、同时 B 从 host 改成 local，`tool_count` / `local_tool_count` 全对，
   `-h` 也不报错，但「工具实际跑在哪」已经变了。只有逐 id 比 local 集合才发现。
2. **分组 exe 编译不过**：声明侧只读 yaml，源码写崩了它照样绿。

**关键改动是 `-FromScratch` 开关**。脚本默认走 `--no-restore` + `-p:BuildProjectReferences=false`
的快路径 —— 那是为「依赖产物已就绪」的本地增量场景准备的（单组 5 秒）。干净容器里没有
`project.assets.json`，也没有 `Abstractions.Primitives.Tools.dll`，同样的参数会直接失败。
所以 `-FromScratch` 去掉这两个开关，让 MSBuild 自己还原并构建契约层；
本地增量**不要加它**，否则白丢 5 秒/组的优势。

契约层只依赖 YamlDotNet、`ExcludeStructura=true`，所以冷启动还原成本很低 ——
实测本地 13 组冷启动 + 抽样真跑全流程 **36 秒**（CI 首次还原会慢一些，15 分钟余量充足）。

与 `tool-node-declarations` 分成两个 job 而不是合并：声明侧几秒就失败，
定位比「等 13 个 exe 编完再说 yaml 写错了」快得多。

**验证**：
- `-FromScratch -Sample` 全量跑：13 组 / 640 工具 / 隔离 390（60%），8 次抽样真跑全通过，退出码 0；
- `-DeclarationsOnly` 复跑仍通过（改动没有破坏声明侧）；
- 两个 workflow yml 用 js-yaml 解析通过，job 结构完整（`runs-on` + steps 齐全）。

**顺带订正 ADR 0157**：那份 ADR 的「逐组做实清单」还是 2026-11-16 的快照（`github 172/172`、`code 10/32`、
`media 0/60`、`slash 0/99`…），与现状差得远，而且「剩下 411 个不硬做」的六类依赖清单里
`lsp_*`、`permission_*`、`web_fetch` 都已经下沉了。按 `--list-local` 实测重写成三列
（local / delegate / host），依据也换成现在的理由；给 11-16 那次决策的当日记录加了
「数字是快照，现行口径见修正注」的指针，避免它被当成现状引用。
另把 `390/640` 的比例统一写成 `60.9%（脚本 Floor 后打印 60%）`——
之前 ADR 写 61%、脚本打印 60%，同一个事实两个数，迟早有人当成矛盾再「纠」一次。

### 2026-11-17 第二十一轮：追掉第十八轮那条「bash 安全检查链没接线」的遗留，并修掉一个真实降级

第十八轮裁剪 tree-sitter 语法时顺带发现：`IBashSecurityValidator` → `BashPermissionChecker`
（`guard.security/services/bash_validation`）在 `modules/**` 下除自身与测试外**零消费者**。
本轮把它查清并修掉了真正有害的那一半。

**一、那条链为什么没接线（不是漏接，是被替代了）**

bash 执行链上的真实防线是 `ShellCommandInterceptionMiddleware` →
`BashDefenseService`（6 个 node：危险命令 / 严格解析 / 保留设备名 / 重定向白名单 / git push CI 守卫 /
argv hash），已在 `DataDrivenPipelineComposition` 注册。权限层还有
`DangerousCommandProtectionMiddleware` 再拦一道。`guard.security/services/bash_validation`
那套是 TS `bashPermissions.ts` / `bashSecurity.ts` 的移植，被上面两条取代后留在原地。
DI 容器里**确实注册了**它（生成器自动生成 `ServiceDescriptor`），所以「搜接口没找到消费者」
不等于「没注册」——这也是它一直没被发现的原因。

**二、对照实验（`BashCommandCoverageTests`，只做静态判定不执行）**

同一批命令跑「当前生效的分类器」与「未接线的 AST 校验器」，结果推翻了我原先的两个假设：

| 命令 | 分类器 | AST 校验器 | 说明 |
|---|---|---|---|
| `rm -rf ./build` | Execution | safe | **Execution 不是 Dangerous**：Dangerous 只留给整盘/系统级 |
| `rm -rf /` | Dangerous | safe | 基线正确 |
| `r''m -rf ./build` | Execution | safe | 分类器**能**剥引号噪声，比 AST 还稳 |
| `echo hi; rm -rf ./build` | ~~Safe~~ → **Execution** | safe | **真缺口**（已修） |
| `true && rm -rf ./build` | ~~Unknown~~ → **Execution** | safe | 同上 |
| `echo hi\nrm -rf ./build` | ~~Safe~~ → **Execution** | safe | 同上 |
| `echo hi \| xargs rm -rf` | Safe | safe | 未修：管道不拆的已知取舍，见下 |
| `jq -n 'system("id")'` | Unknown | JqSystemFunction | 仅 AST 能识别 |
| `cat > x.sh <<EOF…EOF` | Safe | Newlines | 仅 AST 能识别 |
| `eval "$(… base64 -d)"` | Unknown | DangerousVariables | 仅 AST 能识别 |

两个假设被推翻：① 以为「复合命令是缺口」方向对，但以为 `rm -rf ./build` 是 Dangerous（实际 Execution）；
② 以为 AST 校验器是更强的防线（实际对引号混淆、普通删除它都判 safe，它管的是「注入/可解析性」，
不是「破坏性」）。**两套是不同维度，不是新旧替代关系。**

**三、修的是哪一半（commit `d03e33584`）**

分类器按「首词查表」工作，`echo hi; rm -rf ./build` 首词是 `echo` → `Safe`。
而 `Safe` 是**所有权限模式下直接放行**的级别（`RequiresIntervention = Level != Safe`），
`Execution` 在 Auto 模式会被「拒绝并引导切 Ask」—— 也就是说：

> 单跑 `rm -rf ./build` 会被拒，加个 `echo hi;` 前缀反而放行。

这是明确的等级降级，且 Auto 模式（默认安全档）下生效。修法：

1. `ShellCommand.SplitSubcommands`（新增，contracts 层）：引号 / 反引号 / `$(...)` 感知地按
   `;`、`&&`、`||`、换行 拆分。**单管道不拆**（两侧是同一命令的数据流，上游已有「管道目标是解释器」的专门检查，
   拆开会把 `git log | sh` 绕过去）；**含 heredoc 时不拆换行**（正文里的 `rm -rf` 只是写进文件的内容）。
   > 2026-11-17 修正：「上游已有专门检查」这个理由当时是错的 —— 那条检查是子串匹配，
   > 既误报 `cat notes.sh | head` 又漏报 `| xargs rm -rf`。管道右侧的判定已在第二十二轮改为
   > `SplitPipelineStages` + 命令位解析；`SplitSubcommands` 依旧不拆单管道，但理由变成「拆了会丢数据流语义」。
2. `CommandDangerClassifier.Classify(string)`：拆段后逐段分类，取最严的一条；同级别时合并 risks/Details。
   单段命令完全走原路径，行为不变。

**验证**：`guard.security.tests` 1149 通过 / 0 失败（含新增的复合命令回归锁与中间件 Auto 模式断言）、
`abs.guard.tests` 14 个拆分器边界用例、`hands.shell.tests` 526 通过。
`guard.hooks.tests` 有 1 个环境相关失败（`VpnRouteGuard_CanHandle_NoVpnActive_ReturnsFalse`，
本机有代理进程；该 guard 不经过任何本次改动的代码路径）。

**未修、需要决策的两项**（都是「补强」不是「修 bug」，会改变用户体验，所以没替用户决定）：

1. **接线 AST 校验器**补 `jq system()` / `eval` / 命令替换 / heredoc 换行。它的语义是
   「无法静态分析 → 交给用户确认」（`BashPermissionChecker` 把 IsSafe=false 映射为 `Ask`，不是硬拒），
   所以不会拦死命令，但**每条 bash 多一次 tree-sitter 解析、写脚本类命令会多一次确认弹窗**。
2. ~~**管道后接执行器**（`| xargs rm -rf`、`find -exec` 未覆盖）~~ → **已在第二十二轮修复**：
   当时的判断是「属新规则，不是拆段能解决的」，这个判断对，但结论错了 —— 它和分号前缀是**同一类降级**，
   不是可选补强。见下。

原始问题记录在第十八轮末尾，此处闭合它的调查部分。

### 2026-11-17 第二十二轮：管道前缀与分号前缀是同一个降级，`| xargs rm -rf` 原来判 Safe

上一轮修完 `echo hi; rm -rf ./build` 后，我把「管道后接执行器」留在待决策清单里，理由是「接近 AST 分析范畴」。
这一轮先量了一下，发现它的实际行为不是「覆盖不足」，而是**和刚修的那条一模一样**：

```
单跑  rm -rf ./build          → Execution（Auto 拒绝）
前缀  echo hi; rm -rf ./build → Execution（上一轮已修）
管道  echo hi | xargs rm -rf  → Safe      ← 所有权限模式直接放行
```

首词是 `echo`，分类器按首词查表 → Safe。**同一个 bug，只是分隔符换了**。所以这不是「要不要补强」的选择题。

**为什么原来没被发现**：`Combinations` 里确实有一组 `["|", "sh"]` 形式的「管道到解释器」组合，
但它的匹配口径是「原始字符串同时包含 `|` 和 `sh`」这类**子串**判断 —— 两个方向都不准：

| 写法 | 旧口径 | 真实语义 |
|---|---|---|
| `| sh` / `| bash` | 命中（靠子串） | 命中 ✓ |
| `echo hi \| xargs rm -rf` | 未命中 | 应 Execution ✗ |
| `find . -exec rm -rf {} +` | 未命中（无管道） | 应 Execution ✗ |
| `cat notes.sh \| head` | **误报 Execution**（文件名带 sh） | 良性 ✗ |
| `ls \| grep shared` | **误报 Execution**（搜索词带 sh） | 良性 ✗ |

**改法**（不在数据表里加规则，而是把「谁在执行」算对）：

1. `ShellCommand.SplitPipelineStages`（契约层）：与 `SplitSubcommands` 共用同一套引号 / 反引号 /
   `$(...)` 感知的扫描器，只是额外按单管道拆。heredoc 正文里的 `|` 不拆（正文总在换行之后，只拆起始行）。
2. `CommandDangerClassifier.ClassifyIndirectExecution`：枚举「被外层带着执行的内层命令」再逐条判，
   取最严。来源两处 —— 管道右侧、参数内嵌（`xargs` 的参数、`find -exec/-execdir/-ok`）。
   支持递归展开（`| xargs rm -rf` 的右侧解析出 `xargs`，真实命令在它的参数里），层数上限 4。
3. **命令位解析** `ResolveCommandPosition`：跳过包装器（`sudo` / `env` / `nohup` / `time` /
   `command` / `builtin` / `exec`）、选项（`-E`）、环境变量赋值（`FOO=bar`），返回真正被执行的那个词。
   `curl x | sudo -E bash` 的执行者是 `bash`；不跳包装器就会漏，而「整段扫一遍找解释器」会把
   `| grep bash file` 误判成执行解释器。
4. **内层命令不产出 Unknown 信号**（`CollectSignals(unknownCommandIsRisk: false)`）。这条是成败关键：
   `| jq`、`| sort`、`| tee` 都没在 catalog 里登记，一起判 Unknown 会让**最常见的良性管道在 Auto 模式下全被拒**，
   用户会因为噪音直接关掉防护。只做正向识别（解释器 / 已登记命令 / 危险参数 / 递归强制组合）。
5. 删掉 `Combinations` 里的 `["|", …]` 子串组合 —— 判定改为命令位后它就是双份实现加双份误报；
   `DangerousCommandCatalogIntegrityTests` 换成「不许再把子串口径加回来」的断言。

**验证**：
- `BashIndirectExecutionTests`（新增，23 例）：8 条内层危险命令必须判 Execution（含 `find -exec`、
  `xargs`、`| sudo bash`、`| env FOO=1 bash`、`| nohup sh -s`），6 条良性管道必须保持原级别
  （`| jq` / `| sort` / `| head` / 文件名带 sh），拆分器引号与 heredoc 边界。
- `DangerousCommandProtectionMiddleware` 层加锁：`echo hi | xargs rm -rf ./build`、`echo hi | sh`
  在 Auto 模式下必须被拒；`ls -la | sort | head`、`cat notes.sh | head -3` 必须放行。
- `guard.security.tests` 1169 通过 / 0 失败、`abs.guard.tests` 107 通过、`hands.shell.tests` 526 通过、
  `guard.config.tests` 1305 通过。`guard.hooks.tests` 仍是那 1 个环境相关失败（本机有代理，与改动无关）。

**没做到的验证**：真机 `jcc mcp_call bash` 跑不了 —— `SystemActuatorRegistry not initialized`，
需要 `jcchub.exe` 完整启动，本机没有。所以端到端只在中间件层锁住（那正是决定放行/拦截的地方），
没有走「真实执行被拦」这一层。

**顺带发现（未改）**：catalog 里没有登记 `find`、`sort`、`jq`、`xargs` 等常用命令，
单跑 `find . -name "*.cs"` 是 `Unknown` → Auto 模式会被拒。这是「未知即询问」策略与覆盖面的摩擦，
不是本轮引入，改它等于调整安全姿态，需要单独决策。

---

### 2026-11-17 第二十三轮：`-r` 在 `sort` 上是逆序、在 `rm` 上是递归删除 —— 参数作用域与只读登记

上一轮结尾那条「顺带发现」被用户要求一并处理，动手量的时候发现它和另一件同源的事必须一起改。

**一、被拦的不是「未知命令」，而是「带短 flag 的已知命令」**

Auto 模式下实测（全部被拒，Auto 拒绝一切非 Safe）：

| 命令 | 修前 | 修后 | 说明 |
|---|---|---|---|
| `ls -R` | Execution | Safe | `-R` 是「递归列目录」，只读 |
| `tail -f app.log` | Execution | Safe | `-f` 是「跟随文件」 |
| `sort -r list.txt` | Execution | Safe | `-r` 是「逆序」 |
| `grep -r TODO src` | Execution | Safe | `-r` 是「递归搜索」 |
| `rm -r ./build` | Execution | Execution ✓ | 仍是递归删除 |
| `rm -f x` | Execution | Execution ✓ | 仍是强制删除 |
| `mv -f a b` | Execution | Execution ✓ | 仍是覆盖不提示 |

根因：`Flags` 表是「纯 flag → 风险」的**扁平**映射，判定时只看 flag、不看命令名。
`-r` / `-R` / `-s` / `-f` 这些短 flag 在不同命令上语义完全无关，
扁平表只能取「最危险的那个含义」，于是所有带 `-r` 的命令都被当成删除。

**二、改法：给 flag 加作用域，而不是给命令加白名单**

1. `DangerousCommandCatalog.FlagEntry` 新增 `IReadOnlySet<string>? AppliesTo`（`null` = 原来的通用语义）。
2. `-r`、`-R`、`-recurse`、`-Recurse`、`-f`、`-force`（以及 Windows 风格 `-F/` `/s/` `/S/` `/f/` `/F/`
   `/q/` `/Q/` `/*/` `/*.*/`）全部挂到 `BuildDestructiveFlagCommands()` 派生出的四个集合上
   （FileDeletion / DirectoryDeletion / DataModification / SystemModification）——
   该方法因静态字段初始化顺序必须是私有方法，不能直接引用公共集合。
3. 统一入口 `DangerousCommandCatalog.MatchFlag(commandName, flag)`，
   `CommandDangerClassifier.CheckArgumentRisk` 改为委托它。判定链上只有一个 flag 判定点。

**三、登记常用只读命令**（`DangerCommandDefinitions`）

`Safe` 组新增「文本与数据处理工具」24 个：`find` `rg` `sort` `uniq` `cut` `tr` `column` `tree` `diff` `file`
`stat` `basename` `dirname` `realpath` `readlink` `seq` `xxd` `base64` `md5sum` `sha1sum` `sha256sum`
`true` `false` `test` `sleep` `env` `jq` `awk` `sed`；`LightValidation` 新增 `mkdir`、`touch`。

**这不是放宽判定，是收窄 `Unknown`**：`Unknown` 的语义是「不认识 → 询问用户」，
把常用只读命令登记成 `Safe` 只是承认事实。真正的破坏性形式在同一张表里单独登记并被升级：

| 组合 | 级别 | 为什么 |
|---|---|---|
| `find -delete` | Execution | 删除，不是只看 |
| `awk system(` | Execution | 在 awk 里开 shell |
| `jq system(` | Execution | 同上 |
| `sed -i` | LightValidation | 原地改文件 |

**验证**：`BashCommandCatalogCoverageTests`（新增，125 行）做**双向**断言 ——
只读命令的同名 flag 必须保持 Safe、删除类命令的同名 flag 必须仍为 Execution、
24 个常用命令必须已登记、登记成 Safe 的危险形式必须被升级、包装器（`sudo`/`env`）必须透明。
`DangerousCommandCatalogIntegrityTests` 新增 `Flags_RecursiveAndForce_Should_Be_ScopedToDestructiveCommands`
与 `MatchFlag_Should_RespectCommandScope`，防止后来把 `AppliesTo = null` 手滑加回去。

---

### 2026-11-17 第二十四轮：AST 校验器接线，但只升级「构造/混淆」类命令

「`guard.security/services/bash_validation` 没接线」这条从第二十一轮挂到现在。上一轮我给的判断是
「它不是更强的防线」，这一轮要把它接进执行链，先得把「接哪一部分」量清楚。

**一、它和分类器不是新旧替代，是两个维度**

实测它对破坏性完全无感：`rm -rf ./build`、`r''m -rf`、`"rm" -rf` 全判 `safe`；
它管的是**注入 / 可解析性**（`jq system()`、`eval`、命令替换、heredoc、IFS 注入…）。
所以接线方式不是「替换分类器」，而是「分类器判 Safe 之后再补一刀」。

**二、关键是不能把噪声一起接进来**

这套校验器的口径来自「这条命令能否免确认自动执行」。拿本仓库日常场景实测 25 条命令，
下列 7 个检查项在**完全良性**的写法上就会触发：

| 检查项 | 触发举例 | 这是坏事吗 |
|---|---|---|
| `Newlines` | `cat > x.sh <<EOF … EOF` | 否，写脚本 |
| `MalformedTokenInjection` | `if [ -d x ]; then …`、`[ -d x ] && …` | 否，正常条件 |
| `OutputRedirection` | `cmd 2>&1 \| tee log`、`exec 3>&1` | 否，正常重定向 |
| `DangerousVariables` | `source ~/.bashrc` | 否 |
| `BackslashEscapedWhitespace` | `echo a\ b` | 否 |
| `ObfuscatedFlags` | `IFS=$'\n'` | 否，改分隔符 |
| `BraceExpansion` | `echo {a,b}` | 否 |

一并升级的后果是：用户每次写 heredoc 脚本、写 `if` 语句、写 `2>&1` 都要多一次确认弹窗 ——
防护会被当成噪音，最后被整开关掉，比不接更糟。所以**只升级 8 个「被构造/混淆」检查项**：
`JqSystemFunction`、`IfsInjection`、`ControlCharacters`、`UnicodeWhitespace`、`MidWordHash`、
`BackslashEscapedOperators`、`ProcEnvironAccess`、`CommentQuoteDesync`。
共同特征：正常写法不会命中，命中即说明**命令真实内容与字面不同**，静态判定不可信。

**三、接线方式**

- 新增 `IBashAstRiskEscalator` / `BashAstRiskEscalator`（`guard.security/services/bash_validation`），
  `[Register]` 单例；升级结果为 `Unknown` + `CommandRisk.RemoteExecution`，消息里直接给等价写法建议。
- `DangerousCommandProtectionMiddleware` 构造函数末尾追加**可选**参数 `IBashAstRiskEscalator? astEscalator = null`
  （不破坏既有测试与手工构造点），`DetectRisks` 里 `Classify(command)` → `ClassifyWithAstBackstop(...)`：
  分类器已判需要干预就直接返回，只有判 Safe 时才补跑 AST。
- 开关 `JCC_BASH_AST_ESCALATE=0`，排障时不必改代码。

**四、性能取舍（如实说）**

`BashAstSecurityWalker` 构造器里就 `new TreeSitter.Language("bash")` + `new TreeSitter.Parser(_language)`，
即立刻加载原生库建 parser，**不是零成本**。中间件只在分类器判 Safe 时调用 —— 但「判 Safe」恰好是
放行路径上的常态，所以实际代价是**每条要放行的 bash 多一次 tree-sitter 解析**，省掉的只是本就判危险的那一小撮。
这是本方案明确的代价，不是意外；若要消掉，得把解析结果缓存到命令指纹上，属另一件事。

**五、与原计划的偏离**

原本打算把「heredoc 换行」「命令替换」也纳入升级。实测 heredoc 换行（`Newlines`）是写脚本的正常形式，
升级即误伤创作行为，故**未纳入**；命令替换与 `eval` 已由分类器侧的解释器识别覆盖，不需要 AST 重复判一遍。
结论：接线范围比原计划小，理由是噪声实测，不是漏做。

**验证**：`BashAstEscalationTests`（新增，20 例）—— 8 条构造命令必须升级、7 条 authoring pattern 与
良性命令必须不升级、环境变量开关生效、中间件层两端断言（Safe 命令被升级 / 已判 Execution 的不受影响）。
`guard.security.tests` 1209 通过 / 0 失败。

**仍然没做到的验证**：真机 `jcc mcp_call bash` 依旧跑不起来（`SystemActuatorRegistry not initialized`，
需 `jcchub.exe` 完整启动），端到端仍只在中间件层锁。

### 2026-11-17 第二十五至二十七轮：把已知遗留清干净（按影响面排序，不另开新方向）

这一轮只做已经记录在案的遗留项，按「用户每天会撞上 → 测试可信度 → 成本 → 架构卫生」排序。
清单与动作：

| # | 已知问题 | 影响 | 处置 |
|---|---|---|---|
| 1 | `git log \| head` 等在 Auto 模式被拒 | 高频误拒 | 修判定（第二十五轮） |
| 2 | VPN 守卫测试依赖本机代理状态 | 测试噪声 | 与环境解耦（第二十六轮） |
| 3 | AST 每条命令多一次解析 | 性能未知量 | 实测后关闭（第二十七轮） |
| 4 | `BashPermissionChecker` 双份实现 | 架构卫生 | 归档（第二十七轮） |
| 5 | 真机 `mcp_call bash` 端到端 | 验证缺口 | 环境所限，见下 |

---

### 2026-11-17 第二十五轮：git 只读命令接只读管道不再需确认

第二十二轮我在测试里把 `git log | head` 从「良性管道放行」的断言中移除了，理由写成「既有口径」。
这一轮回头看，那个理由站不住 —— 它和「已登记只读命令直接放行」是自相矛盾的：

```
git log --oneline -5       → Safe              （Auto 放行）
git log --oneline -5|head  → LightValidation   （Auto 拒绝）  ← 两侧都只读，反而更严
```

根因在 `ClassifyGitPipeRedirect`：只要 git 只读子命令**出现**管道或重定向，就一律
`LightValidation`（「需确认」）。而 Auto 模式拒绝一切非 Safe，于是最常用的查看命令全被拦。

**改法**：不再「见管道就抬级」，而是看管道右侧**实际是什么**：

| 情形 | 级别 | 依据 |
|---|---|---|
| 无管道无重定向（`git status`） | Safe | 不变 |
| 管道进解释器（`git log \| sh`） | Execution | 不变，命令位口径 |
| 含重定向（`git diff > f`） | LightValidation | 会写文件 |
| 管道右侧全是已登记只读命令（`\| head` / `\| cat` / `\| grep` / `\| sort`） | **Safe** | 整体只读 |
| 管道右侧未登记（`\| tee out.txt` / `\| unknown-tool`） | LightValidation | 不知道会不会写 |

右侧的参数级风险（`| sed -i`）不在这个方法里重复判 —— `ClassifyIndirectExecution` 会取最严兜底，
这是第二十二轮就定下的分工。

**放宽的边界被单独锁住**：新增 `Auto_Mode_Rejects_GitPipeToUnknownTarget`（`| tee`、未知命令仍拒），
`ClassifyGitPipeRedirect_PipeToUnknownCommand_Should_Return_LightValidation`、
`ClassifyGitPipeRedirect_RedirectWithPipe_Should_Return_LightValidation`。
原来的 `Git_PipeToSafeCommand_Should_Return_LightValidation` 改名为 `Git_PipeToReadOnlyCommand_Should_Return_Safe`。

**验证**：`guard.security.tests` 1243 通过 / 0 失败、`hands.shell.tests` 526 通过（分类器的下游消费者）。

**一个录制过程的坑**：`dotnet test -p:BuildProjectReferences=false` 不会重编被引用的库工程，
改了 `Guard.DangerClassification` 后直接跑测试，跑的是**旧 DLL** —— 我因此看到过一次
「明明改了却仍返回旧结论」。顺序必须是「先编改动的库，再跑测试」。

---

### 2026-11-17 第二十六轮：VPN 守卫测试与环境解耦，本机有代理时不再恒红

`VpnRouteGuard_CanHandle_NoVpnActive_ReturnsFalse` 用 `new VpnRouteGuard()`，走 fallback 检测
（进程名 `clash`/`v2ray`… + `HTTP_PROXY`/`HTTPS_PROXY`）。开发机跑着代理时这条恒失败：
**断言的是机器状态，不是代码行为**。连续多轮报告里的「1 个环境相关失败」就是它 ——
一条永远红的噪音会稀释真实回归信号，这次按「测试可信度」排第二处理掉。

改法：注入 `INetworkConnectivityService` 假实现（`StubNetworkConnectivityService`，构造参数给
`vpnActive`），断言只依赖显式状态。顺手补了三条此前没有的正向用例：VPN 激活时 `git`/`gh`/`curl`
命中、`ls -la` 不命中、`Evaluate` 为 git 加上 `http.proxy` 的重写内容。

`guard.hooks.tests` **597 通过 / 0 失败**（此前 596/1）。

---

### 2026-11-17 第二十七轮：AST 代价实测（不加缓存）+ 归档双份实现

**一、先测再决定要不要缓存**

第二十四轮把「每条要放行的 bash 多一次 tree-sitter 解析」记为代价，当时写「要消掉得缓存命令指纹」。
这轮直接量（200 次/条，探针跑的是真实 `BashSecurityValidator.Validate`）：

```
walker 构造（进程内一次，单例）      26.1 ms
git log --oneline -5                0.014 ms/次
cat file.txt | grep foo | wc -l     0.062 ms/次
ls -la | sort -k5 -n | head -20     0.106 ms/次
dotnet build … --no-restore         0.021 ms/次
```

对一条要几十毫秒到几秒的 bash 来说，0.01~0.1 ms 是噪声；而 agent 的 bash 命令几乎不重复，
按整串缓存的命中率接近 0。**结论：不加缓存**，把「需要缓存」这条待办用数据关掉，
而不是加一层没有收益的共享可变状态。

**二、归档 `BashPermissionChecker`**

第二十四轮把 `IBashSecurityValidator` 接进权限链后，`BashPermissionChecker`（TS `bashPermissions.ts`
整条移植：安全校验 + 路径约束 + 只读约束 + 建议规则）就成了**同一决策的第二套实现**，
且它零生产消费者却仍被 `[Register]` 注册。按规范归档到 `.xxx/*.del`（不删除）：
实现、接口（含 `BashPermissionResult`）、14 例边界测试。

保留：`BashSecurityValidator` / `BashAstRiskEscalator`（已接线）、`CommandClassifier`（`CodeSecurityValidator` 在用）。
**遗留候选**：`PathConstraintValidator` 的唯一消费者就是被归档的 Checker，现在也是死代码；
它是独立组件且有独立测试，是否一起归档另判，本轮不动。

**三、端到端仍然锁不住（这次给了确证）**

之前只有报错 `SystemActuatorRegistry not initialized`。这轮查了本机产物：`artifacts/bin` 下
**根本没有 `jcc` / `jcchub` / host 可执行文件**（只有测试程序集），说明 host 从未在本机编译产出，
`mcp_call bash` 的端到端不可能在本机跑通。现有覆盖是两层，不是一层：
`hands.shell.tests` 的 `BashDefenseManualVerificationTests` 锁 `BashDefenseService` 节点，
`guard.security.tests` 锁权限中间件（决定放行/拦截的位置）。**不要声称端到端已验证。**

### 2026-11-17 第二十八轮：把「另一件事」做完，并且这次是启动 jcc 真机验的

上一轮我把两件事写成「另一件事」就收手了：一是归档后变成死代码的 `PathConstraintValidator`，
二是「端到端锁不住」。用户的两个反馈都成立：留着死代码等于没做完；只跑单元测试等于没验证。
这一轮两件都补上。

**一、归档路径约束判定链（`f2fd15e3c`）**

上一轮归档 `BashPermissionChecker` 后，`IPathConstraintValidator` 的唯一消费者就消失了，
整条 TS `pathValidation.ts` 移植成了死代码。一并归档（`Move-Item` 到 `.xxx/*.del`，不删除）：

| 文件 | 内容 |
|---|---|
| `PathConstraintValidator.cs` | 34 命令路径提取 + 操作类型映射 + 危险路径检查 |
| `PathExpansionDetector.cs` | 路径提取 / 命令解析 / 安全包装剥离 |
| `PathRedirectionValidator.cs` | 重定向提取与验证 |
| `PathRemovalChecker.cs` | 危险删除路径 |
| `shell/PathConstraintValidator.cs` | `IPathConstraintValidator` + `PathConstraintResult` |
| `PathConstraintValidatorRetainedDeviceTests.cs` | 33 例 |

合计 1170 行。**刻意保留**：`PathValidator`（`CommandClassifier` 在用）、`SearchScopeValidator`
（同上）、`RealPathResolver`（权限中间件在用）。保留设备名这条真实能力在生产链路上是
`RetainedDeviceNode`，`hands.shell.tests` 有独立用例 —— 归档不丢覆盖。
`guard.security.tests` 1196 通过 / 0 失败（比归档前少的 33 例就是被归档的那批）。

**二、真机验证（这次不是单元测试）**

先得把 jcc 编出来 —— 之前从没编过，`artifacts/bin` 下只有测试程序集：

```
首次    dotnet build modules/apps/cli/JoinCode.csproj -c Debug --no-restore   6m37s  0 警告 0 错误
增量（改 6 个 guard 文件后重编）                                                1m29s
```

然后 `jcc.exe mcp_serve --transport http --port 9917 --token verify`。选这条路是因为
`tools/call` 走 `IToolExecutionGateway`（13 层权限管道，ADR 0138），是**非交互能触达的最真实路径**。

**判读口径（关键）**：headless 没有确认处理器，权限管道里任何「待确认」都会走
`CliPermissionConfirmationHandler` 的非交互分支被自动拒绝，返回 `PD002 用户拒绝工具执行`。所以：

- 命中风险 → 返回**带具体原因**的 `permission_denied`（「已被阻止」「此操作被禁止」）；
- 未命中风险 → 落到 `DefaultResultMiddleware` 的「待确认」→ `PD002`。

也就是说 PD002 在这里等价于「权限链没判出风险」，**不等于「放行后执行成功」**。这个区分必须写清楚，
否则会把「没判出风险」误读成「执行通过」。

**18 条矩阵，真机全过**（脚本 `non_deliverables_tools/scripts/Verify-BashGuard.ps1`，退出码可进 CI）：

| 类别 | 命令 | 实测 |
|---|---|---|
| flag 作用域 | `ls -R` / `sort -r` / `tail -f` / `grep -rn` | NO-RISK |
| git 只读管道 | `git log --oneline -5 \| head -5` / `git diff \| cat` / `git log \| grep fix` | NO-RISK |
| 误报回归 | `cat notes.sh \| head -3` | NO-RISK |
| 间接执行 | `echo hi \| xargs rm -rf ./build` | BLOCKED（文件删除） |
| 间接执行 | `find . -name "*.cs" -exec rm -rf {} +` | BLOCKED（文件删除） |
| 间接执行 | `git log \| sh` | BLOCKED（远程执行） |
| 破坏性 | `rm -rf ./build` / `rm -rf /` / `chmod +x run.sh` | BLOCKED |
| AST 升级 | `cat /proc/self/environ` | BLOCKED（ProcEnvironAccess） |
| AST 不误伤 | `cat > /tmp/x.sh <<'EOF' … EOF` | NO-RISK |

其中 `cat /proc/self/environ` 是 AST 接线的最强证据：`cat` 本身是 Safe，分类器判不出风险，
只有 `BashAstRiskEscalator` 能拦下它 —— 真机确认这条链真的接上了，不是只接了 DI。

**三、途中查清的两个既有事实（都记下来，避免下次再猜）**

1. **serve 模式的权限管道缺工具白名单中间件**。默认白名单里的 `glob` 在 serve 模式下也是
   「待确认 → 拒绝」。所以这套端到端只能验「有没有判出风险」；要验「放行后真的执行」必须交互确认。
2. **`jcc mcp_call bash` 起不来是缺陷，已在同轮修掉（`0c2b987a9`）**。`mcp_serve` 在 `BuildHostAsync`
   之后显式补了 `ShellCapabilityInitializer.Initialize`，`mcp_call` 没有（`SkipChatServices=true`），
   于是 shell 类工具在 `mcp_call` 路径下必然报 `SystemActuatorRegistry not initialized`。
   修法是执行前按工具名（`bash` / `powershell` / `powershell_script`）补同一步初始化 ——
   `Initialize` 内部有一次性闸门，非 shell 工具不受影响。

**四、补上「真的执行」这一层（`mcp_call` 修好之后才可能）**

前面 18 条矩阵只能证明「判出了/没判出风险」。把 `mcp_call` 的执行器初始化补上之后，
才第一次拿到**真实执行**的证据（`artifacts/bin/JoinCode/Debug/net10.0/jcc.exe`）：

| 命令 | 结果 |
|---|---|
| `mcp_call bash command="echo hi_from_mcp_call"` | 执行，输出 `hi_from_mcp_call`，exit 0 / 517ms |
| `mcp_call bash command="git log --oneline -3 \| head -3"` | 执行，输出真实三条提交 |
| `mcp_call bash command="ls -R modules/guard/guard.security/shell \| head -5"` | 执行，输出真实文件清单 |
| `mcp_call bash command="echo test > nul"` | 被 BashDefense 链拦下：保留设备名 `nul` |

两条链路到这一刻才算闭合：**判定层**（serve 的 18 条：有风险就带原因拒绝、没风险就放行到待确认）
和**执行层**（mcp_call 的真跑：良性命令出真实结果、保留设备名在落盘前被拦）。

**五、踩坑**

- `Start-Process` 在本机直接抛 `Item has already been added. Key: 'HTTP_PROXY' / 'http_proxy'`
  —— PS 5.1 构造环境字典时大小写不敏感，环境里同时存在两种写法就崩。脚本因此改成
  「服务由调用方起，脚本只负责打矩阵」，不猜路径也不抢端口。

### 2026-11-17 第二十九轮：节点化现状核实 + 13 个 exe 真启动 + 首轮提示词列出 exe 清单

本轮由五个问题驱动：还有什么没完成、节点化是否完成、是否全部独立 exe、
首轮提示词有没有告诉模型这些 exe、以及「不许拿单测当挡箭牌」的真实校验。

**一、核实结果：节点化完成，但「全部独立」不是 100%**

| 口径 | 数字 | 判据 |
|---|---|---|
| 分组 / 工具总数 | 13 组 / 640 | 13 份 `node.yaml` 的 `tool_count` 之和 |
| 本 exe 内实现（local） | 217 | 各组 `private/*.cs` 实际注册表，exe 自报 id 集合逐一对账 |
| 委托独立 exe（delegate） | 173 | github 组全部走 `jcc-gh --call-tool`，不经宿主 |
| 宿主执行（host） | 250 | 会话命令 94 / system 48 / 内存索引 33 / agent 控制 30 / 连接 18 / 进程表 16 / tools 9 / web 2 |
| 进程隔离合计 | **390 / 640 = 60.9%** | 217 + 173；`Verify-ToolNodes.ps1` 声明侧与现实侧均为这个数 |

250 个 host 不是「没做完」。第十七轮已逐组给出代码证据：它们依赖活会话、内存
`CodeIndexerRegistry`、MCP 连接与进程注册表，抽成零依赖 exe 做不到等价语义。
所以转发路径是持久组件，不能为了凑 100% 把状态搬进进程再接受两份漂移实现。

**二、本机此前并没有 exe —— 已补齐真机证据**

`artifacts/bin` 下原本一个工具 exe 都没有，说明「独立」只到声明层。跑
`Pack-ToolNodes.ps1` 装配后逐个真启动：

- 13 个 exe 全部 `-h` 退出 0，最大 6.7 KB（`tools` 组），在 8 KB 上限内；
- `jcc tool tree` 真实列出 13 组 / 640 工具；
- `Verify-ToolNodes.ps1 -Sample`：自报 local 数与 id 集合均与 `node.yaml` 一致，8 次无副作用抽样真跑通过；
- 跨组 `jcc tool call` 矩阵 15/15 退出 0（含 `github gh_auth_status` 真登录态、`web web_to_markdown` 真抓 example.com、`shell bash` 真输出）；
- 补掉第十六轮留下的验证缺口：`search` 组 14 个 local 工具**逐个真跑 14/14**（`code_search`/`symbol_search`/`search_project_deps` 等），不再只是 `-h` 抽查。

顺带修掉一个会骗模型的文案 bug：slash 组 `help.txt` 首行写 `jcc-slash.exe`，
而真实 exe 叫 `jcc-tool-slash.exe`；按帮助抄命令会敲一个不存在的程序。

**三、首轮提示词：从「540+ 工具自己猜」改成「13 个 exe 带定位的清单」**

原提示词只有一句「其余 540+ 工具是独立可执行文件，用 bash 逐层发现」，
模型既不知道 exe 叫什么，也不知道该找哪一组。本轮改为数据驱动：

1. `scripts/migrate_mcp_tools_to_cli_nodes.py --index-only` 从 13 份 `node.yaml`
   生成 `resources/tools/cli_groups.yaml`：exe 名、工具数、local/delegate 数现算，
   一句话定位是唯一人工维护点（漏登记直接失败，不静默写空）；
2. 契约层新增 `CliGroupIndex` 内嵌加载器，`ToolsSection` 不再硬写任何组名/数量；
3. 提示词按 `组（exe，工具数）定位` 列 13 行，并说明四步用法：
   `<exe> -h` 看索引 → `<exe> -h <关键词>` 组内检索 → `<exe> <工具> -h` 看参数 → `jcc tool call` 执行；
4. `Verify-ToolNodes.ps1` 增加索引与 `node.yaml` 双向对账：加组忘记重新生成会在 CI 直接失败
   （这种漂移不报错、不崩，只是新组对模型永久不存在）。

**四、提示词可观测性：新增 `jcc prompt`**

改完提示词后，原有观测口都不可用：`JCC_DUMP_MESSAGES` 在 `-p` 链路因
`QueryLoopServices` 未装配 `FileContextService` 从不落盘；`/debuglog` 的 flags 经
`slash_call` 转发后没生效。两者本轮均未改动，新增可靠正门：

```
jcc prompt                       # 列出 40+ 段名称与字符数（合计 13243 字符）
jcc prompt --section tools       # 打印该段全文
jcc prompt --section all         # 打印全部
```

真机输出确认 `tools` 段列出 13 行 `jcc-tool-*`，并包含 `640 个工具 / 390 个不经过宿主`
与四步用法。**这是装配后的真实提示词**，不是 `GetContent()` 的单测返回值。

**五、仍未覆盖的边界（如实记录）**

- 模型侧答复本轮未复核：供应商密钥来自环境变量，本机 shell 中 sensenova/deepseek
  不可复现（前者未配置、后者 401），一次成功回答之后无法重复；提示词内容改由
  `jcc prompt` 真机核对，不依赖模型在线。
- 写类/交互类工具（media 桌面操作、system 通知、file 写补丁）仍只到「能跑通」，本轮未做语义回归。

**六、提交与验证**

| 项 | 结果 |
|---|---|
| `Verify-ToolNodes.ps1 -DeclarationsOnly` | 13 组 / 640 / 390 自洽，含新增索引对账 |
| `Verify-ToolNodes.ps1 -Sample` | 声明与现实一致，8 次抽样真跑 |
| 13 个 exe `-h` | 13/13 退出 0 |
| `search` 组逐工具真跑 | 14/14 |
| 跨组 `jcc tool call` | 15/15 |
| `jcc prompt --section tools` | 13 行 exe 清单，真实装配结果 |
| `Abstractions.Primitives.Tools.Tests` | 39 通过 / 0 失败 |
| `Brain.Prompts.Tests` | 215 通过 / 0 失败 |
| `Host.Tests` | 1281 通过 / 0 失败 |

提交：`3f99d2228`（提示词索引）、`bc1b26ea6`（jcc prompt）、`f613d926f`（slash 帮助文案）。

