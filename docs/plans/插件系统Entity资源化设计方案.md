# 插件系统 Entity 资源化设计方案

> **创建时间**: 2026-08-17
> **对齐目标**: Entity 统一基类 + 资源引用计数 + 心跳存活 + UI/非托管资源表 + 两阶段协作式卸载
> **前置**: 插件系统Cordis重新设计方案(已完成)
> **状态**: 设计中

---

## 一、需求来源

用户提出 6 项需求:

| # | 需求 | 对应概念 |
|---|------|---------|
| 1 | 资源基类和插件基类都要继承 Entity | `PluginResourceBase : Entity`、`WorkflowPluginBase : ServiceEntity` |
| a | 插件可以引用另一个插件的资源 | `ResourceReference` 跨插件引用记录 |
| b | 心跳存活属性,使用上层资源时检测上层心跳 | `IPluginHeartbeat` 惰性检测 |
| c | 插件基类持有 UI 资源表,可逆操作时刷新界面 | `UiResourceTable` + `IAppEventBus` 广播 |
| d | 插件基类持有非托管内存资源表,明确登记 | `UnmanagedResourceTable` |
| e | 后台扫描 ObjectId 检查卸载正确性,连带卸载按资源顺序 | `PluginResourceScanner` + 两阶段协作式卸载 |

## 二、决策记录(用户确认)

| 决策点 | 选择 |
|--------|------|
| 插件基类强制性 | **强制继承 WorkflowPluginBase** — 所有插件必须继承,获得 Entity + UI资源表 + 非托管资源表 + 心跳 |
| 资源粒度 | **细粒度** — 每个命令/钩子/技能/Agent 都是一个 Resource,引用计数精确到单个资源 |
| 心跳触发模式 | **惰性检测** — 每次使用上层资源时检测心跳(读 volatile bool,纳秒级) |
| UI资源表消费方 | **事件总线广播** — 通过 IAppEventBus 广播 UiResourceChangedEvent |
| 后台扫描频率 | **事件触发** — 卸载完成后扫描一次,检查 ObjectId 是否全部注销 |
| 实现范围 | **全部实现** — 5个概念 + 卸载流程升级 |

---

## 三、架构概览

### 3.1 类层次

```
Entity (已有)
├── ServiceEntity (已有)
│   ├── PluginManager (已有)
│   ├── WorkflowPluginBase (新增) ── IWorkflowPlugin
│   │   └── DreamPlugin (迁移)
│   └── PluginResourceBase (新增) ── IPluginResource
│       ├── CommandResource
│       ├── HookResource
│       ├── SkillResource
│       └── AgentResource
│
└── (其他 Entity 派生类不变)
```

### 3.2 资源引用图

```
插件A (WorkflowPluginBase)
├── 资源 cmdA1 (CommandResource, ObjectId=...)
├── 资源 cmdA2 (CommandResource, ObjectId=...)
└── 资源 hookA1 (HookResource, ObjectId=...)

插件B (WorkflowPluginBase)
├── 资源 cmdB1 (CommandResource, ObjectId=...)
│   └── 引用 cmdA1 (ResourceReference: B.cmdB1 → A.cmdA1, refCount++)
└── 资源 skillB1 (SkillResource, ObjectId=...)
    └── 引用 hookA1 (ResourceReference: B.skillB1 → A.hookA1, refCount++)
```

### 3.3 卸载流程(两阶段协作式)

```
卸载插件A:

阶段1 — Prepare(放弃引用):
  1. 找到所有引用 A 资源的外部插件 {B, C}
  2. 通知 B、C 调用 ReleaseReferencesTo(A)
     - B 释放对 cmdA1 的引用 → cmdA1.refCount--
     - C 释放对 hookA1 的引用 → hookA1.refCount--
  3. 等待 A 所有资源的 refCount → 0(带超时)
     - 超时 → CooperativeTimeout,回退强制卸载
  4. A 自己的资源如果引用了其他插件的资源,也释放

阶段2 — Commit(释放资源):
  1. 按资源依赖图拓扑序卸载 A 的资源(不是 LIFO)
  2. 每个资源 OnDispose → 从 ObjectIdManager 注销
  3. A.OnDispose → 释放 UI 资源表 → 广播 UiResourceChangedEvent
  4. A.OnDispose → 释放非托管资源表 → 逐个释放 SafeHandle

阶段3 — Verify(后台扫描):
  1. PluginResourceScanner 扫描 A 所有资源的 ObjectId
  2. 检查 ObjectIdManager 中是否已全部注销
  3. 如果有未注销的 → 记录警告日志(资源泄漏)
```

---

## 四、接口定义

### 4.1 ObjectType 枚举扩展

```csharp
// foundation/Abstractions/00-core/Core/Entity/ObjectType.cs
public enum ObjectType
{
    // ... 已有值 ...
    Cache = 19,
    [EnumValue("plugin")] Plugin = 20,      // 新增
    [EnumValue("resource")] Resource = 21,  // 新增
}
```

### 4.2 IPluginHeartbeat — 插件心跳

```csharp
// foundation/Abstractions/03-hands/Skill/IPluginHeartbeat.cs
public interface IPluginHeartbeat
{
    /// <summary>是否存活 — volatile bool,纳秒级读取</summary>
    bool IsAlive { get; }
    
    /// <summary>最后心跳时刻</summary>
    DateTime LastHeartbeatAt { get; }
    
    /// <summary>刷新心跳 — 插件每次活动时调用</summary>
    void Touch();
    
    /// <summary>标记死亡 — 不可逆,触发 OnDeath 事件</summary>
    void MarkDead();
    
    /// <summary>死亡事件 — 心跳停止时触发</summary>
    event EventHandler? OnDeath;
}
```

### 4.3 PluginResourceBase — 资源基类

```csharp
// foundation/Abstractions/03-hands/Skill/PluginResourceBase.cs
public abstract class PluginResourceBase : Entity, IPluginHeartbeat
{
    /// <summary>所属插件名</summary>
    public string OwnerPluginName { get; }
    
    /// <summary>资源类型(Command/Hook/Skill/Agent)</summary>
    public PluginResourceKind Kind { get; }
    
    /// <summary>引用计数 — 多少其他插件的资源引用了此资源</summary>
    public int ReferenceCount => _refCount;
    private volatile int _refCount;
    
    // IPluginHeartbeat 实现
    public bool IsAlive => _isAlive;
    private volatile bool _isAlive = true;
    public DateTime LastHeartbeatAt { get; private set; }
    public event EventHandler? OnDeath;
    
    /// <summary>增加引用 — 返回引用句柄,Dispose 时减少引用</summary>
    public ResourceReferenceHandle AddReference(string consumerPluginName, PluginResourceBase? consumerResource = null);
    
    /// <summary>减少引用 — 引用方放弃此资源时调用</summary>
    public void ReleaseReference(string consumerPluginName);
    
    /// <summary>心跳检测 — 使用此资源前调用,如果提供者已死亡则抛 PluginDeadException</summary>
    public void EnsureAlive();
    
    public void Touch() { LastHeartbeatAt = DateTime.UtcNow; }
    
    public void MarkDead()
    {
        if (!_isAlive) return;
        _isAlive = false;
        OnDeath?.Invoke(this, EventArgs.Empty);
    }
    
    public void EnsureAlive()
    {
        if (!_isAlive)
            throw new PluginDeadException($"资源 {DisplayName} (插件 {OwnerPluginName}) 心跳已停止");
    }
    
    protected PluginResourceBase(string ownerPluginName, PluginResourceKind kind, string displayName)
        : base(ObjectType.Resource, displayName: displayName)
    {
        OwnerPluginName = ownerPluginName;
        Kind = kind;
    }
}

public enum PluginResourceKind
{
    Command,
    Hook,
    Skill,
    Agent,
}
```

### 4.4 ResourceReferenceHandle — 引用句柄

```csharp
// foundation/Abstractions/03-hands/Skill/ResourceReferenceHandle.cs
/// <summary>引用句柄 — using 模式自动释放引用</summary>
public sealed class ResourceReferenceHandle : IDisposable
{
    private readonly PluginResourceBase _target;
    private readonly string _consumerPluginName;
    private bool _released;
    
    internal ResourceReferenceHandle(PluginResourceBase target, string consumerPluginName)
    {
        _target = target;
        _consumerPluginName = consumerPluginName;
    }
    
    public void Dispose()
    {
        if (_released) return;
        _released = true;
        _target.ReleaseReference(_consumerPluginName);
    }
}
```

### 4.5 ResourceReference — 跨插件引用记录

```csharp
// foundation/Abstractions/03-hands/Skill/ResourceReference.cs
/// <summary>跨插件资源引用记录 — 插件B 的资源 引用 插件A 的资源</summary>
public sealed record ResourceReference(
    ObjectId ConsumerResourceId,  // 引用方资源ID
    ObjectId TargetResourceId,    // 被引用资源ID
    string ConsumerPluginName,    // 引用方插件名
    string TargetPluginName);     // 被引用方插件名
```

### 4.6 IResourceReferenceGraph — 引用图管理

```csharp
// foundation/Abstractions/03-hands/Skill/IResourceReferenceGraph.cs
public interface IResourceReferenceGraph
{
    /// <summary>记录引用 — 插件B 引用 插件A 的资源</summary>
    void AddReference(ResourceReference reference);
    
    /// <summary>移除引用 — 引用方放弃引用</summary>
    void RemoveReference(ObjectId consumerResourceId, ObjectId targetResourceId);
    
    /// <summary>获取引用某插件资源的所有引用方插件 — 用于连带卸载</summary>
    IReadOnlyList<string> GetConsumers(string targetPluginName);
    
    /// <summary>获取某插件引用的所有外部资源 — 用于释放引用</summary>
    IReadOnlyList<ResourceReference> GetReferencesBy(string consumerPluginName);
    
    /// <summary>获取某插件所有资源的引用计数 — 用于卸载前检查</summary>
    IReadOnlyDictionary<ObjectId, int> GetReferenceCounts(string pluginName);
}
```

### 4.7 UiResourceTable — UI 资源表

```csharp
// foundation/Abstractions/03-hands/Skill/UiResourceTable.cs
/// <summary>UI 资源表 — 插件持有的界面资源(图标、菜单项、工具栏按钮等)</summary>
public sealed class UiResourceTable
{
    private readonly ConcurrentDictionary<string, UiResourceEntry> _resources = new();
    
    /// <summary>登记 UI 资源</summary>
    public void Register(string key, UiResourceEntry entry);
    
    /// <summary>移除 UI 资源</summary>
    public bool Unregister(string key);
    
    /// <summary>获取所有已登记的 UI 资源</summary>
    public IReadOnlyCollection<UiResourceEntry> GetAll();
    
    /// <summary>清空并返回变更事件 — 卸载时调用</summary>
    public UiResourceChangedEvent ClearAndEmitEvent(string pluginName);
}

public sealed record UiResourceEntry(
    string Key,              // 资源键(如 "toolbar.dream")
    UiResourceKind Kind,     // 资源类型(Icon/MenuItem/ToolbarButton/Panel)
    string DisplayName,      // 显示名
    object? Payload);        // 资源载荷(图标路径、命令名等)

public enum UiResourceKind { Icon, MenuItem, ToolbarButton, Panel, StatusBar }

public sealed record UiResourceChangedEvent(
    string PluginName,
    IReadOnlyList<UiResourceEntry> RemovedResources,
    DateTime Timestamp);
```

### 4.8 UnmanagedResourceTable — 非托管资源表

```csharp
// foundation/Abstractions/03-hands/Skill/UnmanagedResourceTable.cs
/// <summary>非托管内存资源表 — 明确登记,卸载时逐个释放</summary>
public sealed class UnmanagedResourceTable
{
    private readonly ConcurrentDictionary<string, UnmanagedResourceEntry> _resources = new();
    
    /// <summary>登记非托管资源 — 返回句柄,Dispose 时自动注销</summary>
    public UnmanagedResourceHandle Register(string key, SafeHandle handle, long estimatedBytes);
    
    /// <summary>获取所有已登记的非托管资源</summary>
    public IReadOnlyCollection<UnmanagedResourceEntry> GetAll();
    
    /// <summary>释放所有非托管资源 — 卸载时调用</summary>
    public void ReleaseAll();
}

public sealed record UnmanagedResourceEntry(
    string Key,
    SafeHandle Handle,
    long EstimatedBytes,
    DateTime RegisteredAt);

public sealed class UnmanagedResourceHandle : IDisposable
{
    // Dispose 时从表中移除并释放 SafeHandle
}
```

### 4.9 WorkflowPluginBase — 插件基类

```csharp
// foundation/Abstractions/03-hands/Skill/WorkflowPluginBase.cs
public abstract class WorkflowPluginBase : ServiceEntity, IWorkflowPlugin, IPluginHeartbeat
{
    // IWorkflowPlugin 抽象成员(子类实现)
    public abstract string Name { get; }
    public abstract string Version { get; }
    public abstract string Description { get; }
    public abstract OperationResult Load(IServiceCollection services);
    public abstract Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken ct = default);
    
    // 资源登记表
    private readonly Dictionary<ObjectId, PluginResourceBase> _resources = new();
    public IReadOnlyCollection<PluginResourceBase> Resources => _resources.Values;
    
    // UI 资源表
    public UiResourceTable UiResources { get; } = new();
    
    // 非托管资源表
    public UnmanagedResourceTable UnmanagedResources { get; } = new();
    
    // 心跳
    public bool IsAlive => _isAlive;
    private volatile bool _isAlive = true;
    public DateTime LastHeartbeatAt { get; private set; }
    public event EventHandler? OnDeath;
    
    /// <summary>登记资源 — 子类注册命令/钩子/技能/Agent 时调用</summary>
    protected T RegisterResource<T>(T resource) where T : PluginResourceBase;
    
    /// <summary>卸载 — 框架调用,子类不应 override,用 OnUnload 做清理</summary>
    public PluginUnloadResult Unload()
    {
        // 1. 标记死亡(心跳停止)
        MarkDead();
        // 2. 释放所有资源
        foreach (var resource in _resources.Values)
            resource.Dispose(); // Entity.Dispose → OnDispose → ObjectIdManager.Unregister
        // 3. 释放 UI 资源 → 广播事件(由 PluginManager 负责)
        // 4. 释放非托管资源
        UnmanagedResources.ReleaseAll();
        // 5. 子类清理
        OnUnload();
        return PluginUnloadResult.Success(Name, TimeSpan.Zero);
    }
    
    /// <summary>子类覆写:插件特定清理逻辑</summary>
    protected virtual void OnUnload() { }
    
    // IPluginHeartbeat
    public void Touch() { LastHeartbeatAt = DateTime.UtcNow; }
    public void MarkDead() { if (_isAlive) { _isAlive = false; OnDeath?.Invoke(this, EventArgs.Empty); } }
}
```

### 4.10 PluginResourceScanner — 后台扫描

```csharp
// infrastructure/Infrastructure/Plugins/Services/PluginResourceScanner.cs
public sealed class PluginResourceScanner
{
    /// <summary>扫描已卸载插件的资源是否全部注销 — 卸载完成后调用</summary>
    public ResourceScanReport ScanPluginResources(string pluginName, IReadOnlyCollection<ObjectId> resourceIds)
    {
        var leaked = new List<ObjectId>();
        foreach (var id in resourceIds)
        {
            if (ObjectIdManager.IsRegistered(id)) // 需要新增 IsRegistered 方法
                leaked.Add(id);
        }
        return new ResourceScanReport(pluginName, leaked);
    }
}

public sealed record ResourceScanReport(
    string PluginName,
    IReadOnlyList<ObjectId> LeakedResourceIds)
{
    public bool HasLeaks => LeakedResourceIds.Count > 0;
}
```

### 4.11 PluginDeadException

```csharp
// foundation/Abstractions/03-hands/Skill/PluginDeadException.cs
public sealed class PluginDeadException : Exception
{
    public PluginDeadException(string message) : base(message) { }
}
```

---

## 五、PluginManager 卸载流程升级

### 5.1 新增字段

```csharp
// PluginManager.cs 新增
private IResourceReferenceGraph? _referenceGraph;
private PluginResourceScanner _resourceScanner = new();
private IAppEventBus? _eventBus;

private IResourceReferenceGraph ReferenceGraph => 
    _referenceGraph ??= _serviceProvider?.GetService<IResourceReferenceGraph>();
```

### 5.2 UnloadPluginAsync 升级为两阶段

```csharp
public async Task<PluginUnloadResult> UnloadPluginAsync(string pluginName, CancellationToken ct)
{
    // 阶段1 — Prepare: 让引用方放弃引用
    var prepareResult = await PrepareUnloadAsync(pluginName, ct);
    if (!prepareResult.IsSuccess) return prepareResult; // 超时回退
    
    // 阶段2 — Commit: 释放资源(现有撤销链 + 连带卸载)
    var commitResult = await CommitUnloadAsync(pluginName, ct);
    
    // 阶段3 — Verify: 后台扫描
    var report = _resourceScanner.ScanPluginResources(pluginName, commitResult.ResourceIds);
    if (report.HasLeaks)
        _logger?.LogWarning("插件 {Plugin} 有 {Count} 个资源未注销", pluginName, report.LeakedResourceIds.Count);
    
    // 广播 UI 资源变更事件
    if (commitResult.UiEvent is not null && _eventBus is not null)
        await _eventBus.PublishAsync(new AppEvent(..., commitResult.UiEvent));
    
    return commitResult.ToUnloadResult();
}

private async Task<PrepareResult> PrepareUnloadAsync(string pluginName, CancellationToken ct)
{
    var graph = ReferenceGraph;
    if (graph is null) return PrepareResult.Success();
    
    // 1. 找到所有引用此插件资源的消费者
    var consumers = graph.GetConsumers(pluginName);
    
    // 2. 通知消费者放弃引用(如果消费者也死了,跳过)
    foreach (var consumer in consumers)
    {
        if (_workflowPlugins.TryGetValue(consumer, out var host) && host.Plugin.IsAlive)
            await NotifyReleaseReferencesAsync(consumer, pluginName, ct);
    }
    
    // 3. 等待引用计数归零(带超时)
    var deadline = DateTime.UtcNow + _cooperativeTimeout;
    while (DateTime.UtcNow < deadline)
    {
        var refCounts = graph.GetReferenceCounts(pluginName);
        if (refCounts.Values.All(c => c == 0)) return PrepareResult.Success();
        await Task.Delay(100, ct);
    }
    return PrepareResult.Timeout();
}
```

---

## 六、实现步骤(渐进式 TDD)

| 阶段 | 内容 | 测试 |
|------|------|------|
| 1 | ObjectType 新增 Plugin/Resource + ObjectIdManager.IsRegistered | 单元测试 |
| 2 | IPluginHeartbeat + PluginDeadException | 单元测试 |
| 3 | PluginResourceBase + ResourceReferenceHandle + 引用计数 | 单元测试 |
| 4 | ResourceReference + IResourceReferenceGraph + 实现 | 单元测试 |
| 5 | UiResourceTable + UiResourceChangedEvent | 单元测试 |
| 6 | UnmanagedResourceTable + UnmanagedResourceHandle | 单元测试 |
| 7 | WorkflowPluginBase(整合上述) | 单元测试 |
| 8 | PluginResourceScanner | 单元测试 |
| 9 | PluginManager 卸载流程升级(两阶段) | 单元测试 |
| 10 | DreamPlugin 迁移到 WorkflowPluginBase | 集成测试 |
| 11 | 全量编译 + 测试验证 | 全量测试 |

---

## 七、文件清单

### 新增文件

| 文件 | 内容 |
|------|------|
| `foundation/Abstractions/03-hands/Skill/IPluginHeartbeat.cs` | 心跳接口 |
| `foundation/Abstractions/03-hands/Skill/PluginResourceBase.cs` | 资源基类 |
| `foundation/Abstractions/03-hands/Skill/PluginResourceKind.cs` | 资源类型枚举 |
| `foundation/Abstractions/03-hands/Skill/ResourceReferenceHandle.cs` | 引用句柄 |
| `foundation/Abstractions/03-hands/Skill/ResourceReference.cs` | 引用记录 |
| `foundation/Abstractions/03-hands/Skill/IResourceReferenceGraph.cs` | 引用图接口 |
| `foundation/Abstractions/03-hands/Skill/UiResourceTable.cs` | UI 资源表 |
| `foundation/Abstractions/03-hands/Skill/UnmanagedResourceTable.cs` | 非托管资源表 |
| `foundation/Abstractions/03-hands/Skill/WorkflowPluginBase.cs` | 插件基类 |
| `foundation/Abstractions/03-hands/Skill/PluginDeadException.cs` | 插件死亡异常 |
| `infrastructure/Infrastructure/Plugins/Services/ResourceReferenceGraph.cs` | 引用图实现 |
| `infrastructure/Infrastructure/Plugins/Services/PluginResourceScanner.cs` | 后台扫描 |

### 修改文件

| 文件 | 变更 |
|------|------|
| `foundation/Abstractions/00-core/Core/Entity/ObjectType.cs` | 新增 Plugin=20, Resource=21 |
| `foundation/Abstractions/00-core/Core/Entity/ObjectIdManager.cs` | 新增 IsRegistered 方法 |
| `infrastructure/Infrastructure/Plugins/Services/PluginManager.cs` | 卸载流程升级为两阶段 |
| `services/Dream/src/Core/DreamPlugin.cs` | 迁移到 WorkflowPluginBase |

### 新增测试文件

| 文件 | 内容 |
|------|------|
| `foundation/Abstractions/tests/.../PluginResourceBaseTests.cs` | 资源基类测试 |
| `foundation/Abstractions/tests/.../ResourceReferenceGraphTests.cs` | 引用图测试 |
| `foundation/Abstractions/tests/.../WorkflowPluginBaseTests.cs` | 插件基类测试 |
| `infrastructure/Infrastructure/tests/.../PluginResourceScannerTests.cs` | 扫描器测试 |
| `infrastructure/Infrastructure/tests/.../PluginManagerTwoPhaseUnloadTests.cs` | 两阶段卸载测试 |

---

## 八、测试计划

### 8.1 单元测试

| 测试 | 验证点 |
|------|--------|
| PluginResourceBase_AddReference_RefCountIncrement | 引用计数正确递增 |
| PluginResourceBase_ReleaseReference_RefCountDecrement | 引用计数正确递减 |
| PluginResourceBase_EnsureAlive_ThrowsWhenDead | 心跳停止时 EnsureAlive 抛异常 |
| ResourceReferenceGraph_GetConsumers | 正确查找引用方 |
| ResourceReferenceGraph_GetReferenceCounts | 正确统计引用计数 |
| UiResourceTable_ClearAndEmitEvent | 清空并生成变更事件 |
| UnmanagedResourceTable_ReleaseAll | 释放所有非托管资源 |
| WorkflowPluginBase_Unload_ReleasesAllResources | 卸载时释放所有资源 |
| PluginResourceScanner_DetectsLeaks | 检测未注销的 ObjectId |
| PluginManager_TwoPhaseUnload_PrepareThenCommit | 两阶段卸载顺序正确 |

### 8.2 集成测试

| 测试 | 验证点 |
|------|--------|
| PluginA_Unloaded_PluginBReleasesReference | 卸载A时B自动释放引用 |
| PluginA_HeartbeatStops_PluginBDetectsDeath | A心跳停止,B使用资源时检测到 |
| PluginA_Unloaded_UiResourceEventBroadcast | UI资源变更事件正确广播 |
| PluginA_Unloaded_ScannerDetectsNoLeaks | 卸载后扫描无泄漏 |
| DreamPlugin_MigratedToWorkflowPluginBase | DreamPlugin 迁移后功能正常 |

---

<!-- 🤖 Auto Decision: 2026-08-17 -->
<!-- 决策: 资源粒度选择细粒度(每个命令/钩子/技能/Agent 都是一个 Resource) -->
<!-- 原因: 引用计数精确到单个资源,后台扫描能定位到具体哪个资源未释放,符合需求 e -->
<!-- 替代方案: 中粒度(按类别分组) — 简单但无法定位单个命令泄漏,不采用 -->

<!-- 🤖 Auto Decision: 2026-08-17 -->
<!-- 决策: 心跳检测选择惰性检测(每次使用上层资源时检测) -->
<!-- 原因: 零轮询开销,读 volatile bool 纳秒级,符合需求 b "只有使用的时候触发" -->
<!-- 替代方案: 混合模式(惰性+低频后台扫描) — 有扫描开销,暂不采用 -->

<!-- 🤖 Auto Decision: 2026-08-17 -->
<!-- 决策: 卸载流程从 LIFO 升级为两阶段协作式(prepare放弃引用 → commit释放资源) -->
<!-- 原因: 需求 e 指出"连带卸载有资源顺序问题,不能单纯用怎么顺序进来,而是让它们放弃活动的资源" -->
<!-- 替代方案: 保持 LIFO + 连带卸载 — 无法处理非线性资源依赖,不采用 -->
