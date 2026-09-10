# 插件开发契约

> ADR: [0098](../../adr/0098-plugin-system-fusion-actor-effectscope.md)
> 日期: 2026-09-10

## 概述

本项目的插件系统基于 **Actor + EffectScope + 弱引用事件 + ALC 隔离** 融合架构。
插件开发者只需实现 `IWorkflowPlugin` 接口，通过 `PluginContext` 注册副作用，框架自动管理生命周期和撤销链。

## 核心接口（插件开发者必须实现）

### IWorkflowPlugin — 生命周期接口

```csharp
public interface IWorkflowPlugin
{
    string Name { get; }
    string Version { get; }
    string Description { get; }
    Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken ct = default);
    Task<OperationResult> InitializeAsync(IServiceProvider sp, CancellationToken ct = default);
    PluginUnloadResult Unload();
}
```

**生命周期**: Load → Initialize → Unload

6. **LoadAsync**: 注册服务和副作用（通过 ctx）
2. **InitializeAsync**: 获取服务依赖，完成初始化
3. **Unload**: 释放资源（框架已自动执行撤销链，此处仅做额外清理）

### WorkflowPluginBase — 推荐基类

继承 `WorkflowPluginBase` 获得 Fiber 状态机、资源管理、卸载契约校验。

```csharp
public class MyPlugin : WorkflowPluginBase
{
    public override string Name => "MyPlugin";
    public override string Version => "1.0.0";
    public override string Description => "My awesome plugin";

    public override Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken ct = default)
    {
        ctx.RegisterService<IMyService, MyServiceImpl>();
        return Task.FromResult(OperationResult.Ok());
    }

    public override Task<OperationResult> InitializeAsync(IServiceProvider sp, CancellationToken ct = default)
        => Task.FromResult(OperationResult.Ok());

    protected override void OnUnload() { }
}
```

## 可选接口

### IPluginDependencies — 依赖声明

```csharp
public class MyPlugin : WorkflowPluginBase, IPluginDependencies
{
    public IReadOnlyList<string> Dependencies => new[] { "OtherPlugin" };
}
```

声明依赖后，卸载 `OtherPlugin` 时会连带卸载 `MyPlugin`（拓扑排序）。

### IPluginAgentProvider — Agent 提供者

实现此接口为框架提供 Agent 能力。

## PluginContext — 副作用唯一入口

| 方法 | 用途 | 撤销方式 |
|------|------|----------|
| `RegisterService<TService, TImpl>()` | 注册 DI 服务 | ServiceProvider.Dispose |
| `ConfigureServices(Action<IServiceCollection>)` | 批量配置服务 | ServiceProvider.Dispose |
| `Effect(Func<IDisposable>)` | 自定义同步副作用 | IDisposable.Dispose |
| `Effect(Func<IAsyncDisposable>)` | 自定义异步副作用 | IAsyncDisposable.DisposeAsync |
| `RunBackgroundTask(Func<CancellationToken, Task>)` | 后台任务 | 等待任务退出（带超时） |
| `WeakSubscribe(source, target, handler)` | 弱引用事件订阅 | GC 自动清理 |

**关键**: 所有副作用通过 PluginContext 注册，框架自动收集撤销链，卸载时逆序执行。禁止绕过 ctx 直接操作 IServiceCollection。

## 文件位置

| 文件 | 位置 | 用途 |
|------|------|------|
| IWorkflowPlugin.cs | `foundation/Abstractions/03-hands/Skill/` | 生命周期接口 |
| IPluginDependencies.cs | 同上 | 依赖声明接口 |
| PluginContext.cs | 同上 | 副作用入口 |
| WorkflowPluginBase.cs | 同上 | 推荐基类 |
| EffectScope.cs | 同上 | 副作用作用域 |
| PluginDiagnostic.cs | 同上 | 诊断事件 |
| PluginAlc.cs | 同上 | ALC 隔离 |
| WeakEventBroker.cs | 同上 | 弱引用事件 |
| ServiceLookup.cs | 同上 | 服务查询枚举 |
| PluginDependencyGraph.cs | 同上 | 依赖图 |
| IPluginManager.cs | `infrastructure/Infrastructure/Plugins/Interfaces/` | 管理器接口 |

## 错误处理

- **加载失败**: 抛 `InvalidOperationException`，框架记录日志并标记 `Faulted`
- **卸载失败**: 返回 `PluginUnloadResult.AlcUnloadFailed`，框架上报 `PluginDiagnostic`
- **撤销链失败**: 框架捕获异常，上报 `PluginDiagnostic(RevertFailed)`，不中断后续撤销
- **资源泄漏**: 框架扫描 ObjectId，泄漏者加入黑名单，拒绝再次加载

## AOT 兼容

- 禁止反射 emit、dynamic、DispatchProxy
- 事件订阅用 `WeakSubscribe`（AOT 兼容），禁止原生 `+=`（弱引用无法回收）
- JSON 用 `RelaxedJsonSerializer`，禁止直接解析
