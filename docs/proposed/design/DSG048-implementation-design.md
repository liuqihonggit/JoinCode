# DSG048 实现设计 — 详细类图与文件结构

> 📍 **导航**: [docs/](../README.md) › [design/](README.md) | **前置**: [DSG048](DSG048-node-plugin-inheritance-architecture.md)
> 状态: accepted（阶段1 基础设施层已实现并验收）
> 创建: 2026-11-12
> 关联: [DSG048](DSG048-node-plugin-inheritance-architecture.md) 架构 spec

---

## 1. 文件结构

### 1.1 新增文件清单（阶段1 已实现）

```
modules/abstractions/composition/node/interfaces/
  INode.cs                    — 节点统一接口 ✅
  NodeMetadata.cs             — 节点元数据 record ✅
  NodeState.cs                — 节点生命周期状态枚举 ✅
  INodeConsumer.cs            — 消费点接口 ✅
  INodeRegistry.cs            — 节点注册表接口 ✅

modules/abstractions/composition/node/profile/
  NodeProfile.cs              — Profile 数据结构 ✅
  NodeProfileOverride.cs      — Profile 覆写条目 ✅
  ResolvedProfile.cs          — 合并后的最终 Profile ✅
  ConsumerConfig.cs           — 消费点配置 ✅
  NodeEntryConfig.cs          — 消费点中单个节点配置 ✅
  ResolvedConsumerConfig.cs   — 合并后的消费点配置 ✅
  ResolvedNodeEntry.cs        — 合并后的节点条目 ✅

modules/composition/composition/node_registry/
  NodeRegistry.cs             — NodeRegistry 实现 ✅
  NodeProfileResolver.cs      — Profile 继承链解析器 ✅
  NodeProfileLoader.cs        — 内嵌资源 Profile 加载器 ✅
  NodeEntry.cs                — 节点注册条目（内部） ✅
  Node.cs                     — Node<TService> 运行期适配器 ✅

resources/profiles/
  base.yaml                   — 基础 Profile ✅
  headless.yaml               — 无头模式 Profile ✅
  sdk.yaml                    — SDK 模式 Profile ✅
  ci.yaml                     — CI 模式 Profile ✅
```

### 1.2 待实现文件清单（阶段2+）

```
modules/composition/composition/node_registry/consumers/
  PipelineConsumer.cs         — 管道消费点
  ToolDispatchConsumer.cs     — 工具分发消费点
  CommandDispatchConsumer.cs  — 命令分发消费点
  PromptAssemblyConsumer.cs   — 提示词组装消费点

modules/composition/pipelines/registration/
  PipelineComposition.cs      — 改造为数据驱动（现有文件）

modules/generators/node_handler.generator/
  NodeHandlerGenerator.cs     — handler 编译期注册生成器
  GlobalUsings.cs
  NodeHandler.Generator.csproj

modules/generators/middleware_registration.generator/
  MiddlewareRegistrationGenerator.cs — 泛型中间件自动注册生成器
  GlobalUsings.cs
  MiddlewareRegistration.Generator.csproj

modules/plugins/plugins.contracts/registry/
  DynamicPluginRegistry.cs    — 改造（现有文件）
  AotHandlerRegistry.cs       — AOT handler 注册表接口

modules/plugins/plugins.infrastructure/services/
  PluginAotHandlerRegistry.cs — AOT handler 注册表实现

modules/abstractions/primitives/core_attributes/node/
  NodeHandlerAttribute.cs     — [NodeHandler] 特性
  MiddlewareContextAttribute.cs — [MiddlewareContext] 特性

resources/profiles/           — 内嵌资源目录
  base.yaml
  headless.yaml
  sdk.yaml
  ci.yaml
```

### 1.2 改造的现有文件

| 文件 | 改造内容 |
|------|---------|
| `modules/composition/pipelines/registration/PipelineComposition.cs` | 从手动 .Use() 链改为数据驱动组装 |
| `modules/plugins/plugins.contracts/registry/DynamicPluginRegistry.cs` | LoadHandlerTable 从反射改为 AOT 注册表 |
| `modules/composition/composition/dependency_injection/core/ServiceRegistration.cs` | AddWorkflowServices 中新增 NodeRegistry 注册 |

---

## 2. 核心接口定义

### 2.1 INode — 节点统一接口

```csharp
namespace JoinCode.Abstractions.Composition;

public interface INode {
    string NodeId { get; }
    NodeMetadata Metadata { get; }
    NodeState State { get; }
}
```

### 2.2 NodeMetadata — 节点元数据

```csharp
namespace JoinCode.Abstractions.Composition;

public sealed record NodeMetadata(
    string Version,
    string[] Dependencies,
    string[] Capabilities,
    string[] RequiredCapabilities,
    int Priority,
    string? EnabledWhenFeatureFlag
);
```

### 2.3 NodeState — 生命周期状态

```csharp
namespace JoinCode.Abstractions.Composition;

public enum NodeState {
    Defined,
    Loading,
    Active,
    Unloading,
    Unloaded,
    Failed
}
```

### 2.4 NodeContext — 可逆注册上下文

```csharp
namespace JoinCode.Abstractions.Composition;

public sealed class NodeContext {
    private readonly Stack<Func<ValueTask>> _cleanupStack = new();
    private readonly IServiceProvider _services;

    internal NodeContext(IServiceProvider services) {
        _services = services;
    }

    public T GetService<T>() where T : notnull => _services.GetRequiredService<T>();

    public T RegisterEffect<T>(T value, Func<ValueTask> cleanup) {
        _cleanupStack.Push(cleanup);
        return value;
    }

    public T RegisterEffect<T>(T value, Action cleanup) {
        _cleanupStack.Push(() => { cleanup(); return ValueTask.CompletedTask; });
        return value;
    }

    public async ValueTask UnloadAsync() {
        while (_cleanupStack.TryPop(out var cleanup)) {
            try { await cleanup().ConfigureAwait(false); }
            catch (Exception) { /* 记录警告，不传播 */ }
        }
    }
}
```

### 2.5 INodeConsumer — 消费点接口

```csharp
namespace JoinCode.Abstractions.Composition;

public interface INodeConsumer {
    string ConsumerId { get; }
    IReadOnlyList<INode> ResolveNodes(NodeRegistry registry, ResolvedProfile profile);
}
```

### 2.6 INodeRegistry — 节点注册表接口

```csharp
namespace JoinCode.Abstractions.Composition;

public interface INodeRegistry {
    INode? GetNode(string nodeId);
    IReadOnlyList<INode> GetNodesForConsumer(string consumerId, ResolvedProfile profile);
    IReadOnlyList<T> GetServicesForConsumer<T>(string consumerId, ResolvedProfile profile) where T : notnull;
}
```

---

## 3. NodeProfile 数据结构

### 3.1 NodeProfile

```csharp
namespace JoinCode.Abstractions.Composition.Profile;

public sealed record NodeProfile(
    string Name,
    string? Inherits,
    FrozenDictionary<string, ConsumerConfig> Consumers,
    FrozenSet<string> Excludes,
    FrozenList<NodeProfileOverride> Overrides,
    FrozenList<string> Includes
);
```

### 3.2 ConsumerConfig — 消费点配置

```csharp
namespace JoinCode.Abstractions.Composition.Profile;

public sealed record ConsumerConfig(
    string ConsumerId,
    FrozenList<NodeEntryConfig> Nodes
);

public sealed record NodeEntryConfig(
    string Id,
    int Priority,
    string? EnabledWhenFeatureFlag,
    FrozenDictionary<string, string> Config
);
```

### 3.3 NodeProfileOverride

```csharp
namespace JoinCode.Abstractions.Composition.Profile;

public sealed record NodeProfileOverride(
    string Consumer,
    string Node,
    int? Priority,
    FrozenDictionary<string, string>? Config
);
```

### 3.4 ResolvedProfile — 合并后的最终 Profile

```csharp
namespace JoinCode.Abstractions.Composition.Profile;

public sealed record ResolvedProfile(
    string Name,
    FrozenDictionary<string, ResolvedConsumerConfig> Consumers
);

public sealed record ResolvedConsumerConfig(
    string ConsumerId,
    FrozenList<ResolvedNodeEntry> Nodes
);

public sealed record ResolvedNodeEntry(
    string Id,
    int Priority,
    string? EnabledWhenFeatureFlag,
    FrozenDictionary<string, string> Config
);
```

---

## 4. NodeProfile YAML 格式

### 4.1 base.yaml

```yaml
name: base
consumers:
  chat-pipeline:
    nodes:
      - id: stream-crash-snapshot-mw
        priority: 50
      - id: rate-limit-mw
        priority: 100
      - id: chat-error-handling-mw
        priority: 150
      - id: chat-timing-mw
        priority: 200
      - id: token-budget-mw
        priority: 250
      - id: security-check-mw
        priority: 300
      - id: audit-log-mw
        priority: 350
      - id: chat-context-mw
        priority: 400
      - id: chat-prompt-mw
        priority: 450
      - id: chat-stream-mw
        priority: 500
      - id: chat-admin-mw
        priority: 550
      - id: chat-tool-mw
        priority: 600
      - id: chat-telemetry-mw
        priority: 650
      - id: crash-snapshot-mw
        priority: 700

  preprocess-pipeline:
    nodes:
      - id: keyword-injection-mw
        priority: 100
      - id: synonym-mw
        priority: 200
      - id: system-prompt-mw
        priority: 300
      - id: lsp-diagnostic-mw
        priority: 400
      - id: crash-snapshot-mw
        priority: 50

  permission-pipeline:
    nodes:
      - id: bypass-permission-mw
        priority: 100
      - id: agent-permission-mw
        priority: 200
      - id: dangerous-command-mw
        priority: 300
      - id: auto-class-permission-mw
        priority: 400
      - id: config-permission-mw
        priority: 500
      - id: web-fetch-permission-mw
        priority: 600
      - id: path-deny-permission-mw
        priority: 700
      - id: tool-list-permission-mw
        priority: 800
      - id: path-permission-mw
        priority: 900
      - id: dangerous-op-permission-mw
        priority: 1000
      - id: plan-mode-permission-mw
        priority: 1100
      - id: default-permission-mw
        priority: 1200
      - id: crash-snapshot-mw
        priority: 50

  shell-pipeline:
    nodes:
      - id: shell-timeout-mw
        priority: 100
      - id: shell-validate-mw
        priority: 200
      - id: shell-intercept-mw
        priority: 300
      - id: shell-path-gate-mw
        priority: 400
      - id: shell-classify-mw
        priority: 500
      - id: shell-sed-intercept-mw
        priority: 600
      - id: shell-background-mw
        priority: 700
      - id: shell-build-intercept-mw
        priority: 800
      - id: shell-download-hint-mw
        priority: 900
      - id: shell-execute-mw
        priority: 1000
      - id: shell-output-mw
        priority: 1100
      - id: shell-perturbation-audit-mw
        priority: 1200
      - id: crash-snapshot-mw
        priority: 50

  tool-dispatch:
    nodes:
      - id: shell-tool
      - id: fs-tool
      - id: web-tool
        enabled-when: feature.web-search
      - id: code-tool

  command-dispatch:
    nodes:
      - id: shell-command
      - id: fs-command

  prompt-assembly:
    nodes:
      - id: system-prompt
      - id: security-prompt
      - id: tool-guidance-prompt
      - id: memory-prompt
```

### 4.2 headless.yaml

```yaml
name: headless
inherits: base
excludes:
  - gui-module
  - theme-command
  - vim-mode-plugin
  - memory-prompt
```

### 4.3 sdk.yaml

```yaml
name: sdk
inherits: headless
excludes:
  - web-tool
  - metrics-mw
  - crash-snapshot-mw
includes:
  consumers:
    tool-dispatch:
      nodes:
        - id: sdk-api-tool
overrides:
  - consumer: shell-pipeline
    node: shell-timeout-mw
    config:
      timeout: 600
```

---

## 5. NodeRegistry 实现

### 5.1 NodeRegistry

```csharp
namespace JoinCode.Composition.NodeRegistry;

public sealed class NodeRegistry : INodeRegistry {
    private readonly FrozenDictionary<string, NodeEntry> _nodes;
    private readonly IServiceProvider _services;

    private NodeRegistry(IServiceProvider services, FrozenDictionary<string, NodeEntry> nodes) {
        _services = services;
        _nodes = nodes;
    }

    public static NodeRegistry Build(IServiceProvider sp) {
        // 从 DI 容器收集所有已注册的服务
        // 每个服务包装为 NodeEntry
        // NodeId = 类名（或 [Register] 的接口类型名）
        var nodes = new Dictionary<string, NodeEntry>();
        // ... 扫描 DI 容器 ...
        return new NodeRegistry(sp, nodes.ToFrozenDictionary());
    }

    public INode? GetNode(string nodeId) {
        if (!_nodes.TryGetValue(nodeId, out var entry)) return null;
        return entry.CreateNode(_services);
    }

    public IReadOnlyList<INode> GetNodesForConsumer(string consumerId, ResolvedProfile profile) {
        if (!profile.Consumers.TryGetValue(consumerId, out var consumerConfig)) return [];
        var result = new List<INode>();
        foreach (var nodeEntry in consumerConfig.Nodes.OrderBy(n => n.Priority)) {
            if (nodeEntry.EnabledWhenFeatureFlag is not null) {
                var flagService = _services.GetService<IFeatureFlagService>();
                if (flagService is not null && !flagService.IsEnabled(nodeEntry.EnabledWhenFeatureFlag))
                    continue;
            }
            if (_nodes.TryGetValue(nodeEntry.Id, out var entry))
                result.Add(entry.CreateNode(_services));
        }
        return result;
    }

    public IReadOnlyList<T> GetServicesForConsumer<T>(string consumerId, ResolvedProfile profile) where T : notnull {
        var nodes = GetNodesForConsumer(consumerId, profile);
        var result = new List<T>();
        foreach (var node in nodes) {
            if (node is Node<T> typedNode)
                result.Add(typedNode.Service);
        }
        return result;
    }
}
```

### 5.2 NodeEntry — 内部注册条目

```csharp
namespace JoinCode.Composition.NodeRegistry;

internal sealed class NodeEntry {
    private readonly Func<IServiceProvider, INode> _factory;

    private NodeEntry(string nodeId, Func<IServiceProvider, INode> factory) {
        NodeId = nodeId;
        _factory = factory;
    }

    public string NodeId { get; }

    public INode CreateNode(IServiceProvider sp) => _factory(sp);

    public static NodeEntry Create<TService>(string nodeId) where TService : notnull {
        return new NodeEntry(nodeId, sp => {
            var service = sp.GetRequiredService<TService>();
            return new Node<TService>(nodeId, service);
        });
    }
}
```

### 5.3 Node<TService> — 运行期节点适配器

```csharp
namespace JoinCode.Composition.NodeRegistry;

internal sealed class Node<TService> : INode {
    public string NodeId { get; }
    public NodeMetadata Metadata { get; }
    public NodeState State { get; internal set; }
    public TService Service { get; }

    internal Node(string nodeId, TService service) {
        NodeId = nodeId;
        Service = service;
        State = NodeState.Active;
        Metadata = new NodeMetadata(
            Version: "1.0",
            Dependencies: [],
            Capabilities: [],
            RequiredCapabilities: [],
            Priority: 0,
            EnabledWhenFeatureFlag: null
        );
    }
}
```

---

## 6. NodeProfileResolver — 省略即继承合并器

```csharp
namespace JoinCode.Composition.NodeRegistry;

public sealed class NodeProfileResolver {
    private readonly FrozenDictionary<string, NodeProfile> _profiles;

    public NodeProfileResolver(FrozenDictionary<string, NodeProfile> profiles) {
        _profiles = profiles;
    }

    public ResolvedProfile Resolve(string profileName) {
        if (!_profiles.TryGetValue(profileName, out var profile))
            throw new ArgumentException($"Profile '{profileName}' not found");

        if (profile.Inherits is null) return ResolveSelf(profile);
        var parent = Resolve(profile.Inherits);
        return Merge(parent, profile);
    }

    private ResolvedProfile ResolveSelf(NodeProfile profile) {
        var consumers = new Dictionary<string, ResolvedConsumerConfig>();
        foreach (var (consumerId, config) in profile.Consumers) {
            var nodes = config.Nodes
                .Select(n => new ResolvedNodeEntry(n.Id, n.Priority, n.EnabledWhenFeatureFlag, n.Config))
                .ToFrozenList();
            consumers[consumerId] = new ResolvedConsumerConfig(consumerId, nodes);
        }
        return new ResolvedProfile(profile.Name, consumers.ToFrozenDictionary());
    }

    private ResolvedProfile Merge(ResolvedProfile parent, NodeProfile child) {
        var consumers = new Dictionary<string, ResolvedConsumerConfig>(parent.Consumers);

        // 1. 排除子 profile 明确移除的节点（从所有消费点中移除）
        foreach (var nodeId in child.Excludes) {
            foreach (var (consumerId, consumerConfig) in consumers) {
                var filtered = consumerConfig.Nodes.Where(n => n.Id != nodeId).ToFrozenList();
                consumers[consumerId] = consumerConfig with { Nodes = filtered };
            }
        }

        // 2. 添加子 profile 明确新增的消费点或节点
        foreach (var (consumerId, config) in child.Includes) {
            if (consumers.TryGetValue(consumerId, out var existing)) {
                var combined = existing.Nodes.Concat(
                    config.Nodes.Select(n => new ResolvedNodeEntry(n.Id, n.Priority, n.EnabledWhenFeatureFlag, n.Config))
                ).ToFrozenList();
                consumers[consumerId] = existing with { Nodes = combined };
            } else {
                var nodes = config.Nodes
                    .Select(n => new ResolvedNodeEntry(n.Id, n.Priority, n.EnabledWhenFeatureFlag, n.Config))
                    .ToFrozenList();
                consumers[consumerId] = new ResolvedConsumerConfig(consumerId, nodes);
            }
        }

        // 3. 覆写子 profile 明确修改的节点属性
        foreach (var overrideEntry in child.Overrides) {
            if (consumers.TryGetValue(overrideEntry.Consumer, out var consumerConfig)) {
                var nodes = consumerConfig.Nodes.Select(n =>
                    n.Id == overrideEntry.Node
                        ? n with {
                            Priority = overrideEntry.Priority ?? n.Priority,
                            Config = overrideEntry.Config ?? n.Config
                        }
                        : n
                ).ToFrozenList();
                consumers[overrideEntry.Consumer] = consumerConfig with { Nodes = nodes };
            }
        }

        return new ResolvedProfile(child.Name, consumers.ToFrozenDictionary());
    }
}
```

---

## 7. 数据驱动的管道组装

### 7.1 PipelineConsumer — 管道消费点

```csharp
namespace JoinCode.Composition.NodeRegistry.Consumers;

public sealed class PipelineConsumer : INodeConsumer {
    public string ConsumerId { get; }

    public PipelineConsumer(string pipelineId) {
        ConsumerId = pipelineId;
    }

    public IReadOnlyList<INode> ResolveNodes(NodeRegistry registry, ResolvedProfile profile) {
        return registry.GetNodesForConsumer(ConsumerId, profile);
    }
}
```

### 7.2 数据驱动的 PipelineComposition.cs

```csharp
// 改造后的 PipelineComposition.cs
public static class PipelineComposition {
    public static IServiceCollection AddPipelines(this IServiceCollection services) {
        services.AddSingleton<INodeRegistry>(sp => NodeRegistry.Build(sp));
        services.AddSingleton<NodeProfileResolver>(sp => NodeProfileLoader.LoadFromResources());
        services.AddSingleton<ResolvedProfile>(sp => {
            var resolver = sp.GetRequiredService<NodeProfileResolver>();
            var profileName = sp.GetRequiredService<WorkflowConfig>().Profile ?? "base";
            return resolver.Resolve(profileName);
        });

        // 注册管道 — 从 Profile 数据驱动组装
        services.AddSingleton(sp => {
            var registry = sp.GetRequiredService<INodeRegistry>();
            var profile = sp.GetRequiredService<ResolvedProfile>();
            var middlewares = registry.GetServicesForConsumer<IMiddleware<ChatMiddlewareContext>>("chat-pipeline", profile);
            return BuildStreamPipeline<ChatMiddlewareContext, ChatStreamEvent>(middlewares, ...);
        });

        // ... 其他 15 条管道同样数据驱动 ...

        return services;
    }
}
```

---

## 8. DynamicPluginRegistry AOT 改造

### 8.1 AotHandlerRegistry 接口

```csharp
namespace JoinCode.Abstractions.Entity;

public interface IAotHandlerRegistry {
    FrozenDictionary<string, Func<object?[], object?>>? GetHandlers(string nodeId);
}
```

### 8.2 CompiledNodeHandlers — 源码生成器产出

```csharp
// 由 NodeHandlerGenerator 在编译期生成
internal static class CompiledNodeHandlers {
    private static readonly FrozenDictionary<string, FrozenDictionary<string, Func<object?[], object?>>> _handlers =
        FrozenDictionary<string, FrozenDictionary<string, Func<object?[], object?>>>.Empty
            .Add("shell-tool", FrozenDictionary<string, Func<object?[], object?>>.Empty
                .Add("execute", args => ShellToolHandlers.Execute((string)args[0]!))
                .Add("validate", args => ShellToolHandlers.Validate((string)args[0]!)));

    public static FrozenDictionary<string, Func<object?[], object?>>? GetHandlers(string nodeId) =>
        _handlers.TryGetValue(nodeId, out var handlers) ? handlers : null;
}
```

### 8.3 DynamicPluginRegistry.Invoke 改造

```csharp
public InvokeResult Invoke(string name, string method, object?[] args) {
    // AOT 路径：从编译期生成的 handler 表路由
    var handlers = CompiledNodeHandlers.GetHandlers(name);
    if (handlers is not null && handlers.TryGetValue(method, out var handler)) {
        try { return InvokeResult.Ok(handler(args)); }
        catch (Exception ex) { return InvokeResult.HandlerError(ex); }
    }

    // 降级路径：运行时反射（非 AOT 模式）
    if (_runtime is not null) {
        return _runtime.Invoke(name, method, args);
    }

    return InvokeResult.MethodNotFound;
}
```

---

## 9. 泛型中间件自动注册

### 9.1 MiddlewareContextAttribute

```csharp
namespace JoinCode.Abstractions.CoreAttributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class MiddlewareContextAttribute : Attribute {
    public Type ContextType { get; }
    public string PipelineId { get; }
    public int Priority { get; }

    public MiddlewareContextAttribute(Type contextType, string pipelineId, int priority = 0) {
        ContextType = contextType;
        PipelineId = pipelineId;
        Priority = priority;
    }
}
```

### 9.2 使用示例

```csharp
[MiddlewareContext(typeof(ChatMiddlewareContext), "chat-pipeline", 50)]
public class CrashSnapshotMiddleware<TContext> : IMiddleware<TContext> { ... }

// 源码生成器自动生成：
// services.AddSingleton(sp => new CrashSnapshotMiddleware<ChatMiddlewareContext>(...));
```

### 9.3 MiddlewareRegistrationGenerator

扫描 `[MiddlewareContext]` 特性，为每个 (TContext, TPipeline) 组合生成泛型实例化注册代码。

---

## 10. 渐进式分阶段计划

### 10.1 阶段划分原则

- **新旧并行** — 每个阶段新旧系统可并行运行，不破坏现有功能
- **独立可验证** — 每个阶段完成后编译通过 + 测试通过
- **先增后删** — 先新增替代方案，验证通过后再删除旧代码
- **每阶段一个 PR** — 便于 review 和回滚

### 10.2 阶段详情

#### 阶段1：新增基础设施（纯增量，不改动现有代码）

| 步骤 | 内容 | 文件 |
|------|------|------|
| 1.1 | INode + NodeMetadata + NodeState | abstractions.interfaces/interfaces/composition/ |
| 1.2 | NodeContext（可逆注册） | abstractions.interfaces/interfaces/composition/ |
| 1.3 | NodeProfile + ConsumerConfig + NodeProfileOverride + ResolvedProfile | abstractions.interfaces/interfaces/composition/profile/ |
| 1.4 | INodeConsumer + INodeRegistry | abstractions.interfaces/interfaces/composition/ |
| 1.5 | NodeProfileLoader + NodeProfileResolver | composition/node_registry/ |
| 1.6 | NodeRegistry + NodeEntry + Node<T> | composition/node_registry/ |
| 1.7 | base.yaml + headless.yaml + sdk.yaml + ci.yaml | resources/profiles/ |

**验收**: 编译通过，新接口和实现类无警告。现有功能不受影响。

#### 阶段2：数据驱动的管道组装（新旧并行）

| 步骤 | 内容 | 文件 |
|------|------|------|
| 2.1 | PipelineConsumer + 其他消费点 | composition/node_registry/consumers/ |
| 2.2 | PipelineComposition.cs 改造 — 新增数据驱动路径，保留旧路径作为降级 | composition/pipelines/registration/ |
| 2.3 | ServiceRegistration 注册 NodeRegistry + NodeProfileResolver | composition/dependency_injection/core/ |
| 2.4 | 验证数据驱动组装的管道与手动组装的管道行为一致 | 测试 |

**验收**: 数据驱动组装的管道与手动组装的管道行为一致。通过 `--profile base` 使用数据驱动，通过 `--profile legacy` 使用旧的手动组装。

#### 阶段3：泛型中间件自动注册（新旧并行）

| 步骤 | 内容 | 文件 |
|------|------|------|
| 3.1 | MiddlewareContextAttribute | primitives/core_attributes/node/ |
| 3.2 | MiddlewareRegistrationGenerator | generators/middleware_registration.generator/ |
| 3.3 | 在现有中间件类上标注 [MiddlewareContext] | 各中间件文件 |
| 3.4 | PipelineComposition.cs 中移除手动泛型实例化，改用生成器产出 | composition/pipelines/registration/ |

**验收**: CrashSnapshotMiddleware/MetricsMiddleware 等泛型中间件不再手动注册。编译通过，测试通过。

#### 阶段4：DynamicPluginRegistry AOT 改造（新旧并行）

| 步骤 | 内容 | 文件 |
|------|------|------|
| 4.1 | NodeHandlerAttribute | primitives/core_attributes/node/ |
| 4.2 | NodeHandlerGenerator | generators/node_handler.generator/ |
| 4.3 | IAotHandlerRegistry 接口 | plugins.contracts/registry/ |
| 4.4 | PluginAotHandlerRegistry 实现 | plugins.infrastructure/services/ |
| 4.5 | DynamicPluginRegistry.Invoke 改造 — AOT 路径优先，反射路径降级 | plugins.contracts/registry/DynamicPluginRegistry.cs |

**验收**: AOT 下 Invoke 能路由到 handler。非 AOT 模式反射路径仍可用。

#### 阶段5：删除旧代码（仅在阶段2-4验证通过后）

| 步骤 | 内容 | 文件 |
|------|------|------|
| 5.1 | 移除 PipelineComposition.cs 的手动 .Use() 链（已被数据驱动替代） | composition/pipelines/registration/ |
| 5.2 | 移除 PluginAlc（AOT 下不可用，已被 AOT handler 注册表替代） | plugins.contracts/plugin_core/PluginAlc.cs → .xxx/ |
| 5.3 | 移除 AlcUnloadVerifier（依赖 ALC，AOT 下无意义） | plugins.contracts/plugin_core/AlcUnloadVerifier.cs → .xxx/ |
| 5.4 | 移除 DynamicPluginRegistry.LoadHandlerTable 的反射路径 | plugins.contracts/registry/DynamicPluginRegistry.cs |
| 5.5 | 移除 ServiceRegistration.Mcp.cs 中 ToolExecution 管道手动组装 | composition/dependency_injection/infrastructure/ |

**验收**: 全量编译通过，所有测试通过。AOT 发布测试通过。

#### 阶段6：清理硬编码点（可选优化）

| 步骤 | 内容 | 文件 |
|------|------|------|
| 6.1 | PromptTemplateGenerator.InferClassName 改为特性标注驱动 | generators/prompt_template.generator/ |
| 6.2 | McpToolDispatchGenerator.DetectTimeoutPolicy 改为类型符号比较 | generators/mcp_tool_dispatch.generator/ |
| 6.3 | PromptSectionGenerator 硬编码正则改为特性标注收集 | generators/prompt_section.generator/ |

**验收**: 编译通过，测试通过。

#### 阶段7：统一 Registry（可选优化，长期目标）

| 步骤 | 内容 | 文件 |
|------|------|------|
| 7.1 | CommandRegistry 统一到 NodeRegistry 消费点模式 | composition/ |
| 7.2 | ScenarioRegistry 统一到 NodeRegistry 消费点模式 | composition/ |
| 7.3 | ToolSecuritySets 统一到 NodeRegistry 消费点模式 | composition/ |

**验收**: 编译通过，测试通过。

### 10.3 阶段依赖图

```
阶段1（基础设施）→ 阶段2（管道组装）→ 阶段5（删除旧代码）
                 → 阶段3（泛型中间件）↗
                 → 阶段4（AOT 改造）↗
阶段6（硬编码清理）— 独立，可并行
阶段7（统一 Registry）— 长期目标，依赖阶段1-5完成
```

### 10.4 每阶段 PR 要求

- 每个阶段一个 PR，目标分支 main
- PR 标题格式：`feat: DSG048 阶段N - 描述`
- PR 必须包含：编译验证 + 测试验证 + AOT 验证（阶段4-5）
- PR 合并后下一阶段才开始

---

## 11. 验收标准 — exe 启动验证方案

> **核心原则**：每个阶段的验收必须通过实际启动 `jcc.exe` 验证，不能用单元测试当挡箭牌。
> 单元测试是开发期间的快速反馈工具，但最终验收必须走真实 exe 启动路径。

### 11.0 前置：新增诊断命令

实现阶段1时同步新增以下诊断命令到 `jcc.exe`：

| 命令 | 说明 | 实现方式 |
|------|------|---------|
| `jcc --dump-nodes` | 输出所有已注册节点的 ID、状态、所属消费点 | 新增 `[CliOption]` 参数，从 NodeRegistry 导出 |
| `jcc --dump-profile [name]` | 输出指定 Profile 解析后的完整消费点→节点映射 | 新增 `[CliOption]` 参数，从 NodeProfileResolver 导出 |
| `jcc --profile <name>` | 指定运行时使用的 Profile（默认 base） | 新增 `[CliOption]` 参数，传入 WorkflowConfig |

`--dump-nodes` 和 `--dump-profile` 是诊断输出模式，输出后立即退出（不进入交互/对话流程），类似 `--help`/`--version` 的行为模式。

### 11.1 阶段1验收 — 基础设施

**验收方式**：编译 + `--dump-nodes` 诊断输出

```powershell
# 1. 编译 jcc.exe（Debug 模式）
dotnet build modules/apps/cli/JoinCode.csproj -c Debug

# 2. 启动 jcc.exe --dump-nodes，验证节点注册表能正常输出
./modules/apps/cli/bin/Debug/net10.0/jcc.exe --dump-nodes

# 期望输出：
# Node Registry Dump (Profile: base)
# ================================
# Consumer: chat-pipeline
#   [50] stream-crash-snapshot-mw  State: Active
#   [100] rate-limit-mw            State: Active
#   ...
# Consumer: tool-dispatch
#   [0] shell-tool                 State: Active
#   [0] fs-tool                    State: Active
#   ...

# 3. 启动 jcc.exe --dump-profile base，验证 Profile 解析链
./modules/apps/cli/bin/Debug/net10.0/jcc.exe --dump-profile base

# 期望输出：
# Profile: base (resolved from base.yaml)
# Consumer: chat-pipeline
#   stream-crash-snapshot-mw  priority=50
#   rate-limit-mw             priority=100
#   ...

# 4. 启动 jcc.exe --dump-profile headless，验证继承链解析
./modules/apps/cli/bin/Debug/net10.0/jcc.exe --dump-profile headless

# 期望输出：
# Profile: headless (inherits: base, excludes: [gui-module, theme-command, ...])
# Consumer: chat-pipeline
#   stream-crash-snapshot-mw  priority=50
#   ...（base 的所有节点，减去 excludes 列表中的）
```

**判定标准**：
- `--dump-nodes` 输出非空，包含所有现有 `[Register]` 注册的服务
- `--dump-profile base` 输出与 base.yaml 内容一致
- `--dump-profile headless` 输出 = base 减去 excludes 列表
- 现有功能不受影响（`jcc --help` 正常、`jcc -p "hello" --trust --await 10` 正常对话）

### 11.2 阶段2验收 — 数据驱动管道组装

**验收方式**：新旧路径对比 + 真实对话验证

```powershell
# 1. 编译
dotnet build modules/apps/cli/JoinCode.csproj -c Debug

# 2. 旧路径（legacy）— 手动组装管道，正常对话
./modules/apps/cli/bin/Debug/net10.0/jcc.exe --profile legacy -p "echo hello" --trust --await 15

# 期望：正常返回对话结果，exit code 0

# 3. 新路径（base）— 数据驱动组装管道，同样对话
./modules/apps/cli/bin/Debug/net10.0/jcc.exe --profile base -p "echo hello" --trust --await 15

# 期望：正常返回对话结果，exit code 0，行为与 legacy 一致

# 4. 对比 --dump-nodes 输出，验证新旧路径节点列表一致
./modules/apps/cli/bin/Debug/net10.0/jcc.exe --profile legacy --dump-nodes > .xxx/nodes_legacy.txt
./modules/apps/cli/bin/Debug/net10.0/jcc.exe --profile base --dump-nodes > .xxx/nodes_base.txt

# diff 两个文件，节点列表应一致（顺序可能因 priority 排序而调整，但集合相同）

# 5. headless profile 验证 — 排除的节点不应出现
./modules/apps/cli/bin/Debug/net10.0/jcc.exe --profile headless --dump-nodes

# 期望：gui-module、theme-command、vim-mode-plugin、memory-prompt 不在输出中

# 6. E2E 验证 — 启动 MockServer + jcc.exe 走完整对话链路
# 使用现有 test/mock/scripts/run_e2e_test.ps1，但加上 --profile base 参数
```

**判定标准**：
- `--profile base` 与 `--profile legacy` 产生相同的对话行为
- `--dump-nodes` 对比：新旧路径的节点集合一致
- `--profile headless` 正确排除节点
- E2E 对话链路正常（MockServer + jcc.exe）

### 11.3 阶段3验收 — 泛型中间件自动注册

**验收方式**：`--dump-nodes` 验证自动注册 + 对话验证

```powershell
# 1. 编译（需要 --no-incremental，因为涉及源码生成器）
dotnet build modules/apps/cli/JoinCode.csproj -c Debug --no-incremental

# 2. --dump-nodes 验证泛型中间件已自动注册
./modules/apps/cli/bin/Debug/net10.0/jcc.exe --dump-nodes

# 期望：CrashSnapshotMiddleware<ChatMiddlewareContext> 等泛型中间件出现在 chat-pipeline 消费点中
# 且不再需要 PipelineComposition.cs 中手动注册

# 3. 对话验证 — crash-snapshot 中间件实际生效
./modules/apps/cli/bin/Debug/net10.0/jcc.exe --profile base -p "echo hello" --trust --await 15

# 期望：正常对话，crash-snapshot 中间件在管道中生效（可通过 --debuglog 验证中间件执行顺序）

# 4. --debuglog 验证中间件执行链
./modules/apps/cli/bin/Debug/net10.0/jcc.exe --profile base -p "echo hello" --trust --await 15 --debuglog

# 期望：日志中可见中间件按 priority 顺序执行
```

**判定标准**：
- `--dump-nodes` 中泛型中间件自动出现，无需手动注册
- `--debuglog` 日志显示中间件按 priority 顺序执行
- 对话行为与阶段2一致

### 11.4 阶段4验收 — DynamicPluginRegistry AOT 改造

**验收方式**：AOT 发布 + invoke 路由验证

```powershell
# 1. Debug 模式验证（非 AOT）
dotnet build modules/apps/cli/JoinCode.csproj -c Debug --no-incremental

# 2. 对话验证 — 工具调用走 AOT handler 路径
./modules/apps/cli/bin/Debug/net10.0/jcc.exe --profile base -p "list files in current directory" --trust --await 30

# 期望：shell-tool/fs-tool 的 invoke 调用成功，不再返回 MethodNotFound

# 3. AOT 发布
dotnet publish modules/apps/cli/JoinCode.csproj -c Release -r win-x64

# 4. AOT exe 启动验证
./modules/apps/cli/bin/Release/net10.0/win-x64/publish/jcc.exe --dump-nodes

# 期望：AOT 模式下 --dump-nodes 正常输出（证明 NodeRegistry 在 AOT 下工作）

# 5. AOT exe 对话验证
./modules/apps/cli/bin/Release/net10.0/win-x64/publish/jcc.exe --profile base -p "echo hello" --trust --await 30

# 期望：AOT 模式下对话正常，工具调用走 CompiledNodeHandlers 路径

# 6. AOT exe --dump-profile 验证
./modules/apps/cli/bin/Release/net10.0/win-x64/publish/jcc.exe --dump-profile sdk

# 期望：sdk profile 继承链正确解析（sdk → headless → base）
```

**判定标准**：
- Debug 模式：工具 invoke 调用成功
- AOT 发布成功（无 trim 警告）
- AOT exe `--dump-nodes` 正常输出
- AOT exe 对话正常，工具调用走编译期生成的 handler 表

### 11.5 阶段5验收 — 删除旧代码

**验收方式**：全量编译 + AOT 发布 + 完整功能验证

```powershell
# 1. 全量编译（16 域）
pwsh non_deliverables_tools/scripts/Build-AllDomains.ps1

# 2. jcc.exe Debug 启动验证
./modules/apps/cli/bin/Debug/net10.0/jcc.exe --dump-nodes
./modules/apps/cli/bin/Debug/net10.0/jcc.exe --dump-profile base
./modules/apps/cli/bin/Debug/net10.0/jcc.exe --dump-profile headless
./modules/apps/cli/bin/Debug/net10.0/jcc.exe --dump-profile sdk

# 3. jcc.exe 对话验证（所有 profile）
./modules/apps/cli/bin/Debug/net10.0/jcc.exe --profile base -p "echo hello" --trust --await 15
./modules/apps/cli/bin/Debug/net10.0/jcc.exe --profile headless -p "echo hello" --trust --await 15

# 4. AOT 发布
dotnet publish modules/apps/cli/JoinCode.csproj -c Release -r win-x64

# 5. AOT exe 完整验证
./modules/apps/cli/bin/Release/net10.0/win-x64/publish/jcc.exe --dump-nodes
./modules/apps/cli/bin/Release/net10.0/win-x64/publish/jcc.exe --profile base -p "echo hello" --trust --await 30

# 6. E2E 测试（MockServer + jcc.exe）
pwsh test/mock/scripts/run_e2e_test.ps1

# 7. 验证旧代码确实已移除
# PipelineComposition.cs 中不再有手动 .Use() 链
# PluginAlc.cs 已移到 .xxx/
# AlcUnloadVerifier.cs 已移到 .xxx/
```

**判定标准**：
- 16 域全量编译通过
- 所有 profile 的 `--dump-nodes`/`--dump-profile` 正常输出
- 所有 profile 的对话正常
- AOT 发布成功 + AOT exe 对话正常
- E2E 测试通过
- 旧代码已移除（git diff 确认）

### 11.6 阶段6验收 — 硬编码清理

**验收方式**：编译 + 对话 + 源码生成器验证

```powershell
# 1. 编译（--no-incremental，因为涉及源码生成器变更）
dotnet build modules/apps/cli/JoinCode.csproj -c Debug --no-incremental

# 2. 对话验证
./modules/apps/cli/bin/Debug/net10.0/jcc.exe --profile base -p "echo hello" --trust --await 15

# 3. --dump-nodes 验证节点列表不变
./modules/apps/cli/bin/Debug/net10.0/jcc.exe --dump-nodes

# 期望：与阶段5的 --dump-nodes 输出一致（硬编码清理不应改变行为）
```

**判定标准**：行为不变，编译通过

### 11.7 阶段7验收 — 统一 Registry

**验收方式**：编译 + `--dump-nodes` 验证统一消费点

```powershell
# 1. 编译
dotnet build modules/apps/cli/JoinCode.csproj -c Debug --no-incremental

# 2. --dump-nodes 验证所有消费点统一管理
./modules/apps/cli/bin/Debug/net10.0/jcc.exe --dump-nodes

# 期望：command-dispatch、prompt-assembly 等消费点也在 NodeRegistry 中统一管理
# 不再有独立的 CommandRegistry/ScenarioRegistry

# 3. 对话验证
./modules/apps/cli/bin/Debug/net10.0/jcc.exe --profile base -p "echo hello" --trust --await 15

# 4. AOT 发布验证
dotnet publish modules/apps/cli/JoinCode.csproj -c Release -r win-x64
./modules/apps/cli/bin/Release/net10.0/win-x64/publish/jcc.exe --dump-nodes
```

**判定标准**：所有消费点统一在 NodeRegistry 管理，行为不变

### 11.8 验收方案依赖的新增命令实现

#### --dump-nodes 实现路径

```
CliArg 枚举新增 DumpNodes 项
  → CliOptionGenerator 自动生成 Parser 中的 DumpNodes 属性
  → Program.cs 中检测 --dump-nodes，走诊断输出分支
  → 从 DI 容器获取 INodeRegistry + ResolvedProfile
  → 输出格式化文本到 stdout
  → exit code 0
```

#### --dump-profile 实现路径

```
CliArg 枚举新增 DumpProfile 项（AcceptsValue = true）
  → CliOptionGenerator 自动生成 Parser 中的 DumpProfile 属性
  → Program.cs 中检测 --dump-profile <name>
  → 从 DI 容器获取 NodeProfileResolver
  → resolver.Resolve(name) → 输出格式化文本
  → exit code 0
```

#### --profile 实现路径

```
CliArg 枚举新增 Profile 项（AcceptsValue = true, 默认 "base"）
  → CliOptionGenerator 自动生成 Parser 中的 Profile 属性
  → Program.cs 中将 Profile 值传入 WorkflowConfig
  → WorkflowConfig.Profile 驱动 NodeProfileResolver.Resolve()
  → ResolvedProfile 注入 DI 容器
  → 所有消费点从 ResolvedProfile 获取节点列表
```

### 11.9 验收检查清单（每阶段通用）

每个阶段验收时必须完成以下检查：

| 检查项 | 命令 | 通过标准 |
|--------|------|---------|
| 编译 | `dotnet build <csproj> -c Debug` | exit code 0，无警告 |
| exe 启动 | `jcc.exe --dump-nodes` | 输出非空，格式正确 |
| exe 对话 | `jcc.exe --profile <name> -p "echo hello" --trust --await 15` | exit code 0，有对话输出 |
| Profile 解析 | `jcc.exe --dump-profile <name>` | 输出与 YAML 配置一致 |
| 现有功能不破坏 | `jcc.exe --help` | 帮助信息正常输出 |
| AOT（阶段4+） | `dotnet publish -c Release -r win-x64` + 启动 | 发布成功，AOT exe 正常运行 |
