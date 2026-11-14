# DSG050: 管道动态重建 — Profile 切换后管道热更新

> 📍 **导航**: [docs/](../README.md) › [design/](README.md) | **前置**: [DSG049](DSG049-runtime-profile-switching.md) 阶段2
> 状态: proposed
> 创建: 2026-11-14
> 关联: [DSG049](DSG049-runtime-profile-switching.md) 阶段2, [DSG048](DSG048-node-plugin-inheritance-architecture.md)

---

## 1. 问题

DSG049 阶段1 已实现 `IProfileManager` + `ProfileManager`，但 `ProfileSwitching` 事件在生产环境中无订阅者，管道无法感知 Profile 切换。存在两层阻断：

**阻断1 — DI 单例缓存**：`AddSingleton<ResolvedProfile>(sp => sp.GetRequiredService<IProfileManager>().Current)` 的工厂委托只在首次解析时执行。即使 `IProfileManager.Current` 后来变了，DI 容器中的 `ResolvedProfile` 单例仍然是旧值。

**阻断2 — 管道不可变性**：`MiddlewarePipeline<TContext>` 和 `StreamMiddlewarePipeline<TContext, TEvent>` 的 `_pipeline` 是 `readonly` 委托，构造时通过 `Build()` 反向折叠编译为不可变委托链，构造完成后无法修改中间件列表。

## 2. 目标

- Profile 切换后，管道使用新 Profile 的中间件列表执行后续请求
- 切换过程保证状态一致性（旧管道完成当前执行后再切换）
- 无锁设计（`volatile` 引用原子替换），符合项目配置读写规范
- 对消费方透明（管道调用方式不变）

## 3. 设计

### 3.1 动态管道代理 — `DynamicMiddlewarePipeline<TContext>`

引入新的管道代理类，内部持有 `volatile` 管道引用，Profile 切换时重建内部管道：

```csharp
public sealed class DynamicMiddlewarePipeline<TContext> {
    private volatile MiddlewarePipeline<TContext> _inner;
    private readonly IProfileManager _profileManager;
    private readonly NodeServiceResolver _resolver;
    private readonly string _consumerId;
    private readonly ILoggerFactory? _loggerFactory;

    public DynamicMiddlewarePipeline(
        MiddlewarePipeline<TContext> initial,
        IProfileManager profileManager,
        NodeServiceResolver resolver,
        string consumerId,
        ILoggerFactory? loggerFactory) {
        _inner = initial;
        _profileManager = profileManager;
        _resolver = resolver;
        _consumerId = consumerId;
        _loggerFactory = loggerFactory;
        _profileManager.ProfileSwitching += OnProfileSwitching;
    }

    public Task InvokeAsync(TContext context, CancellationToken ct) =>
        _inner.InvokeAsync(context, ct);

    private async ValueTask OnProfileSwitching(
        ResolvedProfile oldProfile, ResolvedProfile newProfile) {
        var middlewares = ResolveMiddlewares(
            _resolver, newProfile, _consumerId, _loggerFactory);
        _inner = PipelineBuilder<TContext>
            .Create()
            .UseRange(middlewares)
            .Build();
    }
}
```

同理 `DynamicStreamMiddlewarePipeline<TContext, TEvent>` 包装 `StreamMiddlewarePipeline`。

### 3.2 DI 注册改造

**当前**：
```csharp
services.AddSingleton<MiddlewarePipeline<PreprocessContext>>(sp =>
    BuildTaskPipeline<PreprocessContext>(sp, "preprocess-pipeline"));
```

**改造后**：
```csharp
services.AddSingleton<MiddlewarePipeline<PreprocessContext>>(sp => {
    var inner = BuildTaskPipeline<PreprocessContext>(sp, "preprocess-pipeline");
    var profileManager = sp.GetRequiredService<IProfileManager>();
    var resolver = sp.GetRequiredService<NodeServiceResolver>();
    return new DynamicMiddlewarePipeline<PreprocessContext>(
        inner, profileManager, resolver, "preprocess-pipeline",
        sp.GetService<ILoggerFactory>());
});
```

**关键决策**：`DynamicMiddlewarePipeline` 实现与 `MiddlewarePipeline` 相同的接口（或隐式转换），消费方代码不需要修改。

### 3.3 ResolvedProfile DI 注册移除

**当前**：
```csharp
services.AddSingleton<ResolvedProfile>(sp => sp.GetRequiredService<IProfileManager>().Current);
```

**改造后**：移除此注册。所有需要 `ResolvedProfile` 的消费方改为从 `IProfileManager.Current` 获取。

`BuildTaskPipeline` / `BuildStreamPipeline` 中的 `sp.GetRequiredService<ResolvedProfile>()` 改为 `sp.GetRequiredService<IProfileManager>().Current`。

### 3.4 ProfileSwitching 事件订阅

`DynamicMiddlewarePipeline` 构造时订阅 `IProfileManager.ProfileSwitching` 事件。事件回调中：

1. 从 `newProfile` 解析新的中间件列表
2. 构建新的 `MiddlewarePipeline` / `StreamMiddlewarePipeline`
3. `_inner = newPipeline` — volatile 写，原子替换

**状态一致性保证**：`volatile` 替换后，新的 `InvokeAsync` 调用会路由到新管道，旧的 `InvokeAsync` 调用仍在旧管道上执行（旧管道对象不会被 GC 回收直到所有在途请求完成）。这是无锁设计的安全保证。

### 3.5 改造范围

| 组件 | 文件路径 | 改造内容 |
|------|---------|---------|
| `DynamicMiddlewarePipeline<TContext>` | 新增 `modules/abstractions/infrastructure/pipeline/core/DynamicMiddlewarePipeline.cs` | 动态管道代理，volatile 引用 + ProfileSwitching 事件订阅 |
| `DynamicStreamMiddlewarePipeline<TContext,TEvent>` | 新增 `modules/abstractions/infrastructure/pipeline/core/DynamicStreamMiddlewarePipeline.cs` | 流式管道动态代理 |
| `DataDrivenPipelineComposition` | `modules/composition/pipelines/registration/DataDrivenPipelineComposition.cs` | 18 条管道注册改为包装 DynamicXxxPipeline；移除 `AddSingleton<ResolvedProfile>` 注册 |
| `BuildTaskPipeline` / `BuildStreamPipeline` | 同上 | `sp.GetRequiredService<ResolvedProfile>()` 改为 `sp.GetRequiredService<IProfileManager>().Current` |

### 3.6 不变组件

| 组件 | 原因 |
|------|------|
| `MiddlewarePipeline<TContext>` | 内部实现不变，DynamicXxxPipeline 包装它 |
| `StreamMiddlewarePipeline<TContext,TEvent>` | 同上 |
| `NodeRegistry` | 节点列表始终不变 |
| `NodeProfileResolver` | 仍用于解析继承链 |
| `NodeServiceResolver` | 仍用于解析中间件工厂 |

### 3.7 渐进式实现

**阶段1**：新建 `DynamicMiddlewarePipeline` + `DynamicStreamMiddlewarePipeline`，单元测试验证 volatile 替换和事件订阅机制

**阶段2**：改造 `DataDrivenPipelineComposition`，18 条管道注册改为包装 DynamicXxxPipeline，移除 `AddSingleton<ResolvedProfile>` 注册

**阶段3**：端到端验证 — exe 启动后通过 API 切换 Profile，确认管道中间件列表变化

## 4. 风险与缓解

| 风险 | 缓解 |
|------|------|
| Profile 切换时正在执行的请求 | `volatile` 替换保证旧请求在旧管道上完成，新请求路由到新管道 |
| 事件订阅泄漏（DynamicPipeline 不释放） | 管道是 DI 单例，生命周期与 IProfileManager 相同，无需取消订阅 |
| 中间件实例持有状态（如缓存） | Profile 切换后中间件重建，状态丢失。需评估哪些中间件有状态，是否需要迁移 |
| 18 条管道全部改造工作量 | 可分批改造：先改核心管道（chat/preprocess），再改其他 |

## 5. 验收

| 验收项 | 方法 |
|--------|------|
| Profile 切换后管道中间件列表变化 | exe 启动后切换 Profile，`--dump-profile` 输出正确 |
| 切换不崩溃 | 切换后对话正常执行 |
| 状态一致性 | 旧管道完成当前执行后再切换（volatile 保证） |
| 无锁设计 | `volatile` 引用原子替换，无 `lock` |
| 单元测试 | DynamicMiddlewarePipeline 替换机制测试 |

## 6. 待确认决策

1. **中间件状态迁移**：Profile 切换后中间件重建，有状态中间件（如 `CrashSnapshotMiddleware`）的状态是否需要迁移到新实例？
2. **分批改造 vs 全量改造**：18 条管道是否全部一次性改造，还是先改核心管道（chat/preprocess）验证后再推广？
3. **DynamicXxxPipeline 是否实现接口**：是直接暴露 `InvokeAsync` 方法，还是实现 `IMiddlewarePipeline<TContext>` 接口（如果存在）？
