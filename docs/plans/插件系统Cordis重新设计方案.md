# 插件系统 Cordis 框架重新设计方案

> **创建时间**: 2026-08-17
> **对齐目标**: Cordis 框架 — 可逆效应 + 响应式协效应 + 连带卸载 + 传递依赖
> **状态**: 实现中

---

## 一、现状分析

### 1.1 只有 PluginAgentLoader 符合 Cordis

| 组件 | 可逆效应 | 响应式协效应 | 连带卸载 | 传递依赖 |
|------|---------|------------|---------|---------|
| PluginAgentLoader | ✅ | ✅ | ✅ | ✅ |
| PluginManager | ⚠️仅agent | ❌ | ❌ | ❌ |
| WorkflowPluginHost | ❌ | ❌ | ❌ | ❌ |
| IWorkflowPlugin | ❌ | ❌ | N/A | ❌ |
| PluginHookInjector | ❌ | ❌ | ❌ | ❌ |
| PluginCommandRegistry | ❌ | ❌ | ❌ | ❌ |
| PluginSkillBridge | ⚠️部分 | ❌ | ❌ | ❌ |
| DreamPlugin | ❌ | N/A | N/A | N/A |

### 1.2 实际 bug

1. **DreamPlugin.cs:51-55** — `_registeredCommandNames` 填充但 `OnDispose` 从未使用,命令残留
2. **WorkflowPluginHost.cs:30-39** — 外部传入的共享服务被加入插件容器,`Dispose()` 时可能错误释放
3. **PluginManager.cs:265-291** — `UnloadAllPluginsAsync` 无拓扑排序,卸载顺序不确定

---

## 二、设计原则

### 2.1 Cordis 四大要素

| 要素 | 实现方式 |
|------|---------|
| **可逆效应** | 每个注册操作返回 `Action` 撤销函数,框架收集形成撤销链 |
| **响应式协效应** | `Changed` 事件通知消费方缓存失效 |
| **连带卸载** | 卸载提供者时,先连带卸载所有依赖方(Theorem 63) |
| **传递依赖** | 不动点迭代支持 A→B→C 链式依赖 |

### 2.2 撤销链模式

```
插件加载:
  PluginManager.LoadWorkflowPluginAsync(plugin)
    → host.Load()           → 撤销函数1 (DI 服务)
    → host.InitializeAsync() → 撤销函数2 (命令注册)
    → hookInjector.Inject()  → 撤销函数3 (Hooks)
    → skillBridge.Register() → 撤销函数4 (Skills)
    → agentLoader.Load()     → 撤销函数5 (Agent)
    → 收集 [undo1, undo2, undo3, undo4, undo5] 存入 _pluginUndoChain[pluginName]

插件卸载:
  PluginManager.UnloadPluginAsync(plugin)
    → 按逆序执行撤销链: undo5 → undo4 → undo3 → undo2 → undo1
    → 连带卸载: 检查其他插件是否依赖此插件,先卸载依赖方
```

---

## 三、接口变更

### 3.1 ICommandRegistrationHook — 加 UnregisterCommands

```csharp
// foundation/Abstractions/03-hands/Shell/ICommandRegistrationHook.cs
public interface ICommandRegistrationHook
{
    void RegisterCommands(ICommandRegistry registry, IServiceProvider serviceProvider);
    
    /// <summary>撤销命令注册 — 可逆效应</summary>
    void UnregisterCommands(ICommandRegistry registry);
}
```

**影响**: DreamPlugin 必须实现 `UnregisterCommands`

### 3.2 IPluginHookInjector — 注册返回撤销函数

```csharp
// infrastructure/Infrastructure/Plugins/Services/PluginHookInjector.cs
public interface IPluginHookInjector
{
    /// <summary>注入 Hooks — 返回撤销函数</summary>
    Task<Action> InjectHooksAsync(string pluginName, IReadOnlyList<PluginHookDefinition> hooks, CancellationToken ct = default);
    
    Task RemoveHooksAsync(string pluginName, CancellationToken ct = default);
    IEnumerable<PluginHookDefinition> GetInjectedHooks(string pluginName);
}
```

### 3.3 IPluginCommandRegistry — 注册返回撤销函数

```csharp
// infrastructure/Infrastructure/Plugins/Services/PluginCommandRegistry.cs
public interface IPluginCommandRegistry
{
    /// <summary>注册命令 — 返回撤销函数</summary>
    Task<Action> RegisterCommandAsync(PluginCommandDefinition command, CancellationToken ct = default);
    
    Task UnregisterCommandAsync(string commandName, CancellationToken ct = default);
    IEnumerable<PluginCommandDefinition> GetRegisteredCommands();
    PluginCommandDefinition? GetCommand(string commandName);
}
```

### 3.4 IPluginSkillBridge — 注册返回撤销函数

```csharp
// foundation/Abstractions 中 IPluginSkillBridge
public interface IPluginSkillBridge
{
    /// <summary>注册插件技能 — 返回撤销函数</summary>
    Task<Action> RegisterPluginSkillsAsync(string pluginName, CancellationToken cancellationToken = default);
    
    Task UnregisterPluginSkillsAsync(string pluginName, CancellationToken cancellationToken = default);
    // ... 其他方法不变
}
```

### 3.5 IWorkflowPlugin — 不变

保持 `Load(IServiceCollection)` 返回 `OperationResult`,因为:
- DI 服务注册的撤销由 `ServiceProvider.Dispose()` 处理(粗粒度但有效)
- 改为返回 `Action` 会破坏所有插件实现
- Cordis 的可逆效应在各 Registry 层实现,不需要 IWorkflowPlugin 层

---

## 四、实现变更

### 4.1 WorkflowPluginHost — 修共享服务 Dispose + 命令撤销

```csharp
// 修改点:
// 1. 不把共享服务加入 _pluginServices,用单独字段保存引用
// 2. Unload() 时调用 UnregisterCommands
// 3. Dispose() 不 dispose 共享服务

public sealed class WorkflowPluginHost : IDisposable
{
    private ServiceProvider? _pluginServiceProvider;
    private readonly ICommandRegistry? _sharedCommandRegistry; // 共享引用,不 Dispose
    
    public WorkflowPluginHost(IWorkflowPlugin plugin, ICommandRegistry? commandRegistry, ...)
    {
        _sharedCommandRegistry = commandRegistry; // 保存引用,不加入 _pluginServices
        // _pluginServices 只加插件自己的服务
    }
    
    public PluginUnloadResult Unload()
    {
        // 撤销命令注册 — 可逆效应
        if (_plugin is ICommandRegistrationHook hook && _sharedCommandRegistry is not null)
        {
            try { hook.UnregisterCommands(_sharedCommandRegistry); }
            catch (Exception ex) { _logger?.LogWarning(ex, "命令撤销失败"); }
        }
        
        var result = _plugin.Unload();
        
        // 只 Dispose 插件自己的容器,不 Dispose 共享服务
        _pluginServiceProvider?.Dispose();
        _pluginServiceProvider = null;
        
        return result;
    }
}
```

### 4.2 DreamPlugin — 实现命令撤销

```csharp
public void RegisterCommands(ICommandRegistry registry, IServiceProvider serviceProvider)
{
    var dreamFeature = serviceProvider.GetRequiredService<IDreamFeature>();
    registry.Register(new DreamCommand(dreamFeature));
    _registeredCommandNames.Add(nameof(DreamCommand));
    registry.Register(new DreamTasksCommand(dreamFeature));
    _registeredCommandNames.Add(nameof(DreamTasksCommand));
}

public void UnregisterCommands(ICommandRegistry registry)
{
    foreach (var commandName in _registeredCommandNames)
    {
        registry.UnregisterCommand(commandName);
    }
    _registeredCommandNames.Clear();
}
```

### 4.3 PluginManager — 全局撤销链 + 拓扑排序

```csharp
// 新增字段: 每个插件的撤销链
private readonly ConcurrentDictionary<string, List<Action>> _pluginUndoChain = new();

// 加载时收集撤销函数
public async Task<WorkflowPluginHost> LoadWorkflowPluginAsync<TPlugin>(...)
{
    var undoChain = new List<Action>();
    
    var host = new WorkflowPluginHost(plugin, ...);
    var loadResult = host.Load();
    // host.Load 的撤销 = host.Unload (粗粒度)
    
    var initResult = await host.InitializeAsync(ct);
    
    // 命令注册撤销
    if (plugin is ICommandRegistrationHook hook)
    {
        // 撤销函数已在 host.Unload() 中调用 UnregisterCommands
    }
    
    // Agent 撤销
    if (plugin is IPluginAgentProvider agentProvider && PluginAgentLoader is not null)
    {
        var undo = PluginAgentLoader.LoadFromPlugin(pluginName, agentProvider);
        undoChain.Add(undo);
    }
    
    _pluginUndoChain[pluginName] = undoChain;
    return host;
}

// 卸载时按逆序执行撤销链
public async Task<PluginUnloadResult> UnloadPluginAsync(string pluginName, CancellationToken ct)
{
    // 1. 连带卸载检查: 找到依赖此插件的其他插件
    var dependents = FindDependentPlugins(pluginName);
    foreach (var dep in dependents) // 递归卸载依赖方
        await UnloadPluginAsync(dep, ct);
    
    // 2. 按逆序执行撤销链
    if (_pluginUndoChain.TryRemove(pluginName, out var undoChain))
    {
        for (int i = undoChain.Count - 1; i >= 0; i--)
        {
            try { undoChain[i](); }
            catch (Exception ex) { _logger?.LogWarning(ex, "撤销失败: {Plugin}", pluginName); }
        }
    }
    
    // 3. 卸载插件本身
    if (_workflowPlugins.TryRemove(pluginName, out var host))
    {
        var result = UnloadWorkflowPlugin(host);
        return result;
    }
    // ...
}

// UnloadAllPluginsAsync — 按注册逆序卸载
public async Task<IReadOnlyList<PluginUnloadResult>> UnloadAllPluginsAsync(...)
{
    // 按注册逆序卸载(后加载的先卸载 — Cordis 逆序执行)
    var pluginNames = _loadOrder.ToList(); // 需要新增 _loadOrder 字段
    pluginNames.Reverse();
    
    var results = new List<PluginUnloadResult>();
    foreach (var pluginName in pluginNames)
    {
        results.Add(await UnloadPluginAsync(pluginName, ct));
    }
    return results;
}
```

### 4.4 PluginCommandRegistry — 注册返回撤销函数

```csharp
public async Task<Action> RegisterCommandAsync(PluginCommandDefinition command, CancellationToken ct = default)
{
    // ... 注册逻辑 ...
    
    var registeredName = command.CommandName;
    var registeredAliases = command.Aliases;
    
    return () =>
    {
        RemoveCore(registeredName);
        if (registeredAliases is not null)
            foreach (var alias in registeredAliases) RemoveCore(alias);
    };
}
```

### 4.5 PluginHookInjector — 注册返回撤销函数

```csharp
public async Task<Action> InjectHooksAsync(string pluginName, IReadOnlyList<PluginHookDefinition> hooks, CancellationToken ct = default)
{
    // ... 注入逻辑 ...
    
    return () =>
    {
        _injectedHooks.TryRemove(pluginName, out _);
    };
}
```

### 4.6 PluginSkillBridge — 注册返回撤销函数

```csharp
public async Task<Action> RegisterPluginSkillsAsync(string pluginName, CancellationToken cancellationToken = default)
{
    // ... 注册逻辑 ...
    var registeredSkillNames = ...;
    
    return () =>
    {
        foreach (var skillName in registeredSkillNames)
            _skillService.UnregisterSkill(skillName);
        _pluginSkillMap.TryRemove(pluginName, out _);
    };
}
```

---

## 五、文件清单

| 文件 | 操作 | 变更类型 |
|------|------|---------|
| `foundation/Abstractions/03-hands/Shell/ICommandRegistrationHook.cs` | 修改 | 加 `UnregisterCommands` 方法 |
| `infrastructure/Infrastructure/Plugins/Plugins/WorkflowPluginHost.cs` | 修改 | 修共享服务 Dispose + 命令撤销 |
| `infrastructure/Infrastructure/Plugins/Services/PluginManager.cs` | 修改 | 全局撤销链 + 拓扑排序 |
| `infrastructure/Infrastructure/Plugins/Services/PluginHookInjector.cs` | 修改 | 返回撤销函数 |
| `infrastructure/Infrastructure/Plugins/Services/PluginCommandRegistry.cs` | 修改 | 返回撤销函数 |
| `core/execution/Hands/src/Skills/Plugin/PluginSkillBridge.cs` | 修改 | 返回撤销函数 |
| `services/Dream/src/Core/DreamPlugin.cs` | 修改 | 实现 `UnregisterCommands` |
| `foundation/Abstractions/.../IPluginSkillBridge.cs` | 修改 | 接口签名变更 |
| `foundation/Abstractions/.../IPluginHookInjector.cs` | 修改 | 接口签名变更 |
| `foundation/Abstractions/.../IPluginCommandRegistry.cs` | 修改 | 接口签名变更 |
| 测试文件 | 新增/修改 | 撤销链 + 连带卸载测试 |

---

## 六、渐进式实现步骤

### 阶段1: 修实际 bug (最小改动)

1. **DreamPlugin** — 实现 `UnregisterCommands`,用 `_registeredCommandNames` 撤销命令
2. **ICommandRegistrationHook** — 加 `UnregisterCommands` 方法
3. **WorkflowPluginHost** — `Unload()` 调用 `UnregisterCommands` + 修共享服务 Dispose
4. 编译 + 测试 + commit

### 阶段2: Registry 层可逆效应

5. **PluginCommandRegistry** — `RegisterCommandAsync` 返回 `Task<Action>`
6. **PluginHookInjector** — `InjectHooksAsync` 返回 `Task<Action>`
7. **PluginSkillBridge** — `RegisterPluginSkillsAsync` 返回 `Task<Action>`
8. 更新所有调用方
9. 编译 + 测试 + commit

### 阶段3: PluginManager 撤销链 + 拓扑排序

10. **PluginManager** — 新增 `_pluginUndoChain` + `_loadOrder`
11. **PluginManager** — `LoadWorkflowPluginAsync` 收集撤销函数
12. **PluginManager** — `UnloadPluginAsync` 按逆序执行撤销链
13. **PluginManager** — `UnloadAllPluginsAsync` 按注册逆序卸载
14. 编译 + 测试 + commit

### 阶段4: 连带卸载

15. **PluginManager** — `FindDependentPlugins` 依赖分析
16. **PluginManager** — `UnloadPluginAsync` 连带卸载依赖方
17. 编译 + 测试 + commit

---

## 七、决策记录

<!-- 🤖 Auto Decision: 2026-08-17 -->
<!-- 决策: IWorkflowPlugin.Load 保持返回 OperationResult,不改为返回撤销函数 -->
<!-- 原因: 改为返回 Action 会破坏所有插件实现,Cordis 可逆效应在各 Registry 层实现已足够 -->
<!-- 替代方案: IWorkflowPlugin.Load 返回 (OperationResult, Action) — 破坏性太大,暂不采用 -->
<!-- 验证: 待编译验证 -->

<!-- 🤖 Auto Decision: 2026-08-17 -->
<!-- 决策: WorkflowPluginHost 不把共享服务加入 _pluginServices,用单独字段保存引用 -->
<!-- 原因: 避免 ServiceProvider.Dispose() 错误释放全局单例(如 ICommandRegistry) -->
<!-- 替代方案: 用自定义 ServiceProviderFactory 排除共享服务 — 复杂度高,暂不采用 -->
<!-- 验证: 待编译验证 -->
