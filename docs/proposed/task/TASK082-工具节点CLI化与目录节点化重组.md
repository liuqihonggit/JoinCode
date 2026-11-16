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
| 9 | 全部消费方切换后归档生成器项目到 `.xxx/` + 修复引用 + 目录节点化重组 | `modules/generators/mcp_tool_dispatch.generator/`、16 域 slnx | ⬜ | ⬜ | ⬜ |

> **2026-11-16 阻断说明（步骤 9 前提不成立）**：`McpToolDispatch` 生成器产出的进程内 handler 注册表**正是转发兜底的依赖**。实测无 Hub 时 `jcc mcp_call` 直接报「Hub 服务不可达」，`JCC_ALLOW_DIRECT_FALLBACK=1` 下 `jcc mcp_list` 返回 `total_count: 0`。因此该生成器在「转发路径退役」之前不能归档；步骤 9 拆成两步：先逐组做实到不再需要转发，再归档生成器。
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
