# DSG048 实现设计 — 详细类图与文件结构

> 📍 **导航**: [docs/](../README.md) › [design/](README.md) | **前置**: [DSG048](DSG048-node-plugin-inheritance-architecture.md)
> 状态: accepted（P1-P4 + 阶段5.5 + 阶段6 + P6 + 阶段7 已实现并验收）
> 创建: 2026-11-12
> 关联: [DSG048](DSG048-node-plugin-inheritance-architecture.md) 架构 spec

---

## 1. 文件结构

### 1.1 新增文件清单（阶段1-4 已实现）

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
  NodeServiceResolver.cs      — (consumerId,nodeId) 组合 key 服务解析器 ✅

modules/composition/pipelines/registration/
  DataDrivenPipelineComposition.cs — 数据驱动管道组装（唯一路径） ✅

resources/profiles/
  base.yaml                   — 基础 Profile（16条管道消费点） ✅
  headless.yaml               — 无头模式 Profile ✅
  sdk.yaml                    — SDK 模式 Profile ✅
  ci.yaml                     — CI 模式 Profile ✅
```

### 1.2 阶段7 新增文件（统一 Registry）

```
modules/composition/composition/dependency_injection/core/
  RegistryHub.cs                          — IRegistryHub 聚合门面实现 ✅

modules/composition/composition/dependency_injection/infrastructure/
  PluginFileWatcherHostedService.cs       — 插件文件监控 IHostedService ✅

modules/plugins/plugins.infrastructure/services/
  PluginServiceRegistration.cs            — 动态插件服务 DI 注册扩展 ✅
```

### 1.3 阶段7 改造的现有文件

| 文件 | 改造内容 | 状态 |
|------|---------|------|
| `modules/composition/composition/dependency_injection/core/ServiceRegistration.cs` | 添加 IRegistryHub + FileReadListenerRegistry + AddDynamicPluginServices 注册 | ✅ |
| `modules/composition/composition/GlobalUsings.cs` | 添加 JoinCode.Plugins.Infrastructure.Services 命名空间 | ✅ |
| `modules/abstractions/infrastructure/utils/collections/CategorizedRegistry.cs` | CachedRegistry → MapRegistry(trackCanonical: true) | ✅ |
| `modules/abstractions/infrastructure/utils/collections/CachedRegistry.cs` | 归档到 .xxx/（向后兼容别名消除） | ✅ 归档 |
| `test/unit/infra.tests/utils/collections/RegistryTests.cs` | CachedRegistryTests → MapRegistryCanonicalTests | ✅ |

### 1.4 待实现文件清单（阶段2+）

```
modules/composition/composition/node_registry/consumers/
  PipelineConsumer.cs         — 管道消费点
  ToolDispatchConsumer.cs     — 工具分发消费点
  CommandDispatchConsumer.cs  — 命令分发消费点
  PromptAssemblyConsumer.cs   — 提示词组装消费点

modules/plugins/plugins.contracts/registry/
  DynamicPluginRegistry.cs    — 改造（现有文件）
  AotHandlerRegistry.cs       — AOT handler 注册表接口

modules/plugins/plugins.infrastructure/services/
  PluginAotHandlerRegistry.cs — AOT handler 注册表实现
```

### 1.2 改造的现有文件

| 文件 | 改造内容 | 状态 |
|------|---------|------|
| `modules/composition/pipelines/registration/PipelineComposition.cs` | 从手动 .Use() 链改为数据驱动组装 → 阶段4 归档到 .xxx/ | ✅ 归档 |
| `modules/apps/engine/app/modules/CoreModule.cs` | 始终用 AddDataDrivenPipelines(profile ?? "base") | ✅ |
| `modules/apps/cli/core/services/CliArg.cs` | --profile EnvVar 从 JCC_PROFILE 改为 JCC_NODE_PROFILE | ✅ |
| `modules/abstractions/primitives.configuration/src/app_data/JccEnvVar.cs` | 新增 NodeProfile 枚举项 | ✅ |
| `native_gui/tests/native_gui.host.tests/hosting/JccChatSessionAssemblyTests.cs` | AddAllPipelines → AddDataDrivenPipelines("base") | ✅ |
| `modules/composition/pipelines/registration/TestPipelineRegistration.cs` | 注释更新 | ✅ |

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

## 8. DynamicPluginRegistry AOT 改造（YAML 配置驱动）

> **设计方向调整**（2026-11-13）：不新增 `[NodeHandler]` 特性。配置参数通过 YAML `config` 字段配置，handler 实现通过现有接口约定识别。

### 8.1 AotHandlerRegistry 接口

```csharp
namespace JoinCode.Abstractions.Entity;

public interface IAotHandlerRegistry {
    FrozenDictionary<string, Func<object?[], object?>>? GetHandlers(string nodeId);
}
```

### 8.2 CompiledNodeHandlers — 源码生成器产出

源码生成器扫描实现了现有接口（如 `[Register]` 注册的服务）的类，结合 YAML 配置中声明的 handler 映射，生成编译期路由表：

```csharp
// 由源码生成器在编译期生成（数据源：YAML 配置 + 接口实现扫描）
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

## 9. 泛型中间件 YAML config 配置

> **设计方向调整**（2026-11-13）：不新增 `[MiddlewareContext]` 特性。配置参数通过 YAML `config` 字段配置，泛型类型参数通过消费点 ID 自动映射。

### 9.1 YAML config 字段配置构造参数

```yaml
# base.yaml — config 字段配置泛型中间件的构造参数
preprocess-pipeline:
  nodes:
    - id: crash-snapshot-mw
      priority: 50
      config:
        pipeline-name: Preprocess

chat-pipeline:
  nodes:
    - id: crash-snapshot-mw
      priority: 700
      config:
        pipeline-name: Chat
```

### 9.2 代码中从 YAML config 读取构造参数

```csharp
// RegisterBaseMiddlewareServices 中从 YAML config 读取构造参数
services.AddSingleton(sp => {
    var config = ResolveNodeConfig("crash-snapshot-mw", "preprocess-pipeline");
    var pipelineName = config.GetValueOrDefault("pipeline-name", "Default");
    return new CrashSnapshotMiddleware<PreprocessContext>(
        sp.GetRequiredService<ICrashSnapshotStore>(), pipelineName);
});
```

### 9.3 消费点 ID → 泛型类型映射

泛型类型参数通过消费点 ID 自动映射（代码中固定，AOT 兼容）：

| 消费点 ID | 上下文类型 |
|-----------|-----------|
| chat-pipeline | ChatMiddlewareContext |
| preprocess-pipeline | PreprocessContext |
| shell-pipeline | ShellPipelineContext |
| ... | ... |

**设计原则**：配置参数在 YAML `config` 字段中（显式组合、直观可读），泛型类型参数通过消费点 ID 映射（代码中固定，AOT 兼容），不新增 `[MiddlewareContext]` 特性。

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
| 2.1 | NodeServiceResolver — (consumerId,nodeId) 组合 key 服务解析器 | composition/node_registry/ |
| 2.2 | DataDrivenPipelineComposition — 数据驱动管道组装新路径 | composition/pipelines/registration/ |
| 2.3 | CoreModule — 根据 Profile 选择新旧路径 | apps/engine/app/modules/ |
| 2.4 | base.yaml 补全 — 全部16条管道消费点定义 | resources/profiles/ |
| 2.5 | RegisterBaseMiddlewareServices 提取为公共方法 | composition/pipelines/registration/ |

**验收表**:

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `NodeServiceResolver.cs` (composition/node_registry/) | `DataDrivenPipelineComposition.BuildTaskPipeline/BuildStreamPipeline` | ✅ | ✅ exe `--profile base` 启动正常 |
| `DataDrivenPipelineComposition.cs` (pipelines/registration/) | `CoreModule.ConfigureServices` | ✅ | ✅ exe `--profile base` 对话正常 |
| `CoreModule.cs` (apps/engine/app/modules/) | `ApplicationBuilder` | ✅ | ✅ exe `--dump-profile base/headless/sdk/ci` 正常 |
| `base.yaml` (resources/profiles/) | `NodeProfileLoader.LoadFromResources` | ✅ | ✅ `--dump-profile base` 输出16条管道 |
| `RegisterBaseMiddlewareServices` (pipelines/registration/) | `DataDrivenPipelineComposition.AddDataDrivenPipelines` | ✅ | ✅ 单元测试 `RegisterBaseMiddlewareServicesTest` |

**验收**: 数据驱动组装的管道与手动组装的管道行为一致。通过 `--profile base` 使用数据驱动，通过 `--profile legacy` 使用旧的手动组装。

#### 阶段3：Profile 继承链验证（headless/sdk/ci 继承 base.yaml）

| 步骤 | 内容 | 文件 |
|------|------|------|
| 3.1 | headless/sdk/ci YAML 修正 — 排除项改为 base.yaml 中实际存在的节点 ID | resources/profiles/ |
| 3.2 | NodeProfileResolverTest — 9个测试验证继承链（排除/新增/覆写/多级继承） | composition.tests/node_registry/ |

**验收表**:

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `headless.yaml` 排除项修正 (resources/profiles/) | `NodeProfileResolver.Merge` 排除逻辑 | ✅ | ✅ `--dump-profile headless` 正确排除节点 |
| `sdk.yaml` 排除+覆写修正 (resources/profiles/) | `NodeProfileResolver.Merge` 覆写逻辑 | ✅ | ✅ `--dump-profile sdk` 正确覆写配置 |
| `ci.yaml` 排除项修正 (resources/profiles/) | `NodeProfileResolver.Merge` 排除逻辑 | ✅ | ✅ `--dump-profile ci` 正确排除节点 |
| `NodeProfileResolverTest` (composition.tests/) | 9个单元测试覆盖全部分支 | ✅ | ✅ 全绿 |

**验收**: headless/sdk/ci 的排除（Excludes）、新增（Includes）、覆写（Overrides）三种操作都正确执行。

#### 阶段3（原计划）：泛型中间件 YAML config 配置（不新增特性）

> **设计方向调整**（2026-11-13）：放弃 `[MiddlewareContext]` 特性 + `MiddlewareRegistrationGenerator` 方案。
> 配置参数通过 YAML `config` 字段配置，泛型类型参数通过消费点 ID 自动映射。

| 步骤 | 内容 | 文件 |
|------|------|------|
| 3.1 | base.yaml 扩展 config 字段 — CrashSnapshotMiddleware 的 pipelineName 等构造参数移入 YAML | resources/profiles/ |
| 3.2 | RegisterBaseMiddlewareServices 改为从 YAML config 读取构造参数 | composition/pipelines/registration/DataDrivenPipelineComposition.cs |
| 3.3 | 消费点 ID → 泛型类型映射表建立 | composition/pipelines/registration/DataDrivenPipelineComposition.cs |

**验收**: CrashSnapshotMiddleware 等泛型中间件的构造参数从 YAML config 读取，不再硬编码在代码中。编译通过，测试通过。

#### 阶段4：移除旧路径（数据驱动成为唯一路径）

| 步骤 | 内容 | 文件 |
|------|------|------|
| 4.1 | RegisterBaseMiddlewareServices 从 PipelineComposition.cs 移入 DataDrivenPipelineComposition.cs | composition/pipelines/registration/ |
| 4.2 | PipelineComposition.cs 归档到 .xxx/（含 AddAllPipelines 手动 .Use() 链） | composition/pipelines/registration/ → .xxx/ |
| 4.3 | CoreModule.cs 始终用 AddDataDrivenPipelines(profile ?? "base") | apps/engine/app/modules/ |
| 4.4 | JccChatSessionAssemblyTests.cs 改用 AddDataDrivenPipelines("base") | native_gui/tests/ |

**验收表**:

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `RegisterBaseMiddlewareServices` 移入 `DataDrivenPipelineComposition.cs` | `AddDataDrivenPipelines` 内部调用 | ✅ | ✅ 单元测试全绿 |
| `PipelineComposition.cs` 归档到 `.xxx/` | 无（已移除） | ✅ | ✅ 编译无引用错误 |
| `CoreModule.cs` 始终用数据驱动 | `ApplicationBuilder` | ✅ | ✅ exe 默认启动+`--profile base` 正常 |
| `JccChatSessionAssemblyTests.cs` 改用数据驱动 | 测试 DI 容器 | ✅ | ✅ 编译通过 |

**验收**: 数据驱动管道组装成为唯一路径，旧的手动 .Use() 链已移除。exe 启动+对话正常。

#### 阶段4（原计划）：DynamicPluginRegistry AOT 改造（YAML 配置驱动，不新增特性）

> **设计方向调整**（2026-11-13）：放弃 `[NodeHandler]` 特性 + `NodeHandlerGenerator` 方案。
> 配置参数通过 YAML `config` 字段配置，handler 实现通过现有接口约定识别。

| 步骤 | 内容 | 文件 |
|------|------|------|
| 4.1 | IAotHandlerRegistry 接口 | plugins.contracts/registry/ |
| 4.2 | PluginAotHandlerRegistry 实现 | plugins.infrastructure/services/ |
| 4.3 | DynamicPluginRegistry.Invoke 改造 — AOT 路径优先，反射路径降级 | plugins.contracts/registry/DynamicPluginRegistry.cs |
| 4.4 | YAML config 字段配置 handler 路由映射 | resources/profiles/ |

**验收**: AOT 下 Invoke 能路由到 handler。非 AOT 模式反射路径仍可用。

#### 阶段5：删除旧代码（仅在阶段2-4验证通过后）

| 步骤 | 内容 | 文件 | 状态 |
|------|------|------|------|
| 5.1 | 移除 PipelineComposition.cs 的手动 .Use() 链（已被数据驱动替代） | composition/pipelines/registration/ → .xxx/ | ✅ 完成 |
| 5.2 | 移除 PluginAlc（AOT 下不可用，已被 AOT handler 注册表替代） | plugins.contracts/plugin_core/PluginAlc.cs → .xxx/ | ✅ 完成 |
| 5.3 | 移除 AlcUnloadVerifier（依赖 ALC，AOT 下无意义） | plugins.contracts/plugin_core/AlcUnloadVerifier.cs → .xxx/ | ✅ 完成（已不存在） |
| 5.4 | 移除 DynamicPluginRegistry.LoadHandlerTable 的反射路径 | plugins.contracts/registry/DynamicPluginRegistry.cs | ✅ 完成（已不存在） |
| 5.5 | 移除 ServiceRegistration.Mcp.cs 中 ToolExecution + AgentTool 管道手动组装 | composition/dependency_injection/infrastructure/ | ✅ 完成 |

**验收表（步骤 5.1）**:

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `PipelineComposition.cs` 归档到 `.xxx/` | `DataDrivenPipelineComposition` 成为唯一路径 | ✅ | ✅ exe `--profile base` 启动+对话正常 |
| `RegisterBaseMiddlewareServices` 移入 `DataDrivenPipelineComposition.cs` | `AddDataDrivenPipelines` 内部调用 | ✅ | ✅ 单元测试 `RegisterBaseMiddlewareServicesTest` 全绿 |
| `CoreModule.cs` 始终用 `AddDataDrivenPipelines(profile ?? "base")` | `ApplicationBuilder` | ✅ | ✅ exe 默认启动（无 --profile）正常 |
| `JccChatSessionAssemblyTests.cs` 改用 `AddDataDrivenPipelines("base")` | 测试 DI 容器构建 | ✅ | ✅ 编译通过 |

**验收**: 全量编译通过，所有测试通过。AOT 发布测试通过。

#### 阶段5.5：移除 ToolExecution + AgentTool 管道手动组装（数据驱动迁移）

> **目标**：将 `ServiceRegistration.Mcp.cs` 中手动组装的 `MiddlewarePipeline<ToolExecutionContext>`（15个 `.Use()` 链）和 `MiddlewarePipeline<AgentToolContext>`（DI GetServices + OrderBy）迁移到 YAML 数据驱动组装。

**当前状态**：
- `ServiceRegistration.Mcp.cs:66-87` — ToolExecution 管道：15个 `.Use(sp.GetRequiredService<...>())` 硬编码链 + `.WithShortCircuit(ctx => ctx.IsShortCircuited)`
- `ServiceRegistration.Mcp.cs:53-64` — AgentTool 管道：`GetServices<IAgentToolMiddleware>() + OrderBy(Order)` 动态收集 + OnError 日志回调

**关键发现**：
- 所有 15 个 ToolExecution 中间件都只有 DI 依赖，无硬编码构造参数
- 所有 5 个 AgentTool 中间件都只有 DI 依赖，Order 属性已在类中定义
- 所有中间件已有 `[Register(typeof(IToolExecutionMiddleware/IAgentToolMiddleware))]` 自动 DI 注册

**CrashSnapshotMiddleware 统一**：

当前存在两个版本：

| 版本 | 位置 | 构造参数 | OnError | Result.IsError 检查 | CooldownService |
|------|------|---------|---------|---------------------|-----------------|
| 非泛型 `McpToolRegistry.CrashSnapshotMiddleware` | mcp_tool_dispatch/core/middleware/ | `(ICrashSnapshotStore, ILogger)` | Continue | 有 | 有 |
| 泛型 `CrashSnapshotMiddleware<TContext>` | abstractions/infrastructure/pipeline/middlewares/ | `(ICrashSnapshotStore, string pipelineName, Func<TContext,CrashExecutionContext?>?)` | Propagate | 无 | 无 |

**决策**：扩展泛型版本，合并非泛型版本的额外逻辑，然后废弃非泛型版本。

扩展后的泛型 `CrashSnapshotMiddleware<TContext>` 构造函数：

```csharp
public CrashSnapshotMiddleware(
    ICrashSnapshotStore store,
    string pipelineName,
    Func<TContext, CrashExecutionContext?>? contextExtractor = null,
    Func<TContext, bool>? isErrorSelector = null,
    Func<TContext, string>? errorMsgSelector = null)
```

新增参数：
- `isErrorSelector` — 可选，当 TContext 有 Result.IsError 语义时传入（如 ToolExecutionContext: `ctx => ctx.Result?.IsError == true`）
- `errorMsgSelector` — 可选，从 TContext 提取错误消息（如 `ctx => ctx.Result?.GetFirstText()`）

行为变更：
- OnError 改为 Continue（记录快照后不中断管道）—与非泛型版本对齐
- 当 `isErrorSelector` 不为 null 且返回 true 时，也记录 CrashSnapshot（非异常路径）
- 集成 CooldownService 防止同一 pipeline+toolName 重复记录

**变更清单**：

| 步骤 | 内容 | 文件 |
|------|------|------|
| 5.5.1 | 扩展泛型 CrashSnapshotMiddleware<TContext> — 增加 isErrorSelector/errorMsgSelector/CooldownService/OnError=Continue | abstractions/infrastructure/pipeline/middlewares/CrashSnapshotMiddleware.cs |
| 5.5.2 | base.yaml 新增 tool-execution-pipeline consumer（15节点） | resources/profiles/base.yaml |
| 5.5.3 | base.yaml 新增 agent-tool-pipeline consumer（5节点） | resources/profiles/base.yaml |
| 5.5.4 | BuildNodeFactories 新增 20 个工厂映射 | composition/pipelines/registration/DataDrivenPipelineComposition.cs |
| 5.5.5 | AddDataDrivenPipelines 新增 2 个管道注册（含短路条件） | composition/pipelines/registration/DataDrivenPipelineComposition.cs |
| 5.5.6 | ServiceRegistration.Mcp.cs 移除 2 个手动组装块 | composition/dependency_injection/infrastructure/ServiceRegistration.Mcp.cs |
| 5.5.7 | 非泛型 McpToolRegistry.CrashSnapshotMiddleware 归档到 .xxx/ | mcp/mcp_tool_dispatch/core/middleware/CrashSnapshotMiddleware.cs |

**base.yaml 新增 consumer 定义**：

```yaml
tool-execution-pipeline:
  nodes:
    - id: crash-snapshot-mw
      priority: 0
      config:
        pipeline-name: ToolExecution
    - id: argument-repair-mw
      priority: 100
    - id: required-params-mw
      priority: 200
    - id: schema-validation-mw
      priority: 300
    - id: agent-restriction-mw
      priority: 400
    - id: permission-check-mw
      priority: 500
    - id: pre-tool-use-hook-mw
      priority: 600
    - id: remote-policy-mw
      priority: 700
    - id: feature-flag-mw
      priority: 800
    - id: on-error-tool-injection-mw
      priority: 850
    - id: tool-health-scoring-mw
      priority: 860
    - id: tool-fix-hook-mw
      priority: 870
    - id: tool-aesthetic-mw
      priority: 880
    - id: post-tool-use-hook-mw
      priority: 890
    - id: tool-execution-mw
      priority: 900

agent-tool-pipeline:
  nodes:
    - id: agent-validation-mw
      priority: 100
    - id: agent-fork-mw
      priority: 200
    - id: agent-background-spawn-mw
      priority: 300
    - id: agent-stream-execution-mw
      priority: 400
    - id: agent-handoff-mw
      priority: 500
```

**BuildNodeFactories 新增映射**：

```csharp
// tool-execution-pipeline — IMiddleware<ToolExecutionContext>
yield return F("tool-execution-pipeline:crash-snapshot-mw",
    sp => CreateCrashSnapshotForToolExecution(sp, "tool-execution-pipeline"));
yield return F("tool-execution-pipeline:argument-repair-mw",
    sp => sp.GetRequiredService<McpToolRegistry.ArgumentRepairMiddleware>());
yield return F("tool-execution-pipeline:required-params-mw",
    sp => sp.GetRequiredService<McpToolRegistry.RequiredParamsMiddleware>());
yield return F("tool-execution-pipeline:schema-validation-mw",
    sp => sp.GetRequiredService<McpToolRegistry.SchemaValidationMiddleware>());
yield return F("tool-execution-pipeline:agent-restriction-mw",
    sp => sp.GetRequiredService<McpToolRegistry.AgentRestrictionMiddleware>());
yield return F("tool-execution-pipeline:permission-check-mw",
    sp => sp.GetRequiredService<McpToolRegistry.PermissionCheckMiddleware>());
yield return F("tool-execution-pipeline:pre-tool-use-hook-mw",
    sp => sp.GetRequiredService<McpToolRegistry.PreToolUseHookMiddleware>());
yield return F("tool-execution-pipeline:remote-policy-mw",
    sp => sp.GetRequiredService<McpToolRegistry.RemotePolicyMiddleware>());
yield return F("tool-execution-pipeline:feature-flag-mw",
    sp => sp.GetRequiredService<McpToolRegistry.FeatureFlagMiddleware>());
yield return F("tool-execution-pipeline:on-error-tool-injection-mw",
    sp => sp.GetRequiredService<McpToolRegistry.OnErrorToolInjectionMiddleware>());
yield return F("tool-execution-pipeline:tool-health-scoring-mw",
    sp => sp.GetRequiredService<McpToolRegistry.ToolHealthScoringMiddleware>());
yield return F("tool-execution-pipeline:tool-fix-hook-mw",
    sp => sp.GetRequiredService<McpToolRegistry.ToolFixHookMiddleware>());
yield return F("tool-execution-pipeline:tool-aesthetic-mw",
    sp => sp.GetRequiredService<McpToolRegistry.ToolAestheticMiddleware>());
yield return F("tool-execution-pipeline:post-tool-use-hook-mw",
    sp => sp.GetRequiredService<McpToolRegistry.PostToolUseHookMiddleware>());
yield return F("tool-execution-pipeline:tool-execution-mw",
    sp => sp.GetRequiredService<McpToolRegistry.ToolExecutionMiddleware>());

// agent-tool-pipeline — IMiddleware<AgentToolContext>
yield return F("agent-tool-pipeline:agent-validation-mw",
    sp => sp.GetRequiredService<AgentValidationMiddleware>());
yield return F("agent-tool-pipeline:agent-fork-mw",
    sp => sp.GetRequiredService<AgentForkMiddleware>());
yield return F("agent-tool-pipeline:agent-background-spawn-mw",
    sp => sp.GetRequiredService<AgentBackgroundSpawnMiddleware>());
yield return F("agent-tool-pipeline:agent-stream-execution-mw",
    sp => sp.GetRequiredService<AgentStreamExecutionMiddleware>());
yield return F("agent-tool-pipeline:agent-handoff-mw",
    sp => sp.GetRequiredService<AgentHandoffMiddleware>());
```

**AddDataDrivenPipelines 新增注册**：

```csharp
// ToolExecution 管道 (短路: ctx.IsShortCircuited)
services.AddSingleton<MiddlewarePipeline<ToolExecutionContext>>(sp =>
    BuildTaskPipeline<ToolExecutionContext>(sp, "tool-execution-pipeline",
        shortCircuit: ctx => ctx.IsShortCircuited));

// AgentTool 管道
services.AddSingleton<MiddlewarePipeline<AgentToolContext>>(sp =>
    BuildTaskPipeline<AgentToolContext>(sp, "agent-tool-pipeline"));
```

**CreateCrashSnapshotForToolExecution 辅助方法**：

```csharp
private static CrashSnapshotMiddleware<ToolExecutionContext> CreateCrashSnapshotForToolExecution(
    IServiceProvider sp, string consumerId) {
    var profile = sp.GetRequiredService<ResolvedProfile>();
    var pipelineName = GetPipelineName(profile, consumerId);
    var store = sp.GetRequiredService<ICrashSnapshotStore>();
    return new CrashSnapshotMiddleware<ToolExecutionContext>(
        store, pipelineName,
        contextExtractor: ctx => new CrashExecutionContext {
            ToolName = ctx.ToolName,
            OperationName = pipelineName,
        },
        isErrorSelector: ctx => ctx.Result?.IsError == true,
        errorMsgSelector: ctx => ctx.Result?.GetFirstText());
}
```

**ServiceRegistration.Mcp.cs 移除内容**：

移除 `AddMcpServices` 中第 53-87 行的两个 `MiddlewarePipeline` 手动组装块（AgentTool + ToolExecution），保留其他服务注册。

**验收表**：

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| 扩展泛型 `CrashSnapshotMiddleware<TContext>` (abstractions/) | `CreateCrashSnapshotForToolExecution` | ✅ | ✅ exe 对话正常 |
| `base.yaml` 新增 tool-execution-pipeline (resources/profiles/) | `BuildTaskPipeline<ToolExecutionContext>` | ✅ | ✅ `--dump-profile base` 输出正确 |
| `base.yaml` 新增 agent-tool-pipeline (resources/profiles/) | `BuildTaskPipeline<AgentToolContext>` | ✅ | ✅ `--dump-profile base` 输出正确 |
| `BuildNodeFactories` 新增20个映射 (pipelines/registration/) | `ResolveMiddlewares` | ✅ | ✅ 单元测试全绿 |
| `AddDataDrivenPipelines` 新增2个管道注册 (pipelines/registration/) | DI 容器 | ✅ | ✅ exe 启动正常 |
| `ServiceRegistration.Mcp.cs` 移除手动组装 | 无（已移除） | ✅ | ✅ 编译通过 |
| 非泛型 `CrashSnapshotMiddleware` 归档到 `.xxx/` | 无（已移除） | ✅ | ✅ 编译无引用错误 |

**验收方式**：

```powershell
# 1. 编译
dotnet build modules/apps/cli/JoinCode.csproj -c Debug

# 2. --dump-profile base 验证新增 consumer
./artifacts/bin/JoinCode/Debug/net10.0/jcc.exe --dump-profile base
# 期望：输出包含 tool-execution-pipeline 和 agent-tool-pipeline 消费点

# 3. 对话验证 — 工具调用走数据驱动管道
./artifacts/bin/JoinCode/Debug/net10.0/jcc.exe -p "say hello" --trust --await 15
# 期望：正常对话，工具调用正常

# 4. 单元测试
dotnet test modules/composition/pipelines/pipelines.tests/ --filter "Category!=Integration"
```

#### 阶段6：清理硬编码点（可选优化）

| 步骤 | 内容 | 文件 | 状态 |
|------|------|------|------|
| 6.1 | PromptTemplateGenerator.InferClassName 改为特性标注驱动 | generators/prompt_template.generator/ | ✅ 完成 |
| 6.2 | McpToolDispatchGenerator.DetectTimeoutPolicy 改为类型符号比较 | generators/mcp_tool_dispatch.generator/ | ✅ 完成 |
| 6.3 | PromptSectionGenerator 硬编码正则改为特性标注收集 | generators/prompt_section.generator/ | ✅ 完成 |

**验收**: 编译通过，测试通过。✅ 全部完成（PR #653）

#### 阶段7：统一 Registry（可选优化，长期目标）

| 步骤 | 内容 | 文件 |
|------|------|------|
| 7.1 | CommandRegistry 统一到 NodeRegistry 消费点模式 | composition/ |
| 7.2 | ScenarioRegistry 统一到 NodeRegistry 消费点模式 | composition/ |
| 7.3 | ToolSecuritySets 统一到 NodeRegistry 消费点模式 | composition/ |

**验收**: 编译通过，测试通过。

### 10.3 阶段依赖图

```
阶段1（基础设施）→ 阶段2（管道组装）→ 阶段4移除旧路径（已完成）
                 → 阶段3（YAML config 扩展）↗
                 → 阶段4原计划（AOT 改造，YAML 驱动）↗
                 → 阶段5（删除旧代码）
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

### 11.3 阶段3验收 — 泛型中间件 YAML config 配置

**验收方式**：YAML config 字段验证 + 对话验证

```powershell
# 1. 编译
dotnet build modules/apps/cli/JoinCode.csproj -c Debug

# 2. --dump-profile base 验证 config 字段包含构造参数
./modules/apps/cli/bin/Debug/net10.0/jcc.exe --dump-profile base

# 期望：crash-snapshot-mw 节点的 config 字段包含 pipeline-name 等构造参数

# 3. 对话验证 — crash-snapshot 中间件使用 YAML config 中的 pipelineName
./modules/apps/cli/bin/Debug/net10.0/jcc.exe --profile base -p "echo hello" --trust --await 15

# 期望：正常对话，crash-snapshot 中间件在管道中生效

# 4. --debuglog 验证中间件执行链
./modules/apps/cli/bin/Debug/net10.0/jcc.exe --profile base -p "echo hello" --trust --await 15 --debuglog

# 期望：日志中可见中间件按 priority 顺序执行，pipelineName 从 YAML config 读取
```

**判定标准**：
- `--dump-profile base` 中 crash-snapshot-mw 的 config 字段包含 pipeline-name
- `--debuglog` 日志显示中间件按 priority 顺序执行
- 对话行为与阶段2一致

### 11.4 阶段4验收 — DynamicPluginRegistry AOT 改造（YAML 配置驱动）

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
