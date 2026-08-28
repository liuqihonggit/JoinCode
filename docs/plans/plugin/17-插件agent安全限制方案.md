# #17 插件 Agent 安全限制 — 方案设计

> **创建时间**: 2026-08-17
> **对齐目标**: TS 原版 `src/utils/plugins/loadPluginAgents.ts`
> **状态**: 待用户审阅

---

## 一、目的：为什么要做这个？

### 1.1 TS 原版 的设计

TS 原版 的插件可以贡献 agent 定义（从插件目录加载 `.md` 文件）。但出于**安装时信任边界**考虑，插件 agent **不能**定义以下三个字段：

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
/// 对齐 TS 原版 loadPluginAgents: 从插件加载 agent 定义 + 安全限制
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
/// 插件 Agent 安全限制校验 — 对齐 TS 原版 安装时信任边界
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
/// 对齐 TS 原版 loadPluginAgents
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

### 7.1 可逆效应 — 撤销链 + 逆序执行

Cordis 的核心思想：**每个修改上下文的操作必须附带逆操作**，框架自动收集形成**撤销链**，卸载时按**注册逆序**执行。

**我之前的设计缺陷**：`PluginManager` 手动存储单个撤销函数，没有全局撤销链，没有逆序执行。

**修正设计**：`PluginAgentLoader` 自身维护撤销链，框架自动管理逆序执行：

```csharp
public sealed class PluginAgentLoader : IPluginAgentLoader
{
    // Map<agentName, (AgentDefinition, pluginName)>
    private FrozenDictionary<string, (AgentDefinition, string)> _pluginAgents = ...;

    // 撤销链 — 按注册顺序记录，卸载时按逆序执行（Cordis Effect 系统）
    private readonly List<(string PluginName, List<string> AgentKeys)> _loadOrder = new();

    /// <summary>加载插件 agent — 返回撤销函数（可逆效应）</summary>
    public Action LoadFromPlugin(string pluginName, IPluginAgentProvider provider)
    {
        var addedKeys = new List<string>();
        // ... 校验 + 添加到 Map ...
        _loadOrder.Add((pluginName, addedKeys));
        NotifyChanged();

        // 返回逆操作 — 执行时触发连带卸载
        return () => UnloadWithCascade(pluginName);
    }

    /// <summary>
    /// 连带卸载 — 对齐 Cordis Theorem 63 (Ordering):
    /// 先卸载所有依赖此插件 agent 的消费者，最后卸载提供者本身
    /// </summary>
    private void UnloadWithCascade(string pluginName)
    {
        // 1. 找到此插件贡献的 agent 名集合
        var providerAgents = _loadOrder
            .Where(x => x.PluginName == pluginName)
            .SelectMany(x => x.AgentKeys)
            .ToHashSet();

        // 2. 连带检查：找到所有引用了这些 agent 的其他插件（消费者）
        //    按注册逆序遍历（后加载的先卸载 — Cordis 逆序执行）
        for (int i = _loadOrder.Count - 1; i >= 0; i--)
        {
            var entry = _loadOrder[i];
            if (entry.PluginName == pluginName) continue;

            // 检查此插件的 agent 是否依赖被卸载的 agent
            if (PluginDependsOnAgents(entry.PluginName, providerAgents))
            {
                RemovePluginAgents(entry);
                _loadOrder.RemoveAt(i);
            }
        }

        // 3. 最后卸载提供者本身（Theorem 63: 提供者最后卸载）
        var selfEntries = _loadOrder.Where(x => x.PluginName == pluginName).ToList();
        foreach (var entry in selfEntries)
            RemovePluginAgents(entry);
        _loadOrder.RemoveAll(x => x.PluginName == pluginName);

        NotifyChanged();
    }
}
```

**关键改进**：
- **撤销链**：`_.LoadOrder` 按注册顺序记录，卸载时按**逆序**遍历（Cordis Effect 系统）
- **连带卸载**：卸载插件A时，先检查哪些其他插件的agent引用了A的agent，先连带卸载消费者
- **Theorem 63**：提供者最后卸载，确保依赖方先清理
- **框架自动管理**：撤销链由 `PluginAgentLoader` 维护，`PluginManager` 只调 `LoadFromPlugin` 和执行返回的撤销函数

### 7.2 连带依赖检查 — Reactive Coeffects

Cordis 的"响应式协效应"：卸载服务提供者时，框架主动查找所有依赖此服务的消费者，先连带卸载。

**agent 之间的依赖关系**（隐式依赖分析）：

```csharp
/// <summary>
/// 检查插件B的 agent 是否依赖被卸载的 agent 集合
/// 依赖关系分析:
/// 1. B的agent的Tools列表包含"Agent"工具 + AllowedAgentTypes引用了被卸载的agent名
/// 2. B的agent的Skills列表引用了被卸载的agent名（skill可能包装agent）
/// </summary>
private bool PluginDependsOnAgents(string consumerPlugin, HashSet<string> providerAgentNames)
{
    var consumerAgents = _pluginAgents
        .Where(kv => kv.Value.Item2 == consumerPlugin)
        .Select(kv => kv.Value.Item1)
        .ToList();

    foreach (var agent in consumerAgents)
    {
        // 检查1: Tools 包含 Agent 工具 + 引用了被卸载的 agent
        if (agent.Tools is not null && agent.Tools.Contains(AgentToolNameConstants.Agent))
        {
            // 如果agent没有限制 AllowedAgentTypes，则依赖所有agent（包括被卸载的）
            // 如果有限制，检查是否包含被卸载的agent名
            var allowedTypes = AgentTypeSpecParser.Parse(agent.SubagentType).AllowedTypes;
            if (allowedTypes is null || allowedTypes.Any(providerAgentNames.Contains))
                return true;
        }

        // 检查2: Skills 引用了被卸载的agent名
        if (agent.Skills is not null && agent.Skills.Any(providerAgentNames.Contains))
            return true;
    }
    return false;
}
```

### 7.3 响应式协效应 — Changed 事件通知

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

### 7.4 效应分类 — 安全限制的本质

Cordis 将操作分类为不同"效应"。我们可以把安全限制理解为**效应分类**：

| 效应类型 | 说明 | 插件 agent 能产生？ |
|----------|------|---------------------|
| **安全效应** | 工具调用、提示词、模型选择 — 在框架安全边界内 | ✅ 允许 |
| **特权效应** | 权限变更（permissionMode）、钩子注入（hooks）、外部进程引入（mcpServers） | ❌ 禁止 |

`PluginAgentValidator` 本质上是一个**效应分类器** — 检查插件 agent 是否只产生安全效应，不产生特权效应。

### 7.5 作用域隔离 — 插件 agent 的可见性

Cordis 的"作用域隔离"：子 Fiber 能看父 Fiber 注册的服务，反之不行。

融入我们的方案：插件 agent 的可见性遵循**加载顺序优先级**：

```
内置 agent (始终可见)
  ← 插件 agent (插件加载后可见，卸载后不可见)
    ← 用户 agent (用户= ~/.jcc/agents/)
      ← 项目 agent (= .jcc/agents/)
```

插件卸载时，其贡献的 agent 变为不可见 — 这就是 Cordis 的"时间可组合性"：卸载不需要重启，agent 定义自动消失。

### 7.6 声明式依赖 — 插件不猜测环境

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
