# #17 插件 Agent 安全限制 — 方案设计

> **创建时间**: 2026-08-17
> **对齐目标**: claude code `src/utils/plugins/loadPluginAgents.ts`
> **状态**: 待用户审阅

---

## 一、目的：为什么要做这个？

### 1.1 claude code 的设计

claude code 的插件可以贡献 agent 定义（从插件目录加载 `.md` 文件）。但出于**安装时信任边界**考虑，插件 agent **不能**定义以下三个字段：

| 禁止字段 | 原因 |
|----------|------|
| `permissionMode` | 插件不能静默改变权限模式（可能绕过安全审查） |
| `hooks` | 插件不能注入钩子（可能在任意事件点执行任意命令） |
| `mcpServers` | 插件不能声明 MCP 服务器（可能引入未审查的外部进程） |

用户手动写的 agent 定义（`~/.claude/agents/`）可以定义这些字段，因为用户自己信任自己。但插件是**第三方安装**的，用户不一定审查了每个 agent 文件，所以必须限制。

### 1.2 我们当前的状况

- **有插件体系**：`IWorkflowPlugin` + `PluginManager`，插件能贡献 DI 服务、命令、Hooks、Skills
- **无插件 agent**：插件**不能**贡献 agent 定义，`AgentDefinitionProvider` 只从内置 + 用户目录 + 项目目录加载
- **因此**：安全限制也无从谈起 — 没有"插件 agent"这个概念

### 1.3 做这个的价值

- **中价值** — 安全防护：如果未来有插件 agent，防止插件越权
- **前提条件**：需先建立"插件贡献 agent 定义"的加载路径

---

## 二、用户提出的设计：`Map<name, 插件类>`

### 2.1 设计思路

用户建议用 `Map<string, IPluginAgentProvider>`（或类似字典结构），插件通过实现接口注册自己能提供的 agent 定义。

```
插件加载时:
  PluginManager 遍历所有已加载插件
  → 检查插件是否实现 IPluginAgentProvider
  → 调用 GetAgentDefinitions() 获取 agent 定义列表
  → 注册到 Map<agentName, AgentDefinition>
  → 加载时校验安全限制
```

### 2.2 优点

- **清晰的责任划分**：插件显式声明"我提供哪些 agent"，而非外部推送
- **按 name 索引**：O(1) 查找，支持覆盖（同名插件 agent 后注册覆盖先注册）
- **插件自治**：插件自己管理 agent 定义的生命周期，卸载时自然移除
- **与现有 `IWorkflowPlugin` 一致**：插件通过实现接口贡献能力（命令、Hooks、Skills 都是这种模式）

### 2.3 与我之前设计的对比

| 维度 | 我之前的设计 (`AddPluginDefinition`) | 用户建议的设计 (`Map<name, 插件类>`) |
|------|--------------------------------------|--------------------------------------|
| **调用方向** | 外部推送（PluginManager 调用 AddPluginDefinition） | 插件主动声明（实现 IPluginAgentProvider） |
| **数据结构** | `List<AgentDefinition>` | `Map<string, AgentDefinition>` |
| **查找效率** | O(n) 遍历 | O(1) 按名查找 |
| **覆盖语义** | 不明确（靠 Deduplicate） | 明确（Map 同 key 覆盖） |
| **与现有模式一致性** | 不一致（现有都是插件实现接口） | 一致（和 ICommandRegistrationHook 等同模式） |
| **插件卸载** | 需外部调 Remove | 插件卸载时 Map 自动移除 |

**结论**：用户的设计更好 — 与现有插件体系一致，查找效率更高，覆盖语义更明确。

---

## 三、采用用户设计后的详细方案

### 3.1 新增接口：`IPluginAgentProvider`

```csharp
// foundation/Abstractions/03-hands/Skill/IPluginAgentProvider.cs
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 插件 Agent 定义提供者 — 插件实现此接口以贡献 agent 定义
/// 对齐 claude code loadPluginAgents: 从插件加载 agent 定义 + 安全限制
/// </summary>
public interface IPluginAgentProvider
{
    /// <summary>获取插件提供的 agent 定义列表</summary>
    IReadOnlyList<AgentDefinition> GetAgentDefinitions();
}
```

### 3.2 新增校验器：`PluginAgentValidator`

```csharp
// core/ai/Agents/src/Services/Support/PluginAgentValidator.cs
namespace Core.Agents;

/// <summary>
/// 插件 Agent 安全限制校验 — 对齐 claude code 安装时信任边界
/// 插件 agent 不能定义 permissionMode/hooks/mcpServers
/// </summary>
public static class PluginAgentValidator
{
    /// <summary>校验插件 agent 定义，违反安全限制则抛异常</summary>
    public static void Validate(AgentDefinition definition)
    {
        // permissionMode 非空 → 拒绝
        // hooks 非空 → 拒绝
        // mcpServers 非空 → 拒绝
    }

    /// <summary>批量校验，返回所有违规项（不抛异常）</summary>
    public static IReadOnlyList<string> ValidateAll(IReadOnlyList<AgentDefinition> definitions)
    {
        // 遍历，收集违规消息，返回列表
    }
}
```

### 3.3 新增加载器：`PluginAgentLoader`

```csharp
// core/ai/Agents/src/Services/Support/PluginAgentLoader.cs
namespace Core.Agents;

/// <summary>
/// 从已加载插件收集 agent 定义 — 维护 Map<name, AgentDefinition>
/// 对齐 claude code loadPluginAgents
/// </summary>
[Register(typeof(IPluginAgentLoader))]
public sealed class PluginAgentLoader : IPluginAgentLoader
{
    private FrozenDictionary<string, AgentDefinition> _pluginAgents = FrozenDictionary<string, AgentDefinition>.Empty;

    /// <summary>从插件列表加载所有 agent 定义，校验安全限制</summary>
    public void LoadFromPlugins(IReadOnlyList<IPluginAgentProvider> providers)
    {
        var map = new Dictionary<string, AgentDefinition>();
        foreach (var provider in providers)
        {
            var definitions = provider.GetAgentDefinitions();
            foreach (var def in definitions)
            {
                PluginAgentValidator.Validate(def);  // 校验安全限制
                map[def.DisplayId] = def;  // 同名覆盖
            }
        }
        _pluginAgents = map.ToFrozenDictionary();
    }

    /// <summary>获取所有插件 agent 定义</summary>
    public IReadOnlyList<AgentDefinition> GetAll() => _pluginAgents.Values.ToList();

    /// <summary>按名查找</summary>
    public AgentDefinition? Find(string name) =>
        _pluginAgents.TryGetValue(name, out var def) ? def : null;
}
```

### 3.4 `AgentDefinitionProvider` 接入

```csharp
// 在 GetAgentDefinitionsAsync 中合并插件 agent
var definitions = new List<AgentDefinition>();
definitions.AddRange(GetBuiltInDefinitions());
definitions.AddRange(_pluginAgentLoader.GetAll());  // 新增：合并插件 agent
// ... 继续加载用户定义和项目定义
```

### 3.5 `PluginManager` 接入

```csharp
// 在插件加载完成后，收集所有 IPluginAgentProvider 实现
public void OnPluginLoaded(IWorkflowPlugin plugin)
{
    if (plugin is IPluginAgentProvider agentProvider)
    {
        _pluginAgentProviders.Add(agentProvider);
        _pluginAgentLoader.LoadFromPlugins(_pluginAgentProviders);
    }
}
```

### 3.6 `DreamPlugin` 示例（如果 Dream 要贡献 agent）

```csharp
public sealed class DreamPlugin : IWorkflowPlugin, IPluginAgentProvider
{
    public IReadOnlyList<AgentDefinition> GetAgentDefinitions()
    {
        return [
            new AgentDefinition
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Doctor,
                WhenToUse = "自举修复",
                SystemPrompt = "...",
                // 注意：不能设置 PermissionMode/Hooks/McpServers
            }
        ];
    }
}
```

---

## 四、文件清单

| 文件 | 操作 | 说明 |
|------|------|------|
| `foundation/Abstractions/03-hands/Skill/IPluginAgentProvider.cs` | 新增 | 插件 agent 提供者接口 |
| `core/ai/Agents/src/Services/Support/PluginAgentValidator.cs` | 新增 | 安全限制校验器 |
| `core/ai/Agents/src/Services/Support/PluginAgentLoader.cs` | 新增 | Map<name, AgentDefinition> 加载器 |
| `core/ai/Agents/src/Services/Support/AgentDefinitionProvider.cs` | 修改 | 合并插件 agent |
| `infrastructure/Infrastructure/Plugins/Services/PluginManager.cs` | 修改 | 加载后收集 IPluginAgentProvider |
| 测试文件 | 新增 | 校验器 + 加载器测试 |

---

## 五、安全限制校验规则

| 字段 | 插件 agent | 用户 agent | 原因 |
|------|-----------|-----------|------|
| `PermissionMode` | ❌ 禁止 | ✅ 允许 | 插件不能静默改变权限模式 |
| `Hooks` | ❌ 禁止 | ✅ 允许 | 插件不能注入任意钩子 |
| `McpServers` | ❌ 禁止 | ✅ 允许 | 插件不能引入未审查的外部进程 |
| `Tools` | ✅ 允许 | ✅ 允许 | 工具列表是安全的（受全局白名单约束） |
| `SystemPrompt` | ✅ 允许 | ✅ 允许 | 提示词是 agent 的核心功能 |
| `ModelName` | ✅ 允许 | ✅ 允许 | 模型选择不影响安全 |
| `Skills` | ✅ 允许 | ✅ 允许 | 技能本身有自己的安全审查 |

---

## 六、渐进式实现步骤

1. **新增 `IPluginAgentProvider` 接口** → 编译 Abstractions
2. **新增 `PluginAgentValidator`** + 测试 → 校验安全限制
3. **新增 `PluginAgentLoader`** + 测试 → Map<name, AgentDefinition>
4. **`AgentDefinitionProvider` 接入** → 合并插件 agent
5. **`PluginManager` 接入** → 加载后收集 IPluginAgentProvider
6. **端到端测试** → 模拟插件贡献 agent + 安全校验

---

## 七、融入 Cordis 框架思想（DeepSeek 插件论文）

> **参考**: Cordis 框架 — 支持"时空可组合性"的插件系统
> **核心**: 可逆效应（Revertible Effects）+ 响应式协效应（Reactive Coeffects）

### 7.1 可逆效应 — 插件卸载时自动移除 agent 定义

Cordis 的核心思想：**每个修改上下文的操作必须附带逆操作**，插件卸载时自动执行逆操作恢复状态。

融入我们的方案：`PluginAgentLoader` 不只负责加载，还要负责**卸载** — 插件卸载时自动从 `Map<name, AgentDefinition>` 中移除该插件贡献的 agent。

```csharp
public sealed class PluginAgentLoader : IPluginAgentLoader
{
    // Map<agentName, (AgentDefinition, pluginName)> — 追踪每个 agent 来自哪个插件
    private FrozenDictionary<string, (AgentDefinition Def, string PluginName)> _pluginAgents = ...;

    /// <summary>加载插件 agent — 返回撤销函数（可逆效应）</summary>
    public Action LoadFromPlugin(string pluginName, IPluginAgentProvider provider)
    {
        var addedKeys = new List<string>();
        var map = new Dictionary<string, (AgentDefinition, string)>(_pluginAgents);
        foreach (var def in provider.GetAgentDefinitions())
        {
            PluginAgentValidator.Validate(def);
            map[def.DisplayId] = (def, pluginName);
            addedKeys.Add(def.DisplayId);
        }
        _pluginAgents = map.ToFrozenDictionary();
        NotifyChanged();  // 响应式协效应：通知 AgentDefinitionProvider 缓存失效

        // 返回逆操作 — 插件卸载时调用
        return () =>
        {
            var unloadMap = new Dictionary<string, (AgentDefinition, string)>(_pluginAgents);
            foreach (var key in addedKeys)
                unloadMap.Remove(key);
            _pluginAgents = unloadMap.ToFrozenDictionary();
            NotifyChanged();
        };
    }
}
```

**关键点**：
- `LoadFromPlugin` 返回 `Action`（撤销函数），而非 void — 这是 Cordis 的"可逆效应"模式
- 撤销函数由 `PluginManager` 在卸载插件时调用，自动移除该插件贡献的 agent
- 不需要"清空所有再重新加载" — 精确移除受影响的条目

### 7.2 响应式协效应 — 依赖变化时主动通知

Cordis 的"响应式协效应"：当依赖的服务出现/消失/变化时，框架主动通知消费方。

融入我们的方案：`PluginAgentLoader` 维护一个 `event EventHandler Changed`，当插件 agent 加载/卸载时触发。`AgentDefinitionProvider` 订阅此事件，自动失效缓存。

```csharp
public sealed class PluginAgentLoader : IPluginAgentLoader
{
    /// <summary>插件 agent 集合变化事件 — 响应式协效应</summary>
    public event EventHandler? Changed;

    private void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

// AgentDefinitionProvider 构造函数中订阅:
_pluginAgentLoader.Changed += (_, _) => ClearCache();
```

**关键点**：
- `AgentDefinitionProvider` 不需要主动轮询插件状态 — 被动接收通知
- 缓存失效是自动的，不会出现"插件已卸载但缓存还引用旧 agent"的陈旧数据

### 7.3 效应分类 — 安全限制的本质

Cordis 将操作分类为不同"效应"。我们可以把安全限制理解为**效应分类**：

| 效应类型 | 说明 | 插件 agent 能产生？ |
|----------|------|---------------------|
| **安全效应** | 工具调用、提示词、模型选择 — 在框架安全边界内 | ✅ 允许 |
| **特权效应** | 权限变更（permissionMode）、钩子注入（hooks）、外部进程引入（mcpServers） | ❌ 禁止 |

`PluginAgentValidator` 本质上是一个**效应分类器** — 检查插件 agent 是否只产生安全效应，不产生特权效应。

### 7.4 作用域隔离 — 插件 agent 的可见性

Cordis 的"作用域隔离"：子 Fiber 能看父 Fiber 注册的服务，反之不行。

融入我们的方案：插件 agent 的可见性遵循**加载顺序优先级**：

```
内置 agent (始终可见)
  ← 插件 agent (插件加载后可见，卸载后不可见)
    ← 用户 agent (用户= ~/.jcc/agents/)
      ← 项目 agent (= .jcc/agents/)
```

插件卸载时，其贡献的 agent 变为不可见 — 这就是 Cordis 的"时间可组合性"：卸载不需要重启，agent 定义自动消失。

### 7.5 声明式依赖 — 插件不猜测环境

Cordis 要求插件声明 `inject` 依赖，不猜测环境。我们的 `IPluginAgentProvider` 已经遵循这个原则：

```csharp
// 插件声明"我提供这些 agent"，不关心谁在消费
public interface IPluginAgentProvider
{
    IReadOnlyList<AgentDefinition> GetAgentDefinitions();
}
```

插件不直接调用 `AgentDefinitionProvider.Add(...)`（推送模式），而是声明自己能提供什么，由框架决定如何消费 — 这是 Cordis 的"声明依赖 → 框架自动管理生命周期"主线。

---

## 八、更新后的文件清单

| 文件 | 操作 | 说明 |
|------|------|------|
| `foundation/Abstractions/03-hands/Skill/IPluginAgentProvider.cs` | 新增 | 插件 agent 提供者接口 |
| `foundation/Abstractions/07-agents/Agent/IPluginAgentLoader.cs` | 新增 | 加载器接口（含 Changed 事件） |
| `core/ai/Agents/src/Services/Support/PluginAgentValidator.cs` | 新增 | 效应分类器（安全限制校验） |
| `core/ai/Agents/src/Services/Support/PluginAgentLoader.cs` | 新增 | Map<name, (Def, PluginName)> + 可逆效应 + 响应式通知 |
| `core/ai/Agents/src/Services/Support/AgentDefinitionProvider.cs` | 修改 | 订阅 Changed 事件，合并插件 agent |
| `infrastructure/Infrastructure/Plugins/Services/PluginManager.cs` | 修改 | 加载/卸载时调用 LoadFromPlugin / 撤销函数 |
| 测试文件 | 新增 | 校验器 + 加载器 + 卸载测试 |

---

## 九、更新后的渐进式实现步骤

1. **新增 `IPluginAgentProvider` + `IPluginAgentLoader` 接口** → 编译 Abstractions
2. **新增 `PluginAgentValidator`** + 测试 → 效应分类器
3. **新增 `PluginAgentLoader`** + 测试 → Map +=name,(Def,PluginName)> + 可逆效应 + Changed 事件
4. **`AgentDefinitionProvider` 接入** → 订阅 Changed，合并插件 agent
5. **`PluginManager` 接入** → 加载时调 LoadFromPlugin 存撤销函数，卸载时执行撤销
6. **端到端测试** → 加载插件 → agent 可见 → 卸载插件 → agent 自动消失

---

## 十、待用户确认

1. **接口命名**：`IPluginAgentProvider` 是否合适？还是用 `IPluginAgentSource` / `IPluginAgentContributor`？
2. **校验失败行为**：抛异常（拒绝加载整个插件）还是跳过单个 agent + 日志警告？
3. **Map 覆盖语义**：两个插件提供同名 agent，后者覆盖前者？还是拒绝（报错）？
4. **可逆效应粒度**：`LoadFromPlugin` 返回 `Action`（撤销函数）是否合适？还是用 `IDisposable`？
5. **是否需要 `IPluginAgentProvider` 单独注册到 DI**？还是通过 `IWorkflowPlugin` 的 `Load(IServiceCollection)` 注册？
