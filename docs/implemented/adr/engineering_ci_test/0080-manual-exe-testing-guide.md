# 0080. 手动测试 exe 与测试执行规则

> 📍 **导航**: [docs/](../README.md) › [adr/](README.md)
> 🔗 **上游索引**: [adr/README.md](README.md) — 修改本文档后须同步更新此索引

> ⚠️ **部分内容已陈旧** — 以下内容已被后续 commit 更新/解决：
> - 第31行 `bash运行在沙箱会抽风` 备注 → 已修复（commit e0765d5c0，CI 初始化 settings.json）
> - 第163-170行「无法交付项」表格 → 已解决（AI 限流/GitHub 清理/Anthropic 依赖均已处理）
> - 第171-189行「启动参数系统测试记录」→ BUG#1-10 已修复（commit 496289543 + e0765d5c0）
> - 核心方法论（手动测试必要性、验收标准、推荐配置）仍有效

- 状态：accepted
- 日期：2026-09-08
- 决策者：项目架构组

## 背景

单元测试不能验证整个启动链路（如 DI 卡死、JSON 宽容出错），需要通过启动参数调用工具进行实际运行（非 mock 操作）。本文档定义手动测试的准备工作、验收标准与推荐配置。

## 手动测试exe功能(非mock操作)

### 准备工作

必须要统一启动参数格式,不要有让人和AI都存在唔会情况.
其它AI在执行工具时候是无法查询这个工具的代码的,
因此你进行手动验证,通过启动参数调用工具进行实际运行,
若出现任何错误和不明确,都会是另一个AI会重复犯的错,也就是隐患.
你需要分析出原因之后,修改代码,实现更宽容应对,也就是面向笨蛋客户的调整.

通过源码生成器,捕捉命令生成map,暴露启动参数上面,
让AI手动调用bash启动并运行,例如`jcc.exe mcp_call xxArgs` 
为什么要真实手动运行?
因为单元测试不能验证整个启动链路,例如DI卡死,json宽容出错.

> bash运行在沙箱会抽风,偶尔获取的CI信息日志条数是0.

### 验收标准
在项目里面有300多个mcp函数,几十个斜杠命令.
面对这种高数量,AI会信心不足而罢工,因此要多轮次进行分解.

#### 第一轮
随机抽样手动运行,把过程中有任何不满,疑惑,都需要修改,
返回值结构化,统一,整理整齐.
你除了是项目的设计者,你还是项目的使用者,请你站在客户角度思考每个交互.

#### 第二轮

脚本测试,但是它只能验证参数是否返回,好处是快速得到坏点.

#### 第三轮

让AI写n个交接文档,
每个文档只处理少量命令,每个文档让用户开一个AI窗口处理.
每次AI只手动执行一个命令测试,和第一轮类似,遇到任何不适都需要改.
手动执行exe的启动参数进行真实运行

你可能会遇到下面几种情况:
1. 直接错误,崩溃,换一个边缘参数就蹦了.
2. 输入输出格式错误.
3. 格式对了,但是 Error:true.
4. 格式对了,是 Error:false, 但是没有逾期输出结果,空输出(必须要返回信息,即使这很冗余)
5. 超时,死锁.改用Actor管道模式(而不是处理了这边死锁,另一边死锁又出现了,查询已有代码实现)
6. 输出结果,但是没有完成对应mcp名称功能,词不达意.
7. 设定是jcc内部使用,测试bash沙箱没有能力使用,也就是跨进程无法使用,是持久化没有实现.
8. 不要用`设计限制`这种话安慰自己,用不了就是用不了!!现在已经是交付验收阶段,你用不了表示客户也用不了!!
9. 成功执行并输出有意义的结果,正面真正的成功,可以交付给客户的.

备注: 
必须要统一启动参数格式,不要有唔会情况,发生过一次唔会都需要进行修复.
其它AI在执行工具时候是无法查询这个工具的代码的,
因此你进行手动验证,通过启动参数调用工具进行实际运行,
若出现任何错误,不明确,不适,都会是另一个AI会重复犯的错,也就是隐患.
你需要分析出原因之后,修改代码,实现更宽容应对,也就是面向笨蛋客户的调整.

超过30s的功能都必须要备注,毕竟这个对于用户体感非常不好.

推荐配置（可选项）

> 默认行为是快速路径，能写三行就不引入架构；
> 效率工具是知道热路径才进行优化，按照热点优化而不是全局，除非用户明确要求。

#### [架构选型]

1. **可复用和归纳的函数、类、枚举，仅写一套**：它们非常类似也要尽可能写成一个，避免用户难以理解
2. **泛型模块化**
3. **状态机**（状态查表事件，动作转移）
4. **中间件洋葱模型**（详见下文"核心技术选型"MiddlewarePipeline）
5. **任何资源类都必须树状生长**：这不是反模式设计，这是 is-a，不是鸵鸟问题（重写 fly）。否则难以收集到容器 `map[typeName,object]`，设计资源生命周期，提供插件卸载，可以参考 deepseekHarness。后台会不断扫描这些资源健康，如果被卸载或者宿主死亡了会自动破坏
6. 统一写文件的输入管道,用Actor模型,可以随意只读(可接受脏读风险).
   通过语法分析器禁止随意写,并且推荐管道进行写入.
   需要效率时候,不受这项管控.

#### [效率]

1. **计算字符串需要 0-GC**，效率拉满，学习 1BRC 操作：多线程 + SIMD + 非托管字符数组指针直写
2. **纯异步函数来处理 IO**，高性能内存数据结构用同步函数。异步锁要自己封装 `AsyncLock`，利用 using 释放，锁内超时报错 key 名，避免同步锁定异步的情况（详见下文"AsyncLock 重入检测"）
3. **下载用多线程和断点续传**
4. **直接硬件支持的函数**，例如 CRC 等等
5. **热路径优化**
6. **无锁队列、环形队列、可排序结构、LRU 缓存结构、Everything 扫盘 + 索引**

#### [编译]

- **NativeAOT 编译，内联函数特性支持**（详见下文"关键约束"）
- 如果当前项目不是这样的，就不要改变用户已配置的，除非用户明确要求

## 全局测试执行规则

```powershell
dotnet test App.slnx -c Release /p:SkipLocalPack=true --filter "Category!=Integration"
```

1. 每个测试都加入一个限时10s，再去找到高耗时。
2. 一旦无法全局测试，出现卡死，就停下来修复全局测试，确保永远都是快速的全局测试。
3. **⚠️ 测试"卡死"排查优先级**（按成本从低到高）：
   - **先查残留 testhost**：`Get-Process -Name testhost | Stop-Process -Force` — 之前 `dotnet test` 被强杀时 testhost 子进程存活，锁住编译产物 DLL 导致后续构建报 `MSB3027 超出重试计数`，表象也是"卡死"。
   - **再查 stdout 管道锁**：`RedirectStandardOutput + ReadToEnd()` 一次性读取会因输出量大缓冲填满而死锁。正确做法：用重定向 `>` 写日志文件再查，不用管道。
   - **最后才是测试逻辑死锁**：见下方"GUI 异步测试"经验。
4. **🔍 定位未标记 Integration 的副作用测试（throw 探针法）**：
   - **场景**：某测试操作鼠标/键盘/文件系统等副作用，但没标 `[Trait("Category", "Integration")]`，导致 `--filter "Category!=Integration"` 仍会触发副作用。
   - **方法**：把可疑的生产方法函数体改成 `throw new NotImplementedException("XXX disabled for testing")`（保留方法签名，编译不报错），然后 `dotnet test` **不带 filter** 运行全部测试，哪些测试抛 `NotImplementedException` 就是哪些测试在调用该方法。
   - **示例**：把 `Win32DesktopInputService.ClickAsync` 和 `TypeTextAsync` 方法体改成 throw，跑 `dotnet test tests/Unit/Hands.Tests/Hands.Tests.csproj`，5个测试抛异常 → 这5个就是操作鼠标的测试。
   - **恢复**：定位完成后 `git checkout -- <文件>` 恢复原始代码。
   - **优势**：比 grep 搜索更准确——能找到间接调用链（如测试调用 `CompoundOperationToolHandlers.MultiClickAsync`，内部再调用 `ClickAsync`），grep 只能找到直接调用。

## 替代方案

无。手动测试是验证完整启动链路的必要手段，单元测试无法替代。throw 探针法是定位间接调用链的最准确方法，grep 无法替代。

## 测试进展记录

### 已完成修复（w3 分支，2026-09-09）

| # | Commit | 修复内容 | 根因 |
|---|--------|----------|------|
| 1 | `54ae98d01` | JSON repair 支持 PowerShell 剥引号后值含大括号 | PowerShell 调用 jcc.exe 时剥掉双引号 |
| 2 | `7c5d90133` | voice 工具 CLI 模式返回明确错误 | `voice_start_recording` 假装成功，`voice_transcribe` API Key 未配置时超时 |
| 3 | `6c952394d` | BuildPrCreateJson 多引号导致 GitHub API 解析失败 | `JsonEscapeString` 已自带引号，`BuildPrCreateJson` 又多加引号 |
| 4 | `bad25313c` | brief_mode 跨进程持久化 | 状态存储在内存中，CLI 单次调用模式进程退出后丢失。改为文件持久化 |
| 5 | `fb02b0e73` | csharp-ls LSP 服务器支持 | Encoding.UTF8 BOM 破坏管道通信 + LocationLink 反序列化 + WorkingDirectory 传递 |
| 6 | `8cbd79fb7` | lsp_workspace_symbol 显式 serverName + GitWorkspaceResolver 统一工具类 | workspace_symbol 不接受文件路径无法启动 LSP 服务器；11 处 git root 寻址重复实现 |
| 7 | `0b283419d` | 统一 10 处 git root 寻址到 GitWorkspaceResolver | 消除 11 处重复实现，B1/B2 修复 worktree 支持缺陷 |

### LSP 工具测试结果（10/10 通过）

C# LSP 服务器从 OmniSharp 切换为 csharp-ls（`dotnet tool install -g csharp-ls`，版本 0.27.0，支持 .sln + .slnx）。

| 工具名 | 结果 | 备注 |
|--------|------|------|
| `lsp_hover` | ✅ | 返回方法签名 |
| `lsp_goto_definition` | ✅ | 跳转到定义位置 |
| `lsp_find_references` | ✅ | 找到所有引用 |
| `lsp_goto_implementation` | ✅ | 跳转到实现位置 |
| `lsp_completion` | ✅ | 返回补全建议 |
| `lsp_document_symbols` | ✅ | 返回文档符号列表 |
| `lsp_prepare_call_hierarchy` | ✅ | 返回调用层次项 |
| `lsp_incoming_calls` | ✅ | 返回传入调用 |
| `lsp_outgoing_calls` | ✅ | 返回传出调用 |
| `lsp_workspace_symbol` | ✅ | 显式 serverName + 动态寻址兜底，6 种场景全通过 |

### lsp_workspace_symbol 测试详情（6 种场景）

| # | 场景 | 结果 | 备注 |
|---|------|------|------|
| 1 | .sln + 显式 serverName | ✅ | 返回 Program 符号 |
| 2 | .sln + 动态寻址（无 serverName） | ✅ | 按 .cs 扩展名自动匹配 csharp-ls |
| 3 | .slnx + 显式 serverName | ✅ | csharp-ls 0.18.0+ 支持 .slnx |
| 4 | git worktree + .sln | ✅ | GitWorkspaceResolver 正确解析 worktree .git 文件 |
| 5 | .sln + .slnx 共存 | ✅ | csharp-ls 自动选择解决方案 |
| 6 | 不存在的 serverName | ✅ | 友好错误提示，列出所有可用服务器 |

### GitWorkspaceResolver 统一工具类

提取到 `Abstractions/05-memory/FileIO/GitWorkspaceResolver.cs`，统一项目中 11 处重复的 workspace/git root 寻址实现：
- `FindGitRootAsync` — 向上搜索 git root，支持 worktree .git 文件解析到主仓库
- `FindGitWorkspaceDir` — 向上搜索 git 工作区目录（不解析 worktree 到主仓库，对应原 DiscoverWorkspaceRoot 行为）
- `FindSolutionRoot` — 向上搜索 .sln/.slnx
- `FindWorkspaceRootAsync` — 先找 .sln/.slnx，找不到 fallback 到 git root

已替换的 11 处重复实现：
- A1 `WorktreeGitRootMiddleware.FindGitRootAsync` → 委托 `GitWorkspaceResolver.FindGitRootAsync`
- A2 `AgentWorktreeService.FindGitRootAsync` → 委托 `GitWorkspaceResolver.FindGitRootAsync`
- B1 `AgentMemoryService.FindGitRoot` → `GitWorkspaceResolver.FindGitRootAsync`（修复 worktree 支持缺陷）
- B2 `SourceCodeEngine.SearchUpForGitRoot` → `GitWorkspaceResolver.FindGitRootAsync`（修复 worktree 支持缺陷）
- C1-C6 `DiscoverWorkspaceRoot` × 6 → `GitWorkspaceResolver.FindGitWorkspaceDir`
- D1 `LspService.ResolveWorkspaceRoot` → 已删除，用 `GitWorkspaceResolver.FindWorkspaceRootAsync`
- D2 `LspManager.FindWorkspaceRoot` → 已删除，用 `GitWorkspaceResolver.FindWorkspaceRootAsync`

### 关键修复：Encoding.UTF8 BOM 破坏管道通信

**根因**：`Encoding.UTF8` 默认带 BOM 前缀（EF BB BF）。通过管道写入外部进程 stdin 时，对方收到 BOM 字节后无法识别为合法输入，静默退出。
**修复**：`ProcessEncodingProvider` 改用 `new UTF8Encoding(false)`（无 BOM）。`ProcessStartInfoBuilder` 添加防御性异常 `ValidateNoBomEncoding`，禁止带 BOM 编码用于进程管道 I/O。

### 无法交付项

| 类别 | 数量 | 原因 | 解决方案 |
|------|------|------|----------|
| AI 限流 | 10 | sensenova 无 API 额度 | 切换到有额度的 AI 提供商 |
| GitHub 清理 | 4 | token 缺 `delete_repo` scope | 添加 scope 或手动删除 |
| Anthropic 依赖 | 1 | web_search 需 Anthropic API | 配置 Anthropic 提供商 |

### 启动参数系统测试（w2 分支，2026-09-12）

> 测试记录文档：[docs/task/TASK018-启动参数测试记录.md](../task/TASK018-启动参数测试记录.md)

系统测试 ~40 个启动参数，发现 10 个问题（BUG#1-10），全部已修复：

| # | Commit | 修复内容 | 根因 |
|---|--------|----------|------|
| 1 | `94df26085` | `--json` ≡ `--format json` 别名统一 | `HasFlag("--json")` 纯字符串匹配,不检查 `--format` 值 |
| 2 | `626b47b69` | `--brief` 子命令精简 JSON ~~(已删除 --brief CLI flag,见 commit e0765d5c0)~~ | 子命令路由未检查 `--brief` 标志 |

> **替代方案**：`--brief` 删除后，简洁输出通过 MCP 工具 `brief_mode`（`BriefToolHandlers`）启用/禁用。`/compact` 命令是上下文压缩（压缩对话历史），与 `--brief` 的简洁输出是不同功能，请勿混淆。

### brief_mode MCP 工具调用方式

#### 1. 启用/禁用简洁模式

**工具名**：`brief_mode`（`SystemToolName.BriefMode`）

**分类**：`mode`

**参数**：

| 参数名 | 类型 | 必填 | 说明 |
|--------|------|------|------|
| `enabled` | boolean | 是 | true 启用，false 禁用 |

**调用示例**：

```json
// 启用简洁模式
{
  "tool": "brief_mode",
  "arguments": {
    "enabled": true
  }
}

// 禁用简洁模式
{
  "tool": "brief_mode",
  "arguments": {
    "enabled": false
  }
}
```

**返回结果**：

```json
{
  "content": [
    {
      "type": "text",
      "text": "Brief mode enabled"
    }
  ],
  "isError": false
}
```

**实现位置**：`kit/mcp/dev/BriefToolHandlers.cs:BriefModeAsync`

**权限要求**：
- AutoAllowed: true（自动允许）
- PlanAllowed: false（计划模式不允许）
- AskAllowed: true（询问模式允许）

**权益检查**：启用时需要 `EntitlementService.IsBriefEntitled`，禁用始终允许。

#### 2. 查询简洁模式状态

**工具名**：`brief_status`（`SystemToolName.BriefStatus`）

**分类**：`mode`

**参数**：无

**调用示例**：

```json
{
  "tool": "brief_status",
  "arguments": {}
}
```

**返回结果**：

```json
{
  "content": [
    {
      "type": "text",
      "text": "Brief mode: enabled\n简要模式已启用 - 将使用精简输出，减少详细信息的显示\nEnabled at: 2026-09-15 09:30:00"
    }
  ],
  "isError": false
}
```

**实现位置**：`kit/mcp/dev/BriefToolHandlers.cs:BriefStatusAsync`

#### 3. 向用户发送消息（简洁模式下的回复）

**工具名**：`send_user_message`（`SystemToolName.SendUserMessage`）

**分类**：`messaging`

**参数**：

| 参数名 | 类型 | 必填 | 说明 |
|--------|------|------|------|
| `message` | string | 是 | 发送给用户的消息内容，支持 markdown 格式 |
| `attachments` | string[] | 否 | 可选的文件附件路径（图片、截图、diff、日志等） |
| `status` | string | 否 | 消息状态：'proactive' 表示主动更新，'normal' 表示回复 |

**调用示例**：

```json
{
  "tool": "send_user_message",
  "arguments": {
    "message": "任务已完成！",
    "attachments": ["/path/to/report.md"],
    "status": "normal"
  }
}
```

**实现位置**：`kit/mcp/dev/BriefToolHandlers.cs:SendUserMessageAsync`

#### 4. 斜杠命令方式

除了 MCP 工具，还可以通过斜杠命令控制简洁模式：

```
/brief          — 切换简洁模式状态
/brief enable   — 启用简洁模式
/brief disable  — 禁用简洁模式
```

**实现位置**：`kit/slash/brain/session/BriefCommand.cs`

#### 与 /compact 命令的区别

| 功能 | 工具/命令 | 作用 |
|------|----------|------|
| 简洁输出 | `brief_mode` / `/brief` | 控制输出格式，减少详细信息显示 |
| 上下文压缩 | `/compact` | 压缩对话历史，节省 token |

两者功能不同，请勿混淆。

| 3 | `626b47b69` | `--quiet` 抑制 ILogger 警告 | `--quiet` 仅存 `CommandLineOptions`,未设置 `JCC_LOG_LEVEL` |
| 4 | `c4d7f6684` | `--await` 无效值返回 exit=3 | 三处解析点静默降级,无 `ArgumentParseError` 路径 |
| 5 | `cdb6a4da2` | `--permission-mode` 无效值返回 exit=3 | 缺少早期验证 |
| 6 | `cdb6a4da2` | `--format` 无效值返回 exit=3 | 缺少早期验证 |
| 7 | `a180cd510` | 未知参数返回 exit=3 | `DetectUnknownOptions` 仅在 `ExecuteMcpCallAsync` 调用 |
| 8 | `05eee4423` | 退出码 210 修复 | `host.Dispose()` 内部调用 `Environment.Exit(210)` 覆盖返回值 |
| 9 | — | 帮助信息已清理参数自然修复 | 帮助从 `CliArg` 枚举生成,已清理参数不在枚举中 |
| 10 | `a4c426d97` | `2>nul` 全变体改写为 `/dev/null` | 仅处理 `2>nul`,未覆盖 `>nul`/`1>nul`/`&>nul`/`>>nul`/`<nul` 等 |

**统一返回结构为 JSON**（ADR 0069 决策6）：所有子命令默认输出 JSON，`--format text` 显式请求彩色文本，`--json` 作为别名（默认即 JSON）。

## TUI 交互验证（JCC_FORCE_TTY 开关）

> 追加日期：2026-10-28 — 源于 `/session` TUI 渲染错位 bug 修复（SessionPickerDialog 替代 CLI Selector）

### 背景

TUI 应用（jcc 交互模式）需要真正的 tty（stdin/stdout 非管道），bash 工具的 stdin/stdout 是管道，jcc 检测到重定向拒绝进入交互模式。`--force-interactive` 参数文档说"即使 stdin 重定向也启用 REPL"，但 `RunInteractiveAsync` 的 tty 检查 + `RawConsoleMode` + `EarlyInputBuffer` + `ReadKeyPump` 均依赖 `Console.KeyAvailable`/`Console.ReadKey`（stdin 重定向时抛 `InvalidOperationException`）。

### JCC_FORCE_TTY 开关（临时旁路，不提交生产）

设置 `JCC_FORCE_TTY=1` 环境变量旁路 tty 检查，让 jcc 在 bash 工具管道中运行 TUI 交互模式。修改点（4 处，受环境变量控制，生产环境无影响）：

| 文件 | 行号 | 旁路内容 |
|------|------|----------|
| `;`native_tui/apps/NativeTui.Host/Program.cs` | RunInteractiveAsync 入口 | tty 检查加 `JCC_FORCE_TTY != "1"` 条件 |
| `native_tui/apps/NativeTui.Host/Program.cs` | RawConsoleMode 创建 | `JCC_FORCE_TTY == "1"` 时跳过（null using） |
| `native_tui/apps/NativeTui.Host/Program.cs` | EarlyInputBuffer | `JCC_FORCE_TTY == "1"` 时跳过（空列表） |
| `native_tui/src/NativeTui.Core/repl/input/ReadKeyPump.cs` | Drain 方法 | `JCC_FORCE_TTY == "1"` 时用 `Console.In.Peek/Read` 替代 `KeyAvailable/ReadKey` |

### ReadKeyPump forceTty 路径键映射

`Console.In.Read()` 返回 int 字符码，需映射到 `ConsoleKeyInfo`。关键：**Enter 的 KeyChar 必须用 `\0`**（不能用 `\n`），否则 `ConsoleKeyAdapter.ToKeyPress` 第 26 行控制字符拦截（`\n`=10 在 `\u0001`-`\u001a` 范围）会映射为 Ctrl+j 而非 Return。

```csharp
info = ch switch {
    '\r' or '\n' => new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false),
    '\u001b' => new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false),
    '\u0010' => new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false),   // Ctrl+P → Up
    '\u000e' => new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false), // Ctrl+N → Down
    _ => new ConsoleKeyInfo(ch, ConsoleKey.None, false, false, false),
};
```

### 管道输入时序问题

命令异步执行（在引擎回合线程），对话框挂载有延迟。管道输入的所有字符一次性到达 stdin，`ReadKeyPump.Drain` 逐字符快速处理，方向键可能在对话框挂载前到达，被 slash picker 处理（`sessPk=False suggest=True`）。

**解法**：用 `sleep` 分段输入模拟真实终端时序（用户看到对话框后再按方向键）：

```bash
{ printf '/session\n'; sleep 3; printf '\x0e\x0e\x10'; } | timeout 20 dotnet jcc.dll --force-interactive --await 15
```

### TUI 对话框交互验收清单

验证 TUI 原生对话框（如 SessionPickerDialog）必须覆盖以下交互：

| 验收项 | 验证方法 | 通过标准 |
|--------|----------|----------|
| **渲染** | 管道输入触发命令，捕获 stdout | 标题 + 列表项（焦点指示符 `❯` + 内容）+ 溢出行 `… 及 N 项` + footer 快捷键行 |
| **方向键焦点移动** | `sleep` 分段输入 `\x0e`（Down）/`\x10`（Up），调试输出看 `selected` 变化 | `MoveFocus` 正确（Down+1, Up-1, clamp 不环绕），滚动窗口跟随 |
| **Enter 提交** | 输入 `\n`，看对话框卸载 | `IsClosed=true`，对话框从滚动区移除，触发选择回调（TCS 完成） |
| **Esc 取消** | 输入 `\x1b`，看对话框卸载 | `IsClosed=true`，对话框移除，TUI 回到正常状态（spinner 恢复） |
| **无错位** | 对比 bug 前后渲染 | 命令输出不与 slash picker/输入框重叠（如 `❯ /se=== 历史会话 ===` 不再出现） |

### 运行命令模板

```bash
# 编译（workdir: D:\project\w4）
dotnet build modules/apps/cli/JoinCode.csproj -c Debug --no-incremental --nologo

# 运行 TUI 交互（workdir: jcc.dll 所在目录）
export JCC_FORCE_TTY=1
printf '/session\n' | timeout 15 dotnet jcc.dll --force-interactive --await 10 > output.txt 2>&1

# 分段输入（方向键交互，sleep 等对话框挂载）
{ printf '/session\n'; sleep 3; printf '\x0e\x0e\x10'; } | timeout 20 dotnet jcc.dll --force-interactive --await 15 > output.txt 2>&1
```

> ⚠️ `dotnet jcc.dll` 对 workdir 敏感，须在 jcc.dll 所在目录运行（`artifacts\bin\JoinCode\Debug\net10.0\`），不能串联 build && run（build 在项目根，run 在 jcc.dll 目录）。

## 真实链路验证方法集（2026-10-31 补充）

### 1. 子代理 transcript 恢复验证

**场景**: Esc/关机/网络中断后，用户说"恢复所有子代理运行"，从 transcript 加载历史重新 spawn。

**步骤**:

```bash
# 1. 构造测试数据 — 3 个子代理（已完成/被中断/一半历史）
python3 -c "
import json, os
base = 'C:/Users/54076/.jcc/sessions/test-resume-all-20261031/subagents'
os.makedirs(base, exist_ok=True)
agents = [
    ('agent-completed', 'completed', '已完成的子代理', ['系统提示', '用户任务', '助手完成结果']),
    ('agent-interrupted', 'running', '被中断的子代理', ['系统提示', '用户任务', '助手开始执行', '工具调用']),
    ('agent-half', 'running', '一半历史的子代理', ['系统提示', '用户任务']),
]
for agent_id, status, desc, entries in agents:
    agent_dir = os.path.join(base, agent_id)
    os.makedirs(agent_dir, exist_ok=True)
    meta = {'agentId': agent_id, 'role': 0, 'agentType': 'executor', 'description': desc, 'createdAt': '2026-10-31T00:00:00Z', 'status': status}
    with open(os.path.join(agent_dir, 'meta.json'), 'w', encoding='utf-8') as f:
        json.dump(meta, f, ensure_ascii=False, indent=2)
    with open(os.path.join(agent_dir, 'transcript.jsonl'), 'w', encoding='utf-8') as f:
        for i, content in enumerate(entries):
            role = 'user' if i == 1 else ('system' if i == 0 else 'assistant')
            entry = {'sessionId': 'test-resume-all-20261031', 'role': role, 'content': content, 'timestamp': f'2026-10-31T00:0{i}:00Z', 'promptTokens': 0, 'completionTokens': 0, 'agentId': agent_id, 'isSidechain': True}
            f.write(json.dumps(entry, ensure_ascii=False) + '\n')
"

# 2. 用 slash_call 调 /agents resume-all，指定 --resume session ID
echo -n "resume-all" > /tmp/agents-args.txt
jcc.exe slash_call /agents --resume test-resume-all-20261031 --args-file /tmp/agents-args.txt

# 期望输出:
# 正在从 transcript 恢复所有被中断的子代理...
# 已从 transcript 恢复 2 个子代理:
#   ● agent-xxx — Resume: 一半历史的子代理
#   ● agent-yyy — Resume: 被中断的子代理
# （agent-completed 被跳过）
```

**坑**:
- `slash_call` 传 JSON 参数给 `ChatCommandContext.Arguments`，但 `/agents` 用 `GetSplitArgs` 解析空格分隔参数 → 用 `--args-file` 传非 JSON 参数
- `ChatCommandContext.SessionId` 默认是 `string.Empty`（不是 null）→ `??` 运算符不触发 fallback → 必须用 `string.IsNullOrEmpty` 检查
- `FlatSubCommandRouter.GetOptionValue` 精确匹配 → 需传 `--resume`（含前缀），不是 `resume`
- transcript 存储路径: `C:\Users\54076\.jcc\sessions\{sessionId}\subagents\{agentId}\` — 不是 `AppData\Local\JoinCode`
- `meta.json` 字段名是 camelCase（`agentId`/`status`/`createdAt`），`status` 完成时写 `"completed"`（小写，`ToValue()`），不是 `"Completed"`（`ToString()`）

### 2. 超时杀进程验证

**场景**: 命令执行超时后，`AbsoluteTimeoutMiddleware` 通过 `SessionToolMap.KillByCommand` 杀死进程。

**步骤**:

```bash
# 用纯 bash 循环（约 3s），设 1s 超时，禁用 auto_background
jcc.exe --bypass mcp_call bash command='for i in $(seq 1 999999); do true; done' timeout=1000 auto_background=false

# 期望输出:
# exit_code=-1（被杀）, duration_ms≈1100（1s 后被杀）, isError=true
```

**坑**:
- `ping -n 30` / `sleep` / `timeout` 等含等待关键字 → `ShellTimeoutKeywordMiddleware` 拦截 → 用纯 CPU 循环 `for i in $(seq 1 N); do true; done`
- `> nul` 重定向 → `nul` 保留设备名拦截 → 用 `do true` 或 `do rem` 不重定向
- `cmd /c` → Path violation 拦截 → 用纯 bash 命令
- `auto_background=true`（默认）→ 超时自动转后台不杀 → 必须设 `auto_background=false`
- bash 工具自身有 30s 超时 → 循环不能太长，`seq 1 999999` 约 3s 足够

### 3. 默认超时值验证

**场景**: 验证 bash 工具 `timeout` 参数默认值是 180000ms（3 分钟）。

**步骤**:

```bash
# 查看 bash 工具 schema
jcc.exe mcp_schema bash

# 检查 timeout 参数:
# "timeout": {"type":"integer","description":"Timeout in milliseconds, default 180000ms (3 minutes)","default":"180000"}
```

**坑**:
- `mcp_schema` 输出是 JSON → 用 `python3 -c "import sys,json; ..."` 解析
- schema 中 `default` 是字符串 `"180000"`，不是整数

## MockServer 聊天室式对话验证（2026-11-01 补充）

### 背景

需要 AI 亲自测试 jcc 的完整对话链路（非 mock），但不依赖真实 LLM。通过 MockServer Interactive 模式 + mock_ai_responder 控制台程序，实现两种测试方式：

1. **自己和自己对话** — AI 用 bash 调 `jcc -p "消息"`，responder 自动匹配意图并 inject 响应，AI 同时扮演用户和 AI
2. **留言板式对话** — 用户用 `jcc -p "留言"` 留言，AI 用 `jcc -p "回复"` 回复，通过 transcript 持久化 + `-c` 恢复实现跨进程对话

### 架构

```
jcc -p "你好"  →  MockServer (端口 8090)  →  挂起等注入 (Interactive 模式)
                        ↑                          ↓
                   responder 轮询 requests  ←  控制面 /__control__/*
                        ↓                          ↓
                   意图匹配 + inject 响应  →  唤醒挂起请求  →  jcc 显示响应
```

### MockServer Interactive 模式挂起机制

`KestrelMockServer` Interactive 模式：无预注入时请求挂起 30s 等注入（`WaitForInjectionAsync` + `_waiters` 队列），`Inject` 优先唤醒等待者。超时兜底走脚本响应。

- 控制端点和 LLM 端点在同一端口（`/__control__/*` vs `/v1/chat/completions`），jcc 走的 HTTP 路径和真实环境一致
- `MockServerController.Inject` 优先唤醒 `_waiters` 队列中的挂起请求，无等待者时存入预注入队列

### mock_ai_responder 控制台程序

`test/mock/mock_ai_responder/` — 轮询 MockServer 控制面 `requests` 端点，意图匹配后 `inject` 响应：

| 意图 | 匹配关键词 | 响应 |
|------|-----------|------|
| 问候 | 你好/hello/hi | 自我介绍 + 引导 |
| 写代码 | 写/实现/函数/方法 | 代码示例 |
| 解释 | 解释/什么是/为什么 | 解释性回复 |
| 工具 | 工具/mcp/command | 工具列表 |
| 默认 | * | 通用回复 |

支持 OpenAI 流式（SSE `data: {...}`）+ 非流式 + Anthropic 响应格式。

### 启动命令模板

```bash
# 1. 编译（workdir: D:\project\w1）
dotnet build test/mock/open_ai.mock_server/OpenAI.MockServer.csproj -c Debug --no-restore
dotnet build test/mock/mock_ai_responder/MockAiResponder.csproj -c Debug --no-restore
dotnet build modules/apps/cli/JoinCode.csproj -c Debug --no-restore

# 2. 启动 MockServer + responder 后台
dotnet run --no-build --project test/mock/open_ai.mock_server -- --port 8090 > .xxx/logs/mockserver.log 2>&1 &
dotnet run --no-build --project test/mock/mock_ai_responder -- --mock-url http://localhost:8090 > .xxx/logs/responder.log 2>&1 &

# 3. jcc 对话（自己和自己对话）
JCC="dotnet artifacts/bin/JoinCode/Debug/net10.0/jcc.dll"
export JCC_VENDOR=openai JCC_ENDPOINT=http://localhost:8090/v1 OPENAI_API_KEY=fake-key JCC_HTTP_MODE=Real
$JCC -p "你好" --await 20

# 4. 留言板式对话（用户留言后 AI 回复）
$JCC -p "用户留言内容" --await 20    # 用户留言
$JCC -c -p "AI 回复内容" --await 20   # AI 恢复会话后回复
```

### 验收清单

| 验收项 | 验证方法 | 通过标准 |
|--------|----------|----------|
| **jcc → MockServer 链路** | `jcc -p "你好"`，看响应 | 非空响应，responder 日志显示匹配意图 |
| **transcript 写入** | `jcc -p "你好"` 后查 `~/.jcc/sessions/*/transcript.jsonl` | 包含 user + assistant 条目（非空） |
| **-c 恢复有内容会话** | `jcc -c`，看"N 条消息" | N > 0（恢复有内容会话，非空会话） |
| **流式响应** | jcc 默认 stream=true，看 SSE 分块 | 响应完整拼接，无格式错误 |
| **500 错误处理** | inject 500 响应，看 jcc 输出 | 错误提示清晰，非 AggregateException 混乱 |

### 已发现并修复的 bug

| # | Commit | 修复内容 | 根因 |
|---|--------|----------|------|
| 1 | `fix: CliSession.InitializeAsync 改用 EnsureSessionAsync` | `-p` 创建的会话 `-c` 能找到 | `SaveSessionInfoAsync` 只写 meta.json 不建 transcript.jsonl，`ListTranscriptsAsync` 跳过无 transcript 文件的目录 |
| 2 | `fix: mainAgent 路径补 transcript 增量持久化` | `-p` 对话后 transcript.jsonl 有内容 | mainAgent 路径绕过 ChatService 管道，`TranscriptPersistMiddleware` 从未被调用，`PostProcessMainAgentAsync` 漏了 transcript 写入 |
| 3 | `fix: -c 跳过空会话恢复有内容会话` | `-c` 恢复有内容会话而非空会话 | `FindMostRecentSessionIdAsync` 按修改时间排序选了刚创建的空会话（MessageCount=0），需跳过空会话 |

### 坑

- **jcc 环境变量**：API 端点用 `JCC_ENDPOINT`（不是 `JCC_API_BASE_URL` 或 `OPENAI_BASE_URL`），供应商用 `JCC_VENDOR=openai`
- **jcc 流式优先**：jcc 默认发 `stream=true` 请求，流式失败后降级非流式
- **预注入残留竞态**：预注入残留响应会被新请求消费，导致格式不匹配错误。启动前清空 inject 队列
- **responder exe 锁定**：旧 responder 进程未杀时 build 报 `MSB3027 超出重试计数`，需 `taskkill //F //IM mock_ai_responder.exe` 后重新 build
- **transcript 过滤**：`-c` 恢复时只加载 `role == "user" || role == "assistant"` 的条目（line 58），system 消息被过滤
