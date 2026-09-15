# 启动参数输入/输出结构统一 — 扫描汇总与方案

> 生成时间: 2026-09-15
> 方法: 6 个 explore 子代理并行扫描,主代理汇总
> 范围: 全项目启动参数的输入结构 + 输出结构 + handler + 测试覆盖

---

## 一、扫描范围与入口点

### 1.1 入口点清单

| 入口 | 文件 | 程序集 |
|------|------|--------|
| 主入口 | `app\cli\Program.cs:8` | jcc.exe |
| TUI 入口 | `app\tui\core\Program.cs:9` | jcctui.exe |
| GUI 入口 | `app\gui\core\Program.cs:9` | jccgui.exe |
| Dream 插件 | `server\dream\Program.cs` | dream.exe |
| Bridge 远程控制 | `server\bridge\session\main\core\BridgeMainArgs.cs:67` | (内嵌) |

### 1.2 解析框架

**自研源码生成器**(非 System.CommandLine,AOT 友好):

| 生成器 | 路径 | 职责 |
|--------|------|------|
| `EnumMetadataGenerator` | `gen\enum_metadata.generator\EnumMetadataGenerator.cs` | 扫描 `[EnumValue]` → 生成 `XxxConstants` + `XxxExtensions` |
| `CliOptionGenerator` | `gen\cli_option.generator\CliOptionGenerator.cs` | 扫描 `[CliOption]` → 生成 `XxxParser` + `XxxParseResult` + `XxxSchema` + `XxxConstants` |

### 1.3 参数名唯一数据源

`lib\abstractions\abs_core\core_utils\constants\JccCliArg.cs` — `enum JccCliArg` + `[EnumValue]`,53 个枚举值(含长名+短名别名)。

---

## 二、参数完整清单

### 2.1 全局 CLI 参数(38 个,定义在 `app\cli\core\services\CliArg.cs`)

#### 分组 1: 基础(6 个)

| # | 长名 | 短名 | 帮助文本 | AcceptsValue | EnvVar | Example |
|---|------|------|----------|--------------|--------|---------|
| 1 | `--help` | `-h` | 显示帮助信息 | 否 | — | — |
| 2 | `--version` | `-v` | 显示版本信息 | 否 | — | — |
| 3 | `--pipe` | — | 命名管道通信 | 是 | — | — |
| 4 | `--prompt` | `-p` | 非交互模式提示词 | 是 | — | `jcc -p "解释这段代码"` |
| 5 | `--model` | `-m` | 指定模型 | 是 | `JCC_MODEL_ID` | `jcc -m gpt-4o -p "hello"` |
| 6 | `--vendor` | — | 切换供应商 | 是 | `JCC_VENDOR` | `jcc --vendor agnes -p "hello"` |

#### 分组 2: 输出(5 个)

| # | 长名 | 短名 | 帮助文本 | AcceptsValue | Example |
|---|------|------|----------|--------------|---------|
| 7 | `--non-interactive` | — | 强制非交互模式 | 否 | — |
| 8 | `--brief` | — | ~~简要模式(已删除,见 commit e0765d5c0)~~ | 否 | — |
| 9 | `--json` | — | 结构化 JSON 输出模式 | 否 | `jcc mcp_list --json` |
| 10 | `--format` | — | 输出格式 (text/json/ndjson) | 是 | — |
| 11 | `--quiet` | `-q` | 静默模式 | 否 | — |

#### 分组 3: 权限(9 个)

| # | 长名 | 短名 | 帮助文本 | RiskLevel | AliasOf | Example |
|---|------|------|----------|-----------|---------|---------|
| 12 | `--no-confirm` | — | 跳过所有确认提示 | — | — | — |
| 13 | `--trust` | — | 自动信任工作目录 | — | — | `jcc --trust -p "hello"` |
| 14 | `--permission-mode` | — | 权限模式 (plan/auto/ask/bypass) | write | — | — |
| 15 | `--bypass` | — | 跳过所有权限检查 | dangerous | `--permission-mode`=`bypass` | — |
| 16 | `--allowed-tools` | — | 工具白名单 | — | — | — |
| 17 | `--disallowed-tools` | — | 工具黑名单 | — | — | — |
| 18 | `--dry-run` | — | 试跑模式 | read | — | `jcc --dry-run -p "删除文件"` |
| 19 | `--yes` | `-y` | 跳过确认(等价 --no-confirm) | — | `--no-confirm` | `jcc -y -p "hello"` |
| 20 | `--force` | `-f` | 强制执行(等价 --permission-mode bypass) | dangerous | `--permission-mode`=`bypass` | — |

#### 分组 4: 诊断(3 个)

| # | 长名 | 短名 | 帮助文本 | EnvVar | Example |
|---|------|------|----------|--------|---------|
| 21 | `--force-interactive` | — | 强制交互模式 | — | — |
| 22 | `--await` | — | 超时自动关闭秒数 | — | `jcc --await 20 -p "hello"` |
| 23 | `--debuglog` | `-d` | 启用调试日志 | `JCC_DEBUGLOG` | — |

#### 分组 5: 会话(2 个)

| # | 长名 | 短名 | 帮助文本 | AcceptsValue | Example |
|---|------|------|----------|--------------|---------|
| 24 | `--continue` | `-c` | 继续最近的会话 | 否 | `jcc -c` |
| 25 | `--resume` | `-r` | 恢复指定会话 | 是 | `jcc -r abc123` |

#### 分组 6: 提示词(2 个)

| # | 长名 | 短名 | 帮助文本 | AcceptsValue |
|---|------|------|----------|--------------|
| 26 | `--system-prompt` | — | 替换系统提示词 | 是 |
| 27 | `--append-system-prompt` | — | 追加系统提示词 | 是 |

#### 分组 7: 医生(4 个)

| # | 长名 | 短名 | 帮助文本 | AcceptsValue |
|---|------|------|----------|--------------|
| 28 | `--doctor` | — | 医生模式 | 否 |
| 29 | `--doctor-server` | — | 医生服务器模式 | 否 |
| 30 | `--doctor-endpoint` | — | 医生 SSE 端点 URL | 是 |
| 31 | `--doctor-port` | — | 医生 SSE 服务器端口 | 是 |

#### 分组 8: 子命令专用(6 个)

| # | 长名 | 短名 | 帮助文本 | AcceptsValue | Example |
|---|------|------|----------|--------------|---------|
| 32 | `--args-file` | — | 从 JSON 文件读取工具参数 | 是 | `jcc mcp_call read_file --args-file args.json` |
| 33 | `--args-stdin` | — | 从 stdin 读取工具参数 | 否 | `jcc mcp_call read_file --args-stdin < args.json` |
| 34 | `--category` | — | 按分类过滤工具列表 | 是 | `jcc mcp_list --category Code` |
| 35 | `--transport` | — | MCP 服务端传输协议 | 是 | `jcc mcp_serve --transport http` |
| 36 | `--port` | — | MCP 服务端监听端口 | 是 | `jcc mcp_serve --port 9903` |
| 37 | `--host` | — | MCP 服务端监听主机 | 是 | `jcc mcp_serve --host localhost` |

### 2.2 仅在 JccCliArg 声明但未在 CliArg 暴露的参数(16 个,供 Bridge/Dream 引用)

`--sandbox`、`--no-sandbox`、`--debug-file`、`--session-timeout`、`--name`、`--spawn`、`--capacity`、`--create-session-in-dir`、`--no-create-session-in-dir`、`--session-id`、`--project`、`--input-format`、`--output-format`、`--replay-user-messages`、`--print`、`--sdk-url`

### 2.3 Bridge 子命令参数(14 个,`server\bridge\session\main\core\BridgeCliArg.cs`)

`--debuglog`、`--sandbox`、`--no-sandbox`、`--debug-file`、`--session-timeout`、`--permission-mode`、`--name`、`--spawn`、`--capacity`、`--create-session-in-dir`、`--no-create-session-in-dir`、`--session-id`、`--continue`、`--help`

互斥规则(`BridgeMainArgs.cs:102-110`):
- `--capacity` 不能与 `--spawn=session` 同时使用
- `--session-id`/`--continue` 不能与 `--spawn`/`--capacity`/`--create-session-in-dir` 同时使用
- `--session-id` 与 `--continue` 互斥

### 2.4 Dream 插件参数(3 个,`server\dream\core\services\DreamCliArg.cs`)

`--help`、`--project`、`--force`

### 2.5 CLI 子命令(19 个,`lib\abstractions\abs_core\core_utils\constants\CliSubCommand.cs`)

| 子命令 | 分类 | 废弃 | 别名 |
|--------|------|------|------|
| `tool`/`agent`/`code`/`mcp` | 已废弃 | 是 | — |
| `schema` | 自省 | — | — |
| `remote-control` | 远程控制 | — | `rc`/`remote` |
| `mcp_call`/`mcp_list`/`mcp_schema`/`mcp_search`/`mcp_serve` | MCP 工具 | — | — |
| `slash_call`/`slash_list`/`slash_schema` | 斜杠命令 | — | — |
| `doctor` | 诊断 | — | — |
| `rg` | 搜索 | — | — |
| `gh` | GitHub | — | — |

### 2.6 rg 子命令特定参数(27 长 + 16 短,硬编码 switch-case)

**不走枚举体系**,在 `app\cli\core\commands\core\RgSubCommand.cs:120-199` 硬编码:

长选项: `--help`、`--ignore-case`、`--smart-case`、`--word-regexp`、`--only-matching`、`--line-number`、`--no-line-number`、`--multiline`、`--multiline-dotall`、`--fixed-strings`、`--hidden`、`--no-ignore`、`--json`、`--count`、`--files-with-matches`、`--content`、`--replace`、`--regex-file`、`--sort`、`--type`、`--glob`、`--head-limit`、`--offset`、`--timeout`、`--before-context`、`--after-context`、`--context`

短选项: `-i`/`-S`/`-w`/`-o`/`-n`/`-U`/`-F`/`-c`/`-l`/`-A`/`-B`/`-C`/`-g`/`-t`/`-r`/`-h`

### 2.7 gh 子命令参数(动态,来自工具 schema)

`jcc gh <group> <action> [位置参数...] [--选项 值] [--json]`,参数来自 `gh_{group}_{action}` 工具的 `ToolSchema`(动态)。已知分组: `pr | issue | repo | release | run | branch | api`

---

## 三、输入结构不一致清单(5 种定义模式)

### 3.1 五种定义模式

| 模式 | 描述 | 使用位置 | 统一性 |
|------|------|----------|--------|
| **A** | `[CliOption]` 特性 + 源码生成器 | 主 CLI / Bridge / Dream | ✅ 内部统一 |
| **B** | 手写 ParseArgs + record | RgSubCommand | ❌ 独立 |
| **C** | 手写 ParseArgs + Dictionary | McpCall / SlashCall | ❌ 独立 |
| **D** | 动态 schema 绑定 | GhSubCommand | ❌ 独立 |
| **E** | 手写静态方法 | DoctorSubCommand / MockServer(5个) | ❌ 最简陋 |

### 3.2 结果对象命名不一致

| 对象 | 命名风格 | 位置 |
|------|----------|------|
| `CommandLineOptions` | `Options` 后缀 | `app\cli\core\services\CommandLineOptions.cs:6` |
| `BridgeMainArgs` | `Args` 后缀 | `server\bridge\session\main\core\BridgeMainArgs.cs:8` |
| `CliArgParseResult` | `ParseResult` 后缀 | 生成器生成 |
| `RgOptions` | `Options` 后缀 | `app\cli\core\commands\core\RgSubCommand.cs:547` |
| `GhResolvedCommand` | `Command` 后缀 | `app\cli\core\commands\core\GhCommandResolver.cs:20` |
| `GhParam` | 无后缀 | `app\cli\core\commands\core\GhCommandResolver.cs:10` |
| `Dictionary<string, JsonElement>` | 无命名 | McpCall |
| `string`(JSON) | 无命名 | SlashCall |

### 3.3 解析方法命名不一致

| 方法 | 命名 | 位置 |
|------|------|------|
| `CliArgParser.Parse` | `Parse` | 生成器生成 |
| `BridgeMainArgsParser.Parse` | `Parse` | `BridgeMainArgs.cs:63` |
| `RgSubCommand.ParseArgs` | `ParseArgs` | `RgSubCommand.cs:94` |
| `McpCliCommand.ParseArgs` | `ParseArgs` | `McpCommand.cs:305` |
| `GhCommandResolver.Resolve` | `Resolve` | `GhCommandResolver.cs:37` |
| `GhArgsBinder.Bind` | `Bind` | `GhCommandResolver.cs:184` |
| `SlashCallExecutor.ResolveArgsJsonAsync` | `ResolveArgsJsonAsync` | `SlashCommandExecutors.cs:60` |
| `CommandLineParser.ParseArgument` | `ParseArgument` | `mock_server.core\CommandLineParser.cs:15` |
| `ApplicationBuilder.ParseArgs` | `ParseArgs` | `ApplicationBuilder.cs:266` |

### 3.4 参数名引用不一致

| 来源 | 方式 | 位置 |
|------|------|------|
| 主 CLI / Bridge / Dream | `JccCliArgConstants.Xxx` | 模式 A |
| 子命令路由 | `CliArgConstants.XxxLongName` | `FlatSubCommandRouter.cs` |
| RgSubCommand | 硬编码 `"--ignore-case"` 等 | `RgSubCommand.cs:149-180` |
| DoctorSubCommand | 硬编码 `"--server"` / `"--endpoint"` / `"--port"` | `DoctorSubCommand.cs:13-15` |
| SlashListExecutor | 硬编码 `"--category"` | `SlashCommandExecutors.cs:131` |
| MockServer | 硬编码 `"--config"` / `"--port"` | 5 个 Program.cs |
| Program.cs 验证 | 硬编码 `"--await"` / `"--permission-mode"` / `"--format"` | `Program.cs:309,333,339` |

### 3.5 验证机制不一致

| 参数 | 验证方式 | 位置 |
|------|----------|------|
| 生成器参数 | 仅 `Unknown option` + `requires a value` | 生成器内置 |
| `--await` | 手写 `ValidateAwaitArg`(正整数) | `Program.cs:305-319` |
| `--permission-mode` | 手写 `ValidateEnumArgs`(枚举值) | `Program.cs:326-347` |
| `--format` | 手写 `ValidateEnumArgs`(枚举值) | `Program.cs:326-347` |
| Bridge `--spawn` | 手写 switch + 枚举转换 | `BridgeMainArgs.cs:82-90` |
| Bridge `--capacity` | 手写 `int.TryParse` + `> 0` | `BridgeMainArgs.cs:96-100` |
| Bridge 互斥规则 | 手写 4 条 if 检查 | `BridgeMainArgs.cs:102-110` |
| Rg `--timeout` | 手写 `ClampTimeout`(钳制到 30-300) | `RgSubCommand.cs:263-268` |
| Rg pattern/paths | 手写必填检查 | `RgSubCommand.cs:201-215` |
| Rg 路径安全 | 手写 `IsRootOrUnsafePath` | `RgSubCommand.cs:33-43` |
| Gh 参数绑定 | 手写 5 种错误检查 | `GhCommandResolver.cs:265-285` |
| MockServer | **无任何验证** | `CommandLineParser.cs:15-27` |

### 3.6 输入形式支持不一致

| 形式 | 主 CLI | Bridge | RgSubCommand | McpCall | GhSubCommand | MockServer |
|------|--------|--------|--------------|---------|--------------|------------|
| `--key value` | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| `--key=value` | ❌ | ❌ | ✅ | ✅ | ✅ | ✅ |
| 短参数 `-k` | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| 短参数聚类 `-abc` | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ |
| 位置参数 | ❌(除 `-p`) | ❌ | ✅ | ✅ | ✅ | ❌ |
| `key=value`(无`--`) | ❌ | ❌ | ❌ | ✅ | ❌ | ❌ |
| JSON 字符串 | ❌ | ❌ | ❌ | ✅ | ❌ | ❌ |
| `--args-file` | ✅ | ❌ | ❌ | ✅ | ❌ | ❌ |
| `--args-stdin` | ✅ | ❌ | ❌ | ✅ | ❌ | ❌ |

### 3.7 帮助文本不一致

| 命令 | 方式 | 位置 |
|------|------|------|
| 主 CLI | `CliArgParser.GetHelpText` 三级分层 | 生成器生成 |
| Bridge | `BridgeCliArgParser.GetHelpText`(委托生成器 + Replace) | `BridgeMainArgs.cs:133` |
| Dream | 手写 `PrintUsage` | `dream\Program.cs:146-167` |
| RgSubCommand | 手写 `PrintUsage` | `RgSubCommand.cs:524` 附近 |
| GhSubCommand | 手写 `Usage` 属性 | `GhCommandResolver.cs:117-137` |
| McpCall/SlashCall | 手写用法字符串 | `FlatSubCommandRouter.cs:55` / `SlashCommandExecutors.cs:14-16` |
| MockServer | **无帮助文本** | 5 个 Program.cs |

### 3.8 IsNegation / AliasOf / EnvVar 使用不一致

| 命令 | IsNegation | AliasOf | EnvVar |
|------|-----------|---------|--------|
| 主 CLI | ❌ 未用 | ✅ 4 处 | ✅ 4 处 |
| Bridge | ✅ 用(2对) | ❌ 未用 | ❌ 未用 |
| Dream | ❌ 未用 | ❌ 未用 | ❌ 未用 |
| RgSubCommand | 手写 `--no-line-number` | ❌ 无机制 | ❌ 无机制 |
| GhSubCommand | ❌ | 手写连字符→下划线宽容 | ❌ 无机制 |

### 3.9 Bridge 独有两层解析器

`BridgeCliArgParser.Parse`(生成器,语法层) → `BridgeMainArgsParser.Parse`(手写,语义层)。其他命令都是单层。位置: `server\bridge\session\main\core\BridgeMainArgs.cs:63-128`。

### 3.10 无统一基类/接口

项目中不存在统一的启动参数基类或接口。各模式各搞各的:
- 模式 A: 生成器生成的 `XxxParseResult` 之间无继承/接口关系
- 模式 B: `RgOptions` 是 `record`,不实现任何接口
- 模式 C: `Dictionary<string, JsonElement>` / `string`,无接口
- 模式 D: `GhResolvedCommand` / `GhParam` 是 `record`,不实现任何接口
- 模式 E: 无类型,直接返回原始值

---

## 四、输出结构不一致清单

### 4.1 统一基础设施(存在但未充分使用)

| 文件 | 类型 | 作用 |
|------|------|------|
| `app\cli\core\output\CliOutputEnvelope.cs:7` | `sealed class` | 信封 `{Ok, Data, Error, Meta, SchemaVersion="1"}` |
| `app\cli\core\output\CliOutputMeta.cs` | `sealed class` | 元数据 `{Version, DurationMs, NextCursor, TotalCount}` |
| `app\cli\core\output\CliStructuredError.cs:6` | `sealed class` | 错误模型 `{Code, Message, Hint, Retryable}` |
| `app\cli\core\output\CliOutputContract.cs:7` | `sealed class` | 统一写入器(WriteData/WriteError/WriteLog) |
| `app\cli\core\output\CliErrorCatalog.cs:11` | `static class` | 错误码工厂 |
| `app\cli\core\output\CliOutputJsonContext.cs:6` | `JsonSerializerContext` | AOT 兼容序列化上下文 |
| `app\cli\core\output\CliErrorCategory.cs:7` | `enum` | 5 类逻辑分组 |
| `app\cli\core\output\CliStreamEvent.cs:6` | `sealed class` | NDJSON 事件(**未接线**) |
| `lib\abstractions\abs_core\core_utils\enums\ExitCode.cs:39` | `enum` | 退出码枚举(分段预留) |

### 4.2 子命令 JSON 输出 3 套格式并存

| 格式 | 使用子命令 | 结构 |
|------|-----------|------|
| **CliOutputEnvelope 信封** `{ok,data,meta,schemaVersion}` | mcp_list, mcp_search, mcp_schema, slash_list, NonInteractiveModeRunner, Program.cs Main catch | 统一 |
| **MCP 工具结果** `{isError, content:[{type,text}]}` | mcp_call, gh | 裸 JSON |
| **裸自定义 JSON** | rg `{matches,totalMatches,fileCount}`, schema `[{name,...}]`, mcp_serve `{transport,toolCount,...}`, slash_schema `{command,schema,argumentHint}` | 各搞各的 |

### 4.3 退出码硬编码 vs 枚举混用

| 路径 | 方式 |
|------|------|
| Program.cs Main, NonInteractiveModeRunner, DoctorModeRunner, StartupSteps, FlatSubCommandRouter(未知选项) | `ExitCode` 枚举 |
| McpCommand, GhSubCommand, RgSubCommand, SlashCommandExecutors, ApplicationBuilder.RunSubCommandAsync(废弃/未知) | 硬编码 `return 0/1/2` |

**语义冲突**: `rg` 超时返回 `2`(`RgSubCommand.cs:82,90`),但 `ExitCode.2 = ConfigurationError`,含义完全不同。

### 4.4 错误输出 3 套格式并存

| 格式 | 位置 | 结构 |
|------|------|------|
| `CliOutputEnvelope.Fail` (JSON) | Program.cs:164,183,211; NonInteractiveModeRunner.cs:47 | `{ok:false, error:{code,message,hint,retryable}, schemaVersion:"1"}` |
| `CliStructuredError` + Rust 风格文本 | FlatSubCommandRouter.cs:215; McpCommand.cs:317; SlashCommandExecutors.cs:107; GhSubCommand.cs:76 | `error: ...\n  |\n  | cmdLine\n  | ^^^ msg\n  |\nhint: ...` |
| 裸 JSON 错误 | McpCommand.cs:573-581 (OutputError) | `{"isError":true,"text":"..."}` |
| `App.ErrorConsole.Warning` / `.Fatal` | Program.cs:18,168,187,218 | 彩色文本 |
| `TerminalHelper.WriteError` 纯文本 | 子命令各处 | `错误: ...` |
| `TerminalHelper.WriteLine($"错误: ...")` | NonInteractiveExecuteStep.cs:51,59; NonInteractiveModeRunner.cs:51 | 纯文本(**JSON 模式下也输出纯文本,污染 stdout**) |

### 4.5 CliOutputContract 只被 2 处使用

`CliOutputContract`(统一写入器)只在:
- `NonInteractiveModeRunner.cs:22`(创建并注入 StartupContext)
- `Program.cs:253` `WriteJsonError`(Main catch 链)

**子命令全部绕过 CliOutputContract**,直接用 `System.Console.WriteLine` / `TerminalHelper.WriteLine` / 手动 `StringBuilder` 拼接 JSON。

### 4.6 其他输出不一致

| # | 不一致点 | 详情 |
|---|----------|------|
| 1 | CliEventConsumer AX/NDJSON 模式未接线 | `--format ndjson` 验证允许但无实际效果 |
| 2 | slash_call 无 JSON 模式 | 不检查 ShouldOutputJson |
| 3 | doctor/bridge 无 JSON 模式 | 纯文本输出 |
| 4 | mcp_list/mcp_search/mcp_schema brief JSON 手动拼接 | 完整模式用工厂,brief 手动拼字符串 |
| 5 | schema 子命令输出裸数组 | 非 CliOutputEnvelope 信封 |
| 6 | NonInteractiveExecuteStep 内部错误不走 OutputContract | JSON 模式下污染 stdout |
| 7 | mcp_serve 退出报告裸 JSON | 非 CliOutputEnvelope 信封 |
| 8 | slash_schema 输出裸 schema JSON | 非 CliOutputEnvelope 信封 |
| 9 | Rust 风格错误只用文本模式 | JSON 模式下参数错误降级为纯文本或裸 JSON |

---

## 五、Handler 不一致清单(16 项)

| # | 不一致点 | 详情 |
|---|----------|------|
| 1 | 返回类型不统一 | 子命令 handler 大部分 `Task<int?>`(nullable),`BridgeMainCommand.ExecuteAsync` 和 `McpCliCommand` 内部方法返回 `Task<int>`(非 nullable) |
| 2 | 子命令参数形态不统一 | 大部分 handler 接收 `string[] args` 整体派发,`McpCliCommand` 内部方法接收已拆解的强类型参数 |
| 3 | Runner 签名不统一 | `InteractiveModeRunner.RunAsync` 返回 `Task`,`NonInteractiveModeRunner.RunAsync` 返回 `Task<int>`,`DoctorModeRunner.RunAsync` 接收 `IServiceProvider` 而非 `IHost` |
| 4 | Doctor 模式双路径 | `--doctor` 全局参数(`Program.cs:79-85`)和 `doctor` 子命令(`DoctorSubCommand.cs`)都调用 `DoctorModeRunner.RunAsync` |
| 5 | `--brief` 参数未消费 ~~(已删除,见 commit e0765d5c0)~~ | `CommandLineOptions.Brief` 字段存在,`ParseArgs` 会设置,但**未见任何中间件或服务消费此字段** |
| 6 | `--dry-run` CLI 层未消费 | `CommandLineOptions.DryRun` 字段存在,但仅 `AgentToolHandlers.cs:110` 消费,CLI 启动路径未见处理 |
| 7 | `--yes`/`--force` 别名展开依赖源码生成器 | 别名展开在生成的 `CliArgParser.Parse` 中 |
| 8 | 子命令分发三层不统一 | `remote-control`/`schema` 在 `ApplicationBuilder` 内联处理,扁平元动词走 `FlatSubCommandRouter`,已废弃命令在 `ApplicationBuilder` 返回错误 |
| 9 | `--quiet` 处理时机早于 `ParseArgs` | `Program.cs:45-48` 直接扫描 args 设置 `ErrorConsole.IsQuiet`,绕过 `CliArgParser` |
| 10 | `--await` 双重计时器 | `StartEarlyAwaitTimer`(子命令路由前)+ `StartAwaitTimer`(主路径),两个独立 Timer |
| 11 | `--permission-mode` 走环境变量间接传递 | `ParseArgs` → `ApplyEnvVars` → `JCC_PERMISSION_MODE` → `PermissionChecker.TryGetPermissionModeFromEnv` |
| 12 | `--model`/`--vendor` 走环境变量间接传递 | 同上,`ApplyEnvVars` → `JCC_MODEL_ID`/`JCC_VENDOR` → `ApplyEnvOverrides` |
| 13 | `--allowed-tools`/`--disallowed-tools` 后配置合并 | 在 `ApplicationBuilder.BuildHost` 中通过 `IOptions` 后配置合并 |
| 14 | `CancellationToken` 默认值不统一 | `BridgeMainCommand.ExecuteAsync` 的 `ct = default`,其他 handler 无默认值 |
| 15 | `McpCliCommand` 实例方法 vs 静态类 | `McpCliCommand` 是 `sealed class` 但全为 `internal static` 方法,其他 Executor 都是 `internal static class` |
| 16 | `FlatSubCommandRouter` 内部 wrapper 签名不一致 | 部分用表达式体,部分用方法体 |

### 5.1 Handler 完整映射表

| 子命令 | Handler 方法 | 文件:行 | 返回类型 |
|--------|--------------|---------|----------|
| `mcp_call` | `McpCliCommand.ExecuteCallAsync` | `app\cli\core\commands\core\McpCommand.cs:11` | `Task<int>` |
| `mcp_list` | `McpCliCommand.ExecuteListAsync` | `McpCommand.cs:36` | `Task<int>` |
| `mcp_schema` | `McpCliCommand.ExecuteSchemaAsync` | `McpCommand.cs:138` | `Task<int>` |
| `mcp_search` | `McpCliCommand.ExecuteSearchAsync` | `McpCommand.cs:89` | `Task<int>` |
| `mcp_serve` | `McpCliCommand.ExecuteServeAsync` | `McpCommand.cs:201` | `Task<int>` |
| `slash_call` | `SlashCallExecutor.ExecuteAsync` | `app\cli\core\commands\slash\SlashCommandExecutors.cs:9` | `Task<int?>` |
| `slash_list` | `SlashListExecutor.ExecuteAsync` | `SlashCommandExecutors.cs:129` | `Task<int?>` |
| `slash_schema` | `SlashSchemaExecutor.ExecuteAsync` | `SlashCommandExecutors.cs:184` | `Task<int?>` |
| `doctor` | `DoctorSubCommand.ExecuteAsync` → `DoctorModeRunner.RunAsync` | `app\cli\core\commands\core\DoctorSubCommand.cs:11` | `Task<int?>` → `Task<int>` |
| `rg` | `RgSubCommand.ExecuteAsync` | `app\cli\core\commands\core\RgSubCommand.cs:13` | `Task<int?>` |
| `gh` | `GhSubCommand.ExecuteAsync` | `app\cli\core\commands\core\GhSubCommand.cs:17` | `Task<int?>` |
| `schema` | `ApplicationBuilder.RunSubCommandAsync` 内联 | `app\cli\app\builder\ApplicationBuilder.cs:169-173` | `Task<int>` |
| `remote-control`/`rc`/`remote` | `BridgeMainCommand.ExecuteAsync` | `kit\slash\transport\BridgeMainCommand.cs:59` | `Task<int>` |
| `tool`/`agent`/`code`/`mcp` (废弃) | `ApplicationBuilder.RunSubCommandAsync` | `ApplicationBuilder.cs:184-192` | `Task<int>` |

### 5.2 错误处理模式清单

| 层级 | 模式 | 文件:行 |
|------|------|---------|
| Program.Main | try-catch 链(5 层) | `Program.cs:152-223` |
| 子命令 handler | 返回 int 退出码 | 各 handler |
| 中间件管道 | `OnError` 回调 + 短路 | `NonInteractiveModeRunner.cs:38-54` |
| StartupLoggingMiddleware | try-catch + rethrow | `StartupLoggingMiddleware.cs:15-31` |
| NonInteractiveExecuteStep | try-catch 设 ExitCode | `NonInteractiveExecuteStep.cs:19-65` |
| RgSubCommand | try-catch + 超时硬终止 | `RgSubCommand.cs:49-91` |
| DoctorModeRunner | try-catch 返回 ExitCode | `DoctorModeRunner.cs:32-63` |
| Rust 风格报错 | `CliErrorCatalog.ToRustStyleString` | `FlatSubCommandRouter.cs:215` 等 |
| JSON 模式错误 | `CliStructuredError` + `CliOutputContract.WriteError` | `Program.cs:251-257` |
| 全局异常钩子 | `AppDomain.UnhandledException` + `TaskScheduler.UnobservedTaskException` | `Program.cs:384-397` |
| 参数验证 | `ValidateAwaitArg` + `ValidateEnumArgs` | `Program.cs:305-347` |
| 密钥红线 | `ApiKeyRedLine.CheckArgsForSecrets` | `Program.cs:14` |
| 未知选项检测 | `FlatSubCommandRouter.DetectUnknownOptions` | `FlatSubCommandRouter.cs:207-218` |

---

## 六、测试覆盖缺口

### 6.1 已测试参数(19/37 有 `CliArgParser.Parse` 单元测试)

`--help`、`--version`、`--pipe`、`--prompt`、`--model`、`--trust`、`--non-interactive`、`--force-interactive`、`--await`、`--debuglog`、`--continue`、`--resume`、`--permission-mode`、`--bypass`、`--allowed-tools`、`--disallowed-tools`、`--system-prompt`、`--append-system-prompt` ~~(--brief 已删除)~~

### 6.2 未测试参数(18/37 无 `CliArgParser.Parse` 单元测试)

`--vendor`、`--no-confirm`、`--doctor-server`、`--doctor-endpoint`、`--doctor-port`、`--json`、`--format`、`--dry-run`、`--yes`、`--force`、`--quiet`、`--args-file`、`--args-stdin`、`--category`、`--transport`、`--port`、`--host`

### 6.3 测试文件清单

| 文件 | 覆盖范围 |
|------|---------|
| `test\unit\host.tests\cli\CommandLineOptionsTests.cs` | `CliArgParser.Parse` 解析(27 个 [Fact]) |
| `test\unit\host.tests\cli\FlatSubCommandRouterTests.cs` | 子命令路由 |
| `test\unit\host.tests\cli\GhCommandResolverTests.cs` | gh 子命令 |
| `test\unit\host.tests\entry\SessionResumeStepTests.cs` | `--continue`/`--resume` |
| `test\unit\host.tests\entry\SystemPromptApplyStepTests.cs` | `--system-prompt`/`--append-system-prompt` |
| `test\unit\host.tests\entry\InitDebugDumpStepTests.cs` | `JsonOutput` 字段 |
| `test\unit\host.tests\entry\NonInteractiveApiKeyCheckStepTests.cs` | API Key 检查 |
| `test\unit\host.tests\runners\DoctorModeRunnerTests.cs` | DoctorMode 字段 |
| `test\mock\mock_server.e2e.tests\e2e\*.cs` | E2E: `--trust`/`--await`/`-p`/`--force-interactive`/`--doctor` |

### 6.4 测试构造方式(3 种)

1. **直接调 `CliArgParser.Parse(args)`** — 解析层测试(`CommandLineOptionsTests.cs`)
2. **直接 `new CommandLineOptions { ... }`** — 启动步骤/Runner 测试(跳过解析)
3. **启动真实 exe 进程** — E2E 测试

---

## 七、统一方案选项

### 方案 A: 全面统一到 `[CliOption]` + CliOutputEnvelope(激进)

**输入侧**:
- RgSubCommand 27 个参数迁移到 `[CliOption]` 枚举体系
- DoctorSubCommand / SlashListExecutor 硬编码字符串改引用 `JccCliArgConstants`
- MockServer 的 `CommandLineParser` 替换为 `[CliOption]` 模式
- `Program.cs` 的 `ValidateAwaitArg`/`ValidateEnumArgs` 合并到 `[CliOption]` 验证声明
- 统一结果对象命名(`XxxOptions`)、解析方法命名(`Parse`)
- 统一支持 `--key=value` 形式(扩展生成器)

**输出侧**:
- 所有子命令 JSON 输出统一为 `CliOutputEnvelope` 信封
- mcp_call/gh 的 `{isError, content}` 包装进信封
- rg/schema/mcp_serve/slash_schema 裸 JSON 包装进信封
- 所有退出码改用 `ExitCode` 枚举(rg 超时改用新枚举值)
- 所有错误输出走 `CliOutputContract`
- 接线 `--format ndjson` 到 `CliEventConsumer(true)`
- slash_call/doctor/bridge 增加 JSON 模式

**Handler 侧**:
- 统一子命令 handler 签名为 `Task<int> ExecuteAsync(string[] args, CancellationToken ct)`
- 统一 Runner 签名
- 消除 Doctor 模式双路径
- 消除 `--dry-run` 未消费问题 ~~(--brief 已删除)~~

**优点**: 一次到位,彻底统一
**缺点**: 改动量大(约 20+ 文件),风险高,需充分测试
**工作量**: ~3-5 天

### 方案 B: 分层渐进式统一(保守)

**阶段 1(低风险)**: 输出侧统一
- 所有子命令 JSON 输出统一为 `CliOutputEnvelope` 信封
- 退出码改用 `ExitCode` 枚举
- 错误输出走 `CliOutputContract`

**阶段 2(中风险)**: 输入侧硬编码消除
- DoctorSubCommand / SlashListExecutor / Program.cs 验证 改引用 `JccCliArgConstants`
- 不改 RgSubCommand/MockServer 结构,只消除硬编码

**阶段 3(高风险)**: RgSubCommand 迁移到 `[CliOption]`
- 27 个参数迁移到枚举体系
- 短参数聚类支持扩展到生成器

**阶段 4(中风险)**: Handler 签名统一
- 统一 `Task<int> ExecuteAsync(string[] args, CancellationToken ct)`
- 消除 Doctor 双路径

**优点**: 风险可控,每阶段可独立验证提交
**缺点**: 周期长,中间状态仍有不一致
**工作量**: ~5-7 天(分 4 阶段)

### 方案 C: 仅统一输出侧(最小改动)

只做输出侧统一(方案 B 阶段 1),输入侧保持现状。

**优点**: 改动量最小,立即解决最严重的"JSON 输出 3 套格式"问题
**缺点**: 输入侧不一致保留
**工作量**: ~1-2 天

### 方案 D: 仅统一输入侧参数名引用(最小改动)

只消除硬编码字符串引用(DoctorSubCommand / SlashListExecutor / Program.cs 验证 / RgSubCommand),不改结构。

**优点**: 改动量小,消除字符串硬编码
**缺点**: 输出侧不一致保留,结构仍散乱
**工作量**: ~1 天

---

## 八、关键文件索引

### 入口与分发
- `app\cli\Program.cs` — 主入口
- `app\cli\app\builder\ApplicationBuilder.cs` — 子命令分发 + 参数解析
- `app\cli\core\commands\core\FlatSubCommandRouter.cs` — 扁平元动词路由

### 参数定义
- `lib\abstractions\abs_core\core_utils\constants\JccCliArg.cs` — 参数名唯一数据源
- `app\cli\core\services\CliArg.cs` — 主 CLI 参数定义
- `lib\abstractions\abs_core\core_utils\constants\CliSubCommand.cs` — 子命令枚举
- `server\bridge\session\main\core\BridgeCliArg.cs` — Bridge 参数
- `server\dream\core\services\DreamCliArg.cs` — Dream 参数

### 解析结果承载
- `app\cli\core\services\CommandLineOptions.cs` — 主 CLI 承载
- `server\bridge\session\main\core\BridgeMainArgs.cs` — Bridge 承载

### 输出基础设施
- `app\cli\core\output\CliOutputEnvelope.cs` — 信封
- `app\cli\core\output\CliStructuredError.cs` — 错误模型
- `app\cli\core\output\CliOutputContract.cs` — 统一写入器
- `app\cli\core\output\CliErrorCatalog.cs` — 错误码工厂
- `lib\abstractions\abs_core\core_utils\enums\ExitCode.cs` — 退出码枚举

### 子命令 Handler
- `app\cli\core\commands\core\McpCommand.cs` — mcp_call/list/schema/search/serve
- `app\cli\core\commands\slash\SlashCommandExecutors.cs` — slash_call/list/schema
- `app\cli\core\commands\core\RgSubCommand.cs` — rg
- `app\cli\core\commands\core\GhSubCommand.cs` — gh
- `app\cli\core\commands\core\DoctorSubCommand.cs` — doctor
- `kit\slash\transport\BridgeMainCommand.cs` — remote-control

### 源码生成器
- `gen\cli_option.generator\CliOptionGenerator.cs` — `[CliOption]` → Parser/Schema
- `gen\enum_metadata.generator\EnumMetadataGenerator.cs` — `[EnumValue]` → Constants/Extensions

### 测试
- `test\unit\host.tests\cli\CommandLineOptionsTests.cs` — 解析层测试
- `test\unit\host.tests\cli\FlatSubCommandRouterTests.cs` — 路由测试
- `test\mock\mock_server.e2e.tests\e2e\*.cs` — E2E 测试
