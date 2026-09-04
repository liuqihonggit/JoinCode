# 0066. 前置感叹号命令（! 触发 AI / !! 不触发 AI）

- 状态：proposed
- 日期：2026-09-05
- 决策者：AI + 用户确认

## 背景

当前项目仅支持 `/` 斜杠命令路由（`CliSession.ProcessUserInputAsync` 和 `MainViewModel.SendAsync` 中 `input.StartsWith('/')` 判断），没有 `!` 或 `!!` 前缀命令实现。项目中存在 `FooterBashModeHint`（`! bash mode`）本地化字符串，但无任何代码使用它，属于预留未实现状态。

用户要求对齐 PI（pi.dev Coding Agent）的前置感叹号命令设计：

| 工具 | `!command` | `!!command` |
|------|-----------|-------------|
| **PI (pi.dev)** | 执行 shell 命令，输出**发送**给模型（触发 AI） | 执行 shell 命令，**不发送**输出给模型（不触发 AI） |
| **ClaudeCode** | Shell mode，输出加到 session，Claude 响应 | 无此设计 |
| **当前项目** | ❌ 未实现 | ❌ 未实现 |

用户需求：
1. `!command` — 执行 shell 命令，输出注入对话上下文，触发 AI 流式响应（对齐 PI/ClaudeCode）
2. `!!command` — **不触发 AI**，智能识别 target 类型：
   - 文件路径 → 用系统默认程序打开
   - URL → 用系统默认浏览器打开
   - 其他 → 当 shell 命令静默执行，输出仅回显
3. 用特性标注（类似 `[ChatCommand]`），对齐 PI 设计
4. 修改 CLI 和 GUI 输入路由

## 决策

采用**前缀命令路由器 + 特性标注**设计：新建 `[PrefixCommand]` 特性 + `IPrefixCommandHandler` 接口，实现两个处理器（`!` 触发 AI、`!!` 不触发 AI），在 CLI/GUI 输入路由中新增前缀判断分支。

### 1. 前缀命令特性

**位置**：`foundation/Abstractions/03-hands/Shell/PrefixCommandAttribute.cs`

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class PrefixCommandAttribute : Attribute
{
    public required string Prefix { get; init; }      // "!" 或 "!!"
    public string Description { get; init; } = "";
    public bool TriggersAi { get; init; }             // ! = true, !! = false
}
```

源码生成器未来可扫描 `[PrefixCommand]` 自动注册处理器（当前硬编码两个，满足开心路径）。

### 2. 前缀命令接口

**位置**：`foundation/Abstractions/03-hands/Shell/IPrefixCommandHandler.cs`

```csharp
public interface IPrefixCommandHandler
{
    string Prefix { get; }
    bool TriggersAi { get; }
    Task<PrefixCommandResult> ExecuteAsync(string command, PrefixCommandContext context, CancellationToken ct);
}

public sealed record PrefixCommandResult(bool Handled, string Output, bool ShouldInjectToAi);

public sealed class PrefixCommandContext
{
    public required IServiceProvider Services { get; init; }
    public required CancellationToken CancellationToken { get; init; }
    public string? WorkingDirectory { get; init; }
}
```

### 3. 两个处理器

| 处理器 | 特性 | 行为 |
|--------|------|------|
| `ShellPrefixCommandHandler` | `[PrefixCommand(Prefix="!", TriggersAi=true)]` | 执行 shell 命令 → 返回输出 → 注入 AI 上下文 → 触发流式响应 |
| `SilentShellPrefixCommandHandler` | `[PrefixCommand(Prefix="!!", TriggersAi=false)]` | 智能识别 target → 打开/静默执行 → 输出回显 → 不触发 AI |

**位置**：`app/JoinCode/Cli/Commands/Prefix/`

### 4. !! 智能识别逻辑（SilentShellPrefixCommandHandler）

```
target = input[2..].Trim()

if target 是 URL（http:// 或 https:// 前缀）
    → Process.Start(UseShellExecute=true) 用系统默认浏览器打开
    → 返回 "已用浏览器打开: {url}"

else if target 是已存在文件路径（File.Exists）
    → Process.Start(UseShellExecute=true) 用系统默认程序打开
    → 返回 "已打开文件: {path}"

else if target 是已存在目录路径（Directory.Exists）
    → Process.Start("explorer.exe", path) 用文件管理器打开
    → 返回 "已打开目录: {path}"

else
    → 当 shell 命令静默执行（Process.Start 捕获 stdout/stderr）
    → 返回命令输出（仅回显，不注入 AI）
```

**跨平台**：Windows 用 `UseShellExecute=true`；Linux/macOS 用 `xdg-open`/`open`。当前项目目标为 Windows（win32），先实现 Windows，跨平台后续补充。

### 5. ! 命令执行逻辑（ShellPrefixCommandHandler）

```
command = input[1..].Trim()

1. 用 Process.Start 执行 shell 命令（cmd /c on Windows），捕获 stdout+stderr
2. 设置超时（默认 30s，可配置）
3. 返回输出
4. 调用方将输出注入 AI 上下文，触发 StreamResponseAsync
```

**注入 AI 的格式**：
```
$ {command}
{output}

（以上为 `!command` 执行结果，请分析）
```

### 6. 前缀命令路由器

**位置**：`app/JoinCode/Cli/Commands/Prefix/PrefixCommandRouter.cs`

```csharp
public static class PrefixCommandRouter
{
    /// <summary>判断输入是否为前缀命令（! 或 !!）</summary>
    public static bool IsPrefixCommand(string input);

    /// <summary>解析前缀命令，返回 (前缀, 命令内容)</summary>
    public static (string prefix, string command)? Parse(string input);

    /// <summary>执行前缀命令</summary>
    public static Task<PrefixCommandResult> ExecuteAsync(string input, PrefixCommandContext context, CancellationToken ct);
}
```

**解析优先级**：先判 `!!`（双感叹号），再判 `!`（单感叹号），避免 `!!` 被 `!` 误匹配。

### 7. CLI 路由修改

**文件**：`app/JoinCode/Cli/Core/CliSession.cs` → `ProcessUserInputAsync`

```
现有：
    if (input.StartsWith('/'))
        await HandleCommandAsync(input, ct);
    else
        await StreamResponseAsync(input, ct);

改为：
    if (input.StartsWith('/'))
        await HandleCommandAsync(input, ct);
    else if (PrefixCommandRouter.IsPrefixCommand(input))
        await HandlePrefixCommandAsync(input, ct);   // 新增
    else
        await StreamResponseAsync(input, ct);
```

`HandlePrefixCommandAsync` 内部：
- `!!` → 执行后输出回显到终端，不触发 AI
- `!` → 执行后将输出作为消息注入 `StreamResponseAsync`

### 8. GUI 路由修改

**文件**：`app/JoinCodeGui/ViewModels/MainViewModel.cs` → `SendAsync`

```
现有：
    if (message[0] == '@') → HandleMentionAsync
    if (message.StartsWith('/')) → ExecuteSlashCommandAsync
    else → 聊天流

新增（在 / 判断前）：
    if (PrefixCommandRouter.IsPrefixCommand(message))
        → HandlePrefixCommandAsync
        → !! 输出回显到 Messages（System 消息），不触发 AI
        → ! 输出注入聊天流，触发 AI
```

### 9. Shell 执行方式

**决策**：直接用 `System.Diagnostics.Process` 执行，不复用 `ShellToolHandlers` 的 MCP 中间件管道。

**理由**：
- `!`/`!!` 是用户主动输入的命令，已授权，无需经过 MCP 工具管道的权限检查/分类/拦截
- 开心路径：`Process.Start` 简单直接，不依赖 DI 容器和中间件管道
- `ShellToolHandlers` 是 AI 调用工具的链路（有中间件管道开销），不适合用户直输入场景
- 复用现有先例：`MainViewModel` 已有 `Process.Start(UseShellExecute=true)` 打开 explorer 的代码

**安全考虑**：
- 设置命令超时（默认 30s），避免挂起
- 捕获 stdout+stderr，限制输出长度（默认 100KB，防止巨量输出撑爆上下文）
- 不启用 `dangerously_disable_sandbox`，因为是用户主动输入

### 10. 本地化

启用已有的 `FooterBashModeHint` 字符串，在 CLI 底部状态栏显示 `! bash 模式` 提示。新增 `FooterSilentBashModeHint`（`!! 静默执行`）。

## 替代方案

- **复用 ShellToolHandlers MCP 管道执行**：经过安全检查和中间件管道，但开销大、依赖 DI，且 `!`/`!!` 是用户主动输入已授权，无需重复检查。未采用。
- **仅实现 `!!` 不实现 `!`**：用户明确要求同时实现两者对齐 PI 完整设计。未采用。
- **`!!` 仅做 shell 静默执行不做智能识别**：用户明确要求"智能识别 + 回退到 shell"（文件→打开、URL→浏览器、其他→命令）。未采用。
- **用斜杠命令 `/!` 和 `/!!` 实现**：破坏前缀命令的直觉语义，且与 PI/ClaudeCode 设计不一致。未采用。
- **新建源码生成器扫描 `[PrefixCommand]` 自动注册**：当前只有两个处理器，硬编码即可，过度工程。未来处理器增多时再引入生成器。未采用（保留为未来增强）。

## 后果

- 正面：对齐 PI/ClaudeCode 前置感叹号命令设计，用户可快速执行 shell 命令（`!`）或静默打开/执行（`!!`）而无需进入斜杠命令。`!!` 智能识别文件/URL/命令，提升 UX。特性标注为未来扩展（新前缀）预留架构。
- 负面：新增 `IPrefixCommandHandler` 接口 + `[PrefixCommand]` 特性 + 两个处理器 + 路由器，代码量增加。`!` 命令绕过 MCP 安全管道，依赖"用户主动输入已授权"假设。
- 中性：CLI 和 GUI 输入路由各新增一个分支，不影响现有 `/` 斜杠命令和聊天流。`FooterBashModeHint` 本地化字符串从预留转为启用。

## 反向引用

- AGENTS.md「规则7 文件驱动界面」— `!!` 智能识别不硬编码，由文件系统/URL 协议驱动
- AGENTS.md「交付优先级」— 先实现 Windows 开心路径，跨平台后续补充
- AGENTS.md「开心路径」— `!`/`!!` 直接用 `Process.Start`，不经过 MCP 中间件管道
- AGENTS.md「枚举 + [EnumValue] 使用规范」— `[PrefixCommand]` 特性对齐 `[ChatCommand]` 模式

## 调查来源

- PI (pi.dev) 文档：https://pi.dev/docs/latest/usage
  - `!command` runs and sends output to the model
  - `!!command` runs without sending output to the model
- ClaudeCode 文档：https://docs.claude.com/en/docs/claude-code/interactive-mode
  - `!` at start = Shell mode（执行命令，输出加到 session，Claude 响应）
  - 无 `!!` 双感叹号设计
