# 0076. .NET 测试和构建输出禁令与 CLI 运行时测试

- 状态：accepted
- 日期：2026-09-08
- 决策者：项目架构组

## 背景

PowerShell 管道与 `dotnet`/`Out-File`/`Select-String` 组合存在多种死锁和数据丢失陷阱。CLI 运行时测试需要统一参数格式与启动方式。`FileMode.Append` 在 .NET 5+ 行为变更需特别处理。

## 详细内容

### .NET 测试和构建输出禁令

1. **❌ 禁止使用 `Out-File` 重定向 dotnet 命令输出**
   - `dotnet test ... | Out-File "$env:TEMP\test.txt"` 是**错误**的
   - 原因: PowerShell 管道逐行传递，`Out-File` 每次写入覆盖前一行，最终文件只有最后一行
   - `Out-File -Append` 虽然不覆盖，但会丢失实时性，无法及时看到结果
    - **✅ 正确做法**: 使用 PowerShell 重定向运算符 `>` 写入日志文件
      - 编译: `dotnet build ... > .xxx/build_log.txt 2>&1`
      - 测试: `dotnet test ... > .xxx/test_log.txt 2>&1`
      - 查看结果: `Get-Content .xxx/test_log.txt -Tail 50` 或 `Select-String -Path .xxx/test_log.txt -Pattern "失败!|已通过!"`
    - **⚠️ 日志文件必须放在 `.xxx/` 目录内**: 如 `.xxx/build_log.txt`，避免被 git 追踪

2. **❌ 禁止使用 `Out-File` 保存编译错误**
   - `dotnet build ... 2>&1 | Out-File "build_error.txt"` 是**错误**的
   - 原因: `Out-File` 通过 PowerShell 管道逐行传递，存在数据丢失风险
   - **✅ 正确做法**: 使用 PowerShell 重定向 `dotnet build ... > .xxx/build_log.txt 2>&1`
   - 查看错误: `Select-String -Path .xxx/build_log.txt -Pattern "error"`

3. **❌ 禁止使用 `Select-String` 过滤 dotnet 输出**
   - `dotnet test ... 2>&1 | Select-String "失败|通过"` 会丢失上下文
   - 原因: 过滤后只剩匹配行，无法看到完整错误信息
   - **✅ 正确做法**: 直接运行，不使用管道过滤

4. **❌ 禁止使用 `Select-Object -Last` 管道连接 dotnet 命令**
   - `dotnet test ... 2>&1 | Select-Object -Last 30` 会导致**进程卡死**
   - 原因: PowerShell 管道是消费端驱动的，`Select-Object -Last N` 必须等所有行输出完才返回最后 N 行。当 dotnet test 输出量大时，PowerShell 管道缓冲区满，dotnet 进程的 stdout 写入阻塞，双方互相等待形成死锁
   - **✅ 正确做法**: 直接运行 `dotnet test/build` 命令，不使用任何管道。终端本身会显示完整输出
   - 如果输出过长，使用 RunCommand 工具的 `CheckCommandStatus` 分段读取，而非 PowerShell 管道

### CLI 运行时测试

1. **✅ 非交互模式测试** — `jcc --trust -p "提示词"` 或 `echo "提示词" | jcc --trust --non-interactive`
2. **✅ 交互式 REPL 测试** — 用 `Register-ObjectEvent` + `BeginOutputReadLine` 异步捕获 stdout，通过 `StandardInput.WriteLine` 发送命令
3. **✅ TUI 模式测试** — `jcctui --trust` 启动独立 TUI 工程（Terminal.Gui v2 全屏界面，多行输入 `Ctrl+Enter` 发送，斜杠命令转发到底层 CmdMap）
4. **⚠️ Mock 测试** — 使用 MockServer 进程提供模拟 AI 响应，通过 `JCC_ENDPOINT` 环境变量指向 MockServer

**常用 CLI 参数**：

| 参数 | 说明 |
|------|------|
| `--trust` | 信任当前目录（跳过目录信任确认） |
| `--bypass` | 跳过所有权限检查（替代旧 `--dangerously-skip-permissions`，等价 `--permission-mode bypass`） |
| `jcctui` | 启动独立 TUI 全屏界面（jcctui.exe，Terminal.Gui v2） |
| `--debuglog` / `-d` | 启用调试日志（等效 `JCC_DEBUGLOG=1`） |
| `--await <seconds>` | 非交互模式超时自动关闭（超时返回 1234） |

**扁平元动词子命令**（ADR 0069）：

| 元命令 | 用途 | 示例 |
|--------|------|------|
| `mcp_call <tool> [key=value ... \| <argsJson> \| --args-file <path> \| --args-stdin]` | MCP 工具直调（PowerShell 用 `key=value`，JSON 用 `--args-file`/`--args-stdin`）。**参数顺序不敏感**：`--trust`/`--json`/`--debuglog` 等布尔标志可放任意位置，不会吞掉 key=value 参数 | `jcc mcp_call ToolSearch query=read` / `jcc mcp_call gh_pr_checks --trust pr_number=201 --json` |
| `mcp_list [--category <cat>]` | 列出 MCP 工具 | `jcc mcp_list --category Code` |
| `mcp_schema <tool>` | 查看工具参数 schema | `jcc mcp_schema read_file` |
| `mcp_search <query>` | 搜索 MCP 工具 | `jcc mcp_search "read"` |
| `mcp_serve [--port 9903] [--transport stdio\|http] [--host H] [--await N]` | 启动 MCP 服务端（HttpListener 不可用时自动降级 TcpListener；--await N 秒后优雅退出+JSON 报告） | `jcc mcp_serve --transport http --port 9903 --await 5` |
| `slash_call <cmd> <argsJson>` | 斜杠命令直调 | `jcc slash_call compact {"level":2}` |
| `slash_list [--category <cat>]` | 列出斜杠命令（分类） | `jcc slash_list` |
| `slash_schema <cmd>` | 查看斜杠命令参数 schema | `jcc slash_schema compact` |
| `doctor [--server] [--port <n>]` | 医生模式 | `jcc doctor --server` |
| `schema` | 输出 CLI 参数定义 JSON | `jcc schema` |
| `rc` / `remote-control` | 远程控制 | `jcc rc --session-timeout 60` |
| `rg <pattern> <path> [path...]` | ripgrep 兼容搜索（**路径必填**，`RgEngine`：mmap + PLINQ 并行 + 零 GC Span 行遍历） | `jcc rg "finally\s*\{" core/ --type cs -g "!**/tests/**" -n` |

**`jcc rg` 内置 ripgrep 兼容搜索**（ADR 0070 — `RgEngine` 独立实现，mmap 零拷贝 + PLINQ 并行 + 零 GC Span 行遍历）：

```powershell
# 基本搜索（PowerShell 双反斜杠自动修复: \\s → \s）— 路径必填！
jcc rg "finally\s*\{" core/ --type cs -g "!**/tests/**" -n
# 忽略大小写 + 上下文
jcc rg "TODO|FIXME" src/ -i -n -C 2
# 字面量搜索（非正则）
jcc rg "Console.WriteLine" app/ -F --content
# 计数模式
jcc rg "class\s+\w+Service" core/ --count --type cs
# JSON 输出
jcc rg "pattern" app/ --json --head-limit 50
# 超时控制（默认 30s，最大 300s，超时返回 2）
jcc rg "pattern" src/ --timeout 60
# smart-case（模式全小写则忽略大小写，含大写则区分）
jcc rg "rgengine" app/ -S -n --content
# word-regexp（词边界匹配）
jcc rg "RgEngine" app/ -w -n --content
# only-matching（只输出匹配部分）
jcc rg "class\s+\w+" app/ -o -n --content
# replace（替换匹配部分，$1/$2 反向引用）
jcc rg "RgEngine" app/ -r "XXX" -n --content
# 按路径排序
jcc rg "pattern" core/ app/ --sort path -n --content
# 搜索隐藏文件 + 不遵守 .gitignore
jcc rg "pattern" .xxx/ --hidden --no-ignore -n --content
```

| 参数 | 说明 |
|------|------|
| `<pattern>` | 正则表达式（PowerShell `\\s` 自动修复为 `\s`） |
| `<path>` | 搜索路径（**必填！** 禁止无路径搜索，避免扫盘卡死） |
| `[path...]` | 额外搜索路径（多路径合并去重） |
| `-t, --type <type>` | 文件类型（cs, js, ts, py, go, rust, java, ...） |
| `-g, --glob <pattern>` | glob 过滤（`!` 前缀排除，如 `!**/tests/**`，**可多次指定**） |
| `-i, --ignore-case` | 忽略大小写 |
| `-S, --smart-case` | 智能大小写（模式全小写则忽略大小写，含大写则区分） |
| `-w, --word-regexp` | 词边界匹配（`\b<pattern>\b`） |
| `-n, --line-number` | 显示行号（content 模式默认开启） |
| `-A/-B/-C <n>` | 匹配行后/前/前后 n 行 |
| `-o, --only-matching` | 只输出匹配部分（非整行） |
| `-r, --replace <str>` | 替换匹配部分（支持 `$1`/`$2` 反向引用） |
| `-U, --multiline` | 多行模式（`.` 匹配换行） |
| `-F, --fixed-strings` | 字面量搜索（非正则，自动 `Regex.Escape`） |
| `--content` | 输出匹配行（`file:line:content`） |
| `--count` | 输出匹配计数 |
| `--files-with-matches` | 只输出文件名（默认） |
| `--sort <mode>` | 排序（`path`/`modified`/`none`，默认 `none`） |
| `--hidden` | 搜索隐藏文件（默认跳过 `.` 开头目录/文件） |
| `--no-ignore` | 不遵守 .gitignore（默认遵守） |
| `--head-limit <n>` | 限制结果数（默认 250，0=无限） |
| `--offset <n>` | 跳过前 n 条结果 |
| `--timeout <seconds>` | 超时秒数（默认 30，最大 300，超时**硬终止**返回 2） |
| `--json` | JSON 输出 |
| `--regex-file <path>` | 从文件读取正则（避免命令行转义问题，配合 `-U` 多行模式） |

**宽容策略（防御工程）**：
1. **PowerShell 转义自动修复**：`\\s` → `\s`、`\\{` → `\{` 等（检测双反斜杠后跟正则元字符）
2. **缺少 path 立即报错退出**（禁止无路径搜索，避免 AI 忘记输路径导致扫盘卡死）
3. **根目录（`C:\` / `/`）拒绝扫盘**
4. **超时硬终止**：默认 30s，超时返回退出码 2（不会卡死 120s）
5. **无匹配返回 1**（对齐 rg 退出码）
6. **二进制文件自动跳过，遵守 .gitignore**（`RgEngine` 内置 `IsBinary` + `.gitignore` 解析）
7. **mmap + PLINQ + 零 GC**：`RgEngine` 独立实现 — 大文件（>64KB）用 `MemoryMappedFile` 零拷贝读取，PLINQ `AsParallel().WithCancellation()` 并行每文件，Span 行遍历零分配，.NET Regex SIMD 引擎

**退出码**：`0` = 有匹配，`1` = 无匹配/参数错误，`2` = 超时

> 全局参数可在元命令前或后：`jcc --trust --model gpt-4o mcp_call read_file {"path":"x"}` 或 `jcc mcp_call read_file --trust path=x`。**布尔标志（--trust/--json/--debuglog 等）不会吞掉 key=value 参数**，参数顺序不敏感（CliArgConstants.BooleanFlags 白名单由源码生成器自动维护）。
> 旧 `jcc mcp call` 已废弃，提示用 `jcc mcp_call`。旧 `jcc tool/agent/code` 已归档。

### .NET FileMode.Append 陷阱

1. **❌ `FileMode.Append` 在 .NET 5+ 中文件不存在时抛 `FileNotFoundException`**
   - 与 .NET Framework 行为不同！旧版会自动创建文件
   - **✅ 正确做法**: 先检查文件是否存在，不存在则用 `FileMode.CreateNew` 创建空文件
   - 涉及文件: `TranscriptFileWriter`、`BridgeSubprocessManager`
2. **⚠️ `InMemoryFileSystem` 的 `ByteContent`/`TextContent` 不一致**
   - `WriteAllBytes` 设置 `ByteContent`，`AppendAllText` 修改 `TextContent`
   - `ReadAllBytes` 优先返回 `ByteContent`，如果 `ByteContent` 存在但过时，会返回旧数据
   - **✅ 正确做法**: `AppendAllText` 中如果 `ByteContent` 存在，先解码为 `TextContent` 再追加，然后清除 `ByteContent`

## 替代方案

无。这些是 PowerShell + .NET 环境下的强制约束。
