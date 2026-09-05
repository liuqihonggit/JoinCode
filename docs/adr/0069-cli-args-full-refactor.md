# 0069. 启动参数完全重构 — 扁平元动词 + 统一解析框架 + 斜杠命令直调

- 状态：proposed
- 日期：2026-09-06
- 决策者：项目架构组
- 取代：0065（jcc mcp 子命令设计，`jcc mcp call` → `jcc mcp_call`）

## 背景

当前启动参数设计存在 6 个核心痛点：

1. **两套解析框架并存**：主 CLI/Bridge/Dream 用自定义 `CliOptionGenerator` 源码生成器，子命令（tool/agent/code/mcp）用 `System.CommandLine` 库。两套框架的 Option/Command 定义方式、帮助生成、错误处理风格完全不同。
2. **参数定义分散在 4 个枚举 + 1 个结果类**：`JccCliArg`（参数名常量）+ `CliArg`（元数据）+ `CommandLineOptions`（结果字段）重复声明，新增参数需改两个文件，映射在 ParseArgs 中手动逐字段赋值。
3. **MCP 工具调用入口 4 条，语义混淆**：`jcc mcp call`（调内部工具）、`/mcp`（管理服务器）、`mcp_call_tool`（调外部服务器）、LLM 自动调用。用户难以区分。
4. **斜杠命令不能在启动参数直调**：`-p "/command"` 会被当作普通提示词发给 LLM，而非执行斜杠命令。约 100 个斜杠命令只能在 REPL 内使用。
5. **空壳子命令 + doctor 污染全局参数**：`tool`/`agent`/`code` 子命令是空壳（exec/run/analyze/search 提示"请在交互模式下调用"）；4 个 doctor 参数占据全局参数空间。
6. **等价参数 + 环境变量映射手动处理**：`--bypass`≡`--permission-mode bypass`、`--yes`≡`--no-confirm` 等在 ParseArgs 中用 if 语句逐个处理；CLI→环境变量映射手动 `Environment.SetEnvironmentVariable`，无声明式表达。

## 决策

### 决策1：子命令风格 — 下划线元动词

所有子命令改为扁平 `<域>_<动作>` 元动词，不用嵌套子命令：

| 元命令 | 用途 | 示例 |
|--------|------|------|
| `mcp_call <tool> <argsJson>` | MCP 工具直调 | `jcc mcp_call read_file '{"path":"x"}'` |
| `mcp_list [--category <cat>]` | 列出 MCP 工具 | `jcc mcp_list --category Code` |
| `mcp_schema <tool>` | 查看工具参数 schema | `jcc mcp_schema read_file` |
| `mcp_search <query>` | 搜索 MCP 工具 | `jcc mcp_search "read"` |
| `mcp_serve [--port 9903]` | 启动 MCP 服务端 | `jcc mcp_serve --transport http` |
| `slash_call <cmd> <argsJson>` | 斜杠命令直调 | `jcc slash_call compact '{"level":2}'` |
| `slash_list [--category <cat>]` | 列出斜杠命令（分类） | `jcc slash_list` |
| `slash_schema <cmd>` | 查看斜杠命令参数 schema | `jcc slash_schema compact` |
| `doctor [--server] [--port <n>]` | 医生模式 | `jcc doctor --server` |
| `schema` | 输出 CLI 参数定义 JSON | `jcc schema` |
| `rc` / `remote-control` | 远程控制 | `jcc rc --session-timeout 60` |

**全局参数可在元命令前**：`jcc --trust --model gpt-4o mcp_call read_file '{"path":"x"}'`

**argsJson 传递格式**：位置参数为主（简洁、AI 友好），大参数降级 `--args-file <path>` 或 `--args-stdin`，无参数命令可省略。

### 决策2：解析框架 — 统一到 CliOptionGenerator

废弃 `System.CommandLine`，全部统一到自定义 `CliOptionGenerator` 源码生成器：
- 已支持枚举 + `[CliOption]` + 帮助生成 + schema 自省 + NativeAOT 友好
- 扁平元动词不需要嵌套子命令，生成器完全胜任
- 消灭双框架，一套帮助/schema 机制
- 编译时生成，AOT 零反射

### 决策3：斜杠命令直调 — 全量暴露 + 分类储存

- 所有斜杠命令都能 `slash_call`，不设 `[DirectCallable]` 筛选标记
- `slash_list` 按分类分组显示全部命令
- 不适合直调的命令（`/exit` `/theme`）调用时自行处理
- 理由：用户需要全量暴露用于自动化测试场景

### 决策4：参数表声明 — [ChatCommandArg] 特性 + 生成器收集

斜杠命令参数 schema 声明方式：
- 在命令类上加 `[ChatCommandArg("name", Type="string", Description="...")]` 特性
- 源码生成器扫描收集，生成 `SlashCommandSchemaCatalog`（类似 `GeneratedSlashCommandCatalog`）
- `slash_schema <cmd>` 输出 `ToolSchema` JSON，和 `mcp_schema` 统一格式
- **渐进式**：未声明 `[ChatCommandArg]` 的命令降级输出 `ArgumentHint`，不阻塞重构
- 复用现有 `ToolSchema` + `ToolSchemaProperty`（JSON Schema 子集）

### 决策5：清理动作（6 项全部执行）

| 清理项 | 动作 |
|--------|------|
| 空壳子命令 tool/agent/code | 移到 `.xxx/` 归档，功能由 mcp_call/slash_call 覆盖 |
| doctor 4 参数 | 从全局 CliArg 移除，改为 `jcc doctor [--server] [--endpoint <url>] [--port <n>]` |
| 5 个"内部"废弃参数 | Print/SdkUrl/InputFormat/OutputFormat/ReplayUserMessages 移到 `.xxx/` |
| 等价参数 | 从 ParseArgs 手动 if 改为 CliOptionGenerator 声明式别名表 |
| CLI→环境变量映射 | 从手动 SetEnvironmentVariable 改为 `[CliOption(EnvVar="JCC_XXX")]` 特性声明 |
| 废弃斜杠命令 | /passes（→/permissions）、/output-style（→/config）移到 `.xxx/` |

### 决策6：等价参数声明式别名表

| 参数 | 等价于 | 声明方式 |
|------|--------|----------|
| `--bypass` | `--permission-mode bypass` | `[CliOption(AliasOf = CliArg.PermissionMode, AliasValue = "bypass")]` |
| `--yes` / `-y` | `--no-confirm` | `[CliOption(AliasOf = CliArg.NoConfirm)]` |
| `--force` | `--bypass` | `[CliOption(AliasOf = CliArg.Bypass)]` |
| `--json` | `--format json` | `[CliOption(AliasOf = CliArg.Format, AliasValue = "json")]` |

生成器在解析时自动展开别名，消除 ParseArgs 中的手动 if 链。

## 替代方案

### 子命令风格替代方案

1. **分组子命令 + call 动词**（`jcc mcp call <tool>`）：放弃。用户明确选择下划线元动词风格，更接近直觉。
2. **最简扁平位置参数**（`jcc mcp <tool>`）：放弃。保留字冲突（`mcp serve` 中 serve 既是子命令又是工具名），需特殊处理。

### 解析框架替代方案

1. **保留双框架，仅重组参数面**：放弃。双框架并存是核心痛点，不解决则重构不彻底。
2. **推倒重来，重新设计声明机制**：放弃。风险高，现有 schema 自省能力需重建，且 CliOptionGenerator 已满足需求。

### 斜杠命令直调范围替代方案

1. **标记筛选 [DirectCallable]**：放弃。用户明确要全量暴露用于测试，分类储存即可。
2. **全量暴露 + 标注可直调/仅交互**：放弃。标注是建议非约束，约束力弱，用户选择分类储存更简洁。

### 参数表声明替代方案

1. **[ChatCommand] 上加 ArgsSchema JSON 字符串**：放弃。手写 JSON 字符串易出错、可读性差、无编译时校验。
2. **独立 schema 声明类**：放弃。每个命令两个类，文件膨胀，分散维护。
3. **不声明 schema，仅用 ArgumentHint 降级**：放弃。AI 拿不到结构化参数表，只能猜。

## 后果

- 正面：
  - 启动参数格式统一，使用者和 AI 不会误会
  - 一套解析框架，一套帮助/schema 机制
  - MCP 工具和斜杠命令都能从启动参数直调，自动化测试友好
  - 参数表有结构化声明，AI 可通过 `slash_schema`/`mcp_schema` 获取参数契约
  - 全局参数空间精简（doctor 移出、内部参数清理、空壳删除）
  - 等价参数和环境变量映射声明式，消除手动 if 链
- 负面：
  - 所有子命令需重写（从 System.CommandLine 迁移到 CliOptionGenerator）
  - 斜杠命令参数 schema 需逐步补充（先建机制，后补数据）
  - ADR 0065 被 superseded
- 中性：
  - 新增 `[ChatCommandArg]` 特性 + 生成器扩展
  - CliOptionGenerator 需扩展支持别名表和 EnvVar 声明
  - CliSubCommand 枚举需重构（mcp_call/slash_call/doctor 等新值）

## 渐进式执行顺序

1. 写 ADR + 任务清单（本文档）
2. 建机制：`[ChatCommandArg]` 特性 + 生成器扩展 + `SlashCommandSchemaCatalog`
3. 重组子命令为扁平元动词（mcp_call/slash_call 等）
4. 统一到 CliOptionGenerator，废弃 System.CommandLine
5. 清理空壳/废弃/内部参数
6. doctor 提升为子命令
7. 等价参数声明式别名表
8. CLI→环境变量映射声明式特性
9. 逐步给高频命令补 `[ChatCommandArg]` schema
