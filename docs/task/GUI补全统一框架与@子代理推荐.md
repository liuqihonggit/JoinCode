# GUI 补全统一框架与 @子代理推荐

> 调研时间：2026-09-06
> GUI 项目：`app/JoinCodeGui/`（Avalonia + CommunityToolkit.Mvvm）
> 关联 ADR：待补（统一补全框架架构决策）

## 需求背景

用户反馈：GUI 输入 `@` 时无法弹出子代理推荐列表。当前 `@` 触发的是文件补全，与发送时 `@agentName` 路由到子代理的语义冲突。同时用户希望**统一补全触发符的扩展机制**——未来加新符号（如 `$`）只在一处注册，不改 Parser/ViewModel 等多处。

## 触发符分配决策

| 触发符 | 旧 | 新 | 说明 |
|--------|----|----|------|
| `/` | 命令/参数 | 命令/参数 | 不变 |
| `@` | 文件 | **子代理** | 对齐发送路由 `@agentName message` |
| `#` | 工具 | **文件** | 释放给文件 |
| 工具补全 | `#` | **废弃** | 工具由 LLM 自动调用，`/tools` 查看列表；`!` 已能直接执行 shell |

**废弃工具补全理由**：当前 `#` 工具补全选中后仅把工具名作为普通文本插入消息发给 LLM（不直接执行），价值有限；工具由 LLM 自动调用更自然；减法思维释放触发符。

## 切割方式（tokenization）

补全 token 以**空格**为边界：
- `#file` 触发文件补全（触发符 + 前缀，无空格）
- `#file ` 空格结束该 token，不触发补全
- 选中回填后自动加空格：`#file ` → 后续 `请修改这个文档` 是普通对话文本
- 多补全项：`#file1.txt @agent1 消息` 各 token 独立

**语义**：`#file 请修改这个文档` = 文件引用 `file` + 空格分隔 + 对话指令。空格划分文件引用与对话。

## 统一框架设计

### 核心接口

```csharp
namespace JoinCode.Gui.SlashCommands;

/// <summary>补全提供者接口 — 每个触发符一个实现，统一注册到 CompletionTriggerRegistry</summary>
public interface ICompletionProvider
{
    /// <summary>触发字符（如 '/' '@' '#'）</summary>
    char TriggerChar { get; }

    /// <summary>补全模式</summary>
    SlashCompletionMode Mode { get; }

    /// <summary>模式徽章文本（如 "命令补全"、"代理补全"、"文件补全"）</summary>
    string Label { get; }

    /// <summary>获取补全候选 — prefix 为触发符后到光标的内容（不含触发符，无空格）</summary>
    IReadOnlyList<SlashCommandItem> GetCandidates(string prefix, CompletionContext context);
}

/// <summary>补全上下文 — 传给 Provider 的运行时数据</summary>
public sealed class CompletionContext
{
    public required IJccChatSession Session { get; init; }
    public IReadOnlyList<SlashCommandItem> SlashCommandCache { get; init; } = [];
    public IReadOnlyList<SubAgentSummary> AvailableSubAgentsCache { get; init; } = [];
}

/// <summary>补全触发符注册表 — 按 TriggerChar 索引，Parser 和 ViewModel 统一查询</summary>
public static class CompletionTriggerRegistry
{
    private static readonly FrozenDictionary<char, ICompletionProvider> _providers = Build();

    private static FrozenDictionary<char, ICompletionProvider> Build()
    {
        var list = new ICompletionProvider[]
        {
            new CommandCompletionProvider(),   // '/'
            new AgentCompletionProvider(),     // '@'
            new FileCompletionProvider(),      // '#'
        };
        return list.ToFrozenDictionary(p => p.TriggerChar);
    }

    public static ICompletionProvider? TryGet(char triggerChar)
        => _providers.TryGetValue(triggerChar, out var p) ? p : null;

    public static IReadOnlyCollection<ICompletionProvider> All => _providers.Values;
}
```

### 扩展点

**未来加 `$` 工具补全**：只写 `ToolCompletionProvider : ICompletionProvider`（TriggerChar='$'）+ 在 `Build()` 加一行。**零改 Parser、零改 ViewModel**。

**未来用源码生成器自动注册**：扫描 `[RegisterCompletion]` 特性自动生成 `CompletionTriggerRegistry.Generated.cs`，消除 `Build()` 手动那一行。本次不做，留作增强。

### Provider 实现清单

| Provider | TriggerChar | Mode | 数据源 |
|----------|-------------|------|--------|
| `CommandCompletionProvider` | `/` | Command/Argument | 斜杠命令缓存 + `CommandArgumentProvider` |
| `AgentCompletionProvider` | `@` | Agent | `IJccChatSession.GetAvailableSubAgentsAsync()` |
| `FileCompletionProvider` | `#` | File | `Environment.CurrentDirectory` 扫描 |

`/` 命令的 Argument 模式（`/model xxx`）特殊：在 `CommandCompletionProvider` 内部处理，或保留 Argument 特殊分支（Parser 层）。倾向后者——Argument 模式回填区间不同（替换参数区间而非触发符区间），统一进 Provider 需 Provider 暴露 `GetCompletionKind` 区分回填行为。本次保留 Argument 特殊分支，Command/Agent/File 走统一 Provider。

## @ 子代理补全数据源

- **接口**：`IAgentDefinitionProvider.GetAgentDefinitionsAsync()` 返回全部代理定义（内置 9 + 插件 + 用户.md + 项目.md）
- **GUI 门面**：`IJccChatSession` 新增 `GetAvailableSubAgentsAsync()` → `IReadOnlyList<SubAgentSummary>`
- **SubAgentSummary record**：`(string Name, string Description, string DisplayId)`
  - `Name` = `DisplayId`（如 `executor:code`、`coordinator`）
  - `Description` = `AgentDefinition.Description` 或 `WhenToUse`
- **选中回填**：插入 `@DisplayId ` 到输入框
- **发送路由**：走现有 `FindSubAgentIdByNameAsync`（运行中代理转发）；未运行则提示"未找到子代理，当前运行中: [...]"

**关于"map 储存 subAgent"**：`SubAgentSummary` 列表即该 map，由 `AgentCompletionProvider` 消费。`MainViewModel._availableSubAgentsCache` 在 `AttachRealSession` 时异步预加载（对齐 `_availableToolsCache` 模式）。

**关于"源码生成方式"**：subAgent 运行时动态加载（.md 文件），源码生成器只能覆盖编译时已知的内置 9 个，无法覆盖动态部分。本次用**统一工厂方法 `SlashCommandItem.FromAgents()`**（对齐已有 `FromMetadata()`）实现构造统一。源码生成器自动注册 Provider 留作未来增强。

## 涉及文件（9 个）

| # | 文件 | 改动 | 类型 |
|---|------|------|------|
| 1 | `app/JoinCodeGui/SlashCommands/SlashCommandParser.cs` | 枚举 `Tool`→`Agent`；遍历 Registry 找触发符；删硬编码 / @ # | 改 |
| 2 | `app/JoinCodeGui/SlashCommands/ToolCompletionProvider.cs` | 归档到 `.xxx/`（不删除） | 移 |
| 3 | `app/JoinCodeGui/SlashCommands/FileCompletionProvider.cs` | 实现 `ICompletionProvider`（TriggerChar='#'）；注释 `@`→`#` | 改 |
| 4 | `app/JoinCodeGui/SlashCommands/AgentCompletionProvider.cs` | **新增**，TriggerChar='@'，消费 SubAgentSummary | 新 |
| 5 | `app/JoinCodeGui/SlashCommands/CommandCompletionProvider.cs` | **新增**，TriggerChar='/'，封装现有命令补全 | 新 |
| 6 | `app/JoinCodeGui/SlashCommands/ICompletionProvider.cs` | **新增** 接口 + `CompletionContext` + `CompletionTriggerRegistry` | 新 |
| 7 | `app/JoinCodeGui/Hosting/IJccChatSession.cs` | 新增 `GetAvailableSubAgentsAsync()` + `SubAgentSummary` record | 改 |
| 8 | `app/JoinCodeGui/Hosting/JccChatSession.cs` | 实现，从 `IAgentDefinitionProvider` 获取 | 改 |
| 9 | `app/JoinCodeGui/Hosting/PlaceholderChatSession.cs` | 占位返回空 | 改 |
| 10 | `app/JoinCodeGui/ViewModels/MainViewModel.cs` | `_availableSubAgentsCache`/`RefreshAgentSuggestions`/`SlashModeLabel` Agent 分支/`CompleteSlashSuggestion` Agent 分支/`AttachRealSession` 预加载/`RefreshSlashSuggestions` 走 Registry | 改 |
| 11 | `app/JoinCodeGui/ViewModels/SlashCommandItem.cs` | 新增 `FromAgents()` 工厂方法 | 改 |

## 任务拆解（TDD）

> 循环：🔴红测试 → 实现 → 编译 → 🟢绿测试 → commit

### 任务 1：SubAgentSummary + IJccChatSession 接口
- 🔴 红测试：`IJccChatSession.GetAvailableSubAgentsAsync` 返回非空列表（mock session）
- 实现 `SubAgentSummary` record + 接口方法（默认返回空）
- `PlaceholderChatSession` 占位实现
- 编译 + 绿测试 + commit

### 任务 2：JccChatSession 实现
- 🔴 红测试：`JccChatSession.GetAvailableSubAgentsAsync` 从 `IAgentDefinitionProvider` 提取 DisplayId/Description
- 实现（对齐 `GetAvailableToolsAsync` 模式）
- 编译 + 绿测试 + commit

### 任务 3：统一框架接口 + Registry
- 🔴 红测试：`CompletionTriggerRegistry.TryGet('@')` 返回 AgentCompletionProvider
- 实现 `ICompletionProvider` + `CompletionContext` + `CompletionTriggerRegistry`
- 编译 + 绿测试 + commit

### 任务 4：三个 Provider 实现
- 🔴 红测试：各 Provider `GetCandidates` 返回正确候选
- `FileCompletionProvider` 改实现接口（TriggerChar='#'）
- `AgentCompletionProvider` 新增（TriggerChar='@'）
- `CommandCompletionProvider` 新增（TriggerChar='/'）
- `ToolCompletionProvider` 归档到 `.xxx/`
- 编译 + 绿测试 + commit

### 任务 5：SlashCommandParser 改造
- 🔴 红测试：`Parse("@ag", 3)` 返回 Mode=Agent；`Parse("#fi", 3)` 返回 Mode=File
- 遍历 Registry.All 找触发符；`@`→Agent、`#`→File；删 Tool 模式
- 编译 + 绿测试 + commit

### 任务 6：MainViewModel 接入
- 🔴 红测试：输入 `@` 弹出代理列表；`#` 弹出文件列表
- `_availableSubAgentsCache` + `RefreshSlashSuggestions` 走 Registry + `SlashModeLabel` + `CompleteSlashSuggestion` + `AttachRealSession` 预加载
- `SlashCommandItem.FromAgents()` 工厂
- 编译 + 绿测试 + commit

### 任务 7：GUI 冒烟验证
- 启动 GUI → 输入 `@` → 确认弹出代理列表（coordinator/executor:code 等）
- 输入 `#` → 确认弹出文件列表
- 选中 `@executor:code` → 回填 `@executor:code ` → 输入消息 → 发送 → 确认路由

## 决策记录

<!-- 🤖 Auto Decision: 2026-09-06 -->
<!-- 决策: 废弃工具独立补全，# 改为文件，@ 改为子代理 -->
<!-- 原因: 工具补全仅插入文本给 LLM 价值有限；@ 语义对齐发送路由；减法思维释放触发符 -->
<!-- 替代方案: 保留工具补全用 $ 触发（用户否决，选择废弃） -->
<!-- 验证: 待 TDD 验证 -->

<!-- 🤖 Auto Decision: 2026-09-06 -->
<!-- 决策: 统一框架用 ICompletionProvider 接口 + 手动注册 Registry，源码生成器自动注册留作未来增强 -->
<!-- 原因: subAgent 运行时动态加载，源码生成器只能覆盖内置 9 个；本次先交付统一框架，加符号只改 Provider + 注册一行 -->
<!-- 替代方案: 本次就上源码生成器扫 [RegisterCompletion] 特性（工作量过大，用户同意留作增强） -->
<!-- 验证: 待 TDD 验证 -->

<!-- 🤖 Auto Decision: 2026-09-06 -->
<!-- 决策: @ 补全显示全部代理定义（AgentDefinition 列表），非仅运行中 -->
<!-- 原因: 用户要"map 储存每个 subAgent"，指全部；选中未运行的发送时提示 -->
<!-- 替代方案: 只显示运行中（与现有 @ 路由完全一致，但不符合"map 储存每个"诉求） -->
<!-- 验证: 待 TDD 验证 -->

<!-- 🤖 Auto Decision: 2026-09-06 -->
<!-- 决策: GetCandidates 的 prefix 语义为 SlashParseResult.Prefix 原样传入（命令模式含 / 如 "/c"，代理/文件模式不含触发符如 "ag"/"src"） -->
<!-- 原因: SlashCommandParser 对 / 命令返回含 / 的 Prefix，对 @/# 返回不含触发符的 Prefix；Provider 内部处理差异，MainViewModel 原样传 -->
<!-- 替代方案: 统一 prefix 不含触发符（需改 Parser 和所有现有测试，破坏面大） -->
<!-- 验证: 8 个 CompletionTriggerRegistryTests + 8 个 MainViewModelCompletionTests 通过 ✅ -->

<!-- 🤖 Auto Decision: 2026-09-06 -->
<!-- 决策: Argument 模式保留 Parser 特殊分支，不走 Registry 统一路径 -->
<!-- 原因: Argument 回填区间不同（替换参数区间而非触发符区间），统一进 Provider 需暴露 GetCompletionKind 区分回填行为，过度抽象 -->
<!-- 替代方案: Provider 暴露 CompletionKind 区分回填（本次不做，保留特殊分支） -->
<!-- 验证: 28 个 SlashCommandParserTests + 130 MainViewModelTests 通过 ✅ -->

## 任务完成状态

| 任务 | 状态 | commit |
|------|------|--------|
| 1: SubAgentSummary + IJccChatSession 接口 | ✅ | `feat: 新增SubAgentSummary与GetAvailableSubAgentsAsync接口` |
| 2: JccChatSession 实现 | ✅ | `feat: JccChatSession实现GetAvailableSubAgentsAsync` |
| 3: 统一框架接口 + Registry + 三个 Provider | ✅ | `feat: 统一补全框架ICompletionProvider+CompletionTriggerRegistry+三个Provider实现` |
| 4: 三个 Provider 实现 | ✅ | 合并到任务 3 |
| 5: SlashCommandParser 改造 | ✅ | `refactor: SlashCommandParser遍历CompletionTriggerRegistry找触发符` |
| 6: MainViewModel 接入 Registry | ✅ | `refactor: MainViewModel补全走CompletionTriggerRegistry统一路径` |
| 7: GUI 冒烟验证 | ⏳ 待执行 | — |

## 扩展点验证

未来加 `$` 工具补全只需：
1. 新增 `ToolCompletionProvider : ICompletionProvider`（TriggerChar='$'）
2. 在 `CompletionTriggerRegistry.Build()` 加一行 `new ToolCompletionProvider()`
3. **零改 Parser、零改 ViewModel** — Registry 自动索引，Parser 自动遍历，ViewModel 自动走统一路径
