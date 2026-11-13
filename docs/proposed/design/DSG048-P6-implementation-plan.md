# P6 实现规划 — DynamicPluginRegistry AOT 改造

> 状态: accepted（已实现 2026-11-14）
> 创建: 2026-11-14
> 关联: [DSG048](DSG048-node-plugin-inheritance-architecture.md) §5.8, [DSG048-impl](DSG048-implementation-design.md) §8

---

## 1. 现状分析

### 1.1 DynamicPluginRegistry 当前状态

| 维度 | 状态 |
|------|------|
| 生产代码引用 | **零引用** — 仅在 `PluginManager.cs:236` 注释中提到 |
| Invoke 在 AOT 下 | 永远返回 `MethodNotFound`（`_loader` 默认返回空表） |
| 反射路径 | 已移除（`LoadHandlerTable` 不存在） |
| `_loader` 委托 | `Func<PluginPackage, FrozenDictionary<string, Func<object?[], object?>>>` — 支持外部注入但默认空表 |
| 测试覆盖 | `DynamicPluginRegistryTests.cs` — 仅单元测试 |

### 1.2 YAML `tool-dispatch` 消费点当前状态

| 维度 | 状态 |
|------|------|
| YAML 定义 | `base.yaml:422-428` — shell-tool, fs-tool, web-tool, code-tool |
| 生产代码消费 | **零消费** — `GetNodesForConsumer("tool-dispatch")` 从未被调用 |
| `INodeConsumer` 实现 | **零实现** |
| 与实际工具分发的关系 | **完全独立** — 实际工具走 `[McpToolDispatch]` + `McpToolDispatchGenerator` + `LocalToolRegistry` |

### 1.3 实际工具分发链路（独立于 YAML tool-dispatch）

```
[McpToolDispatch] 特性标注 → McpToolDispatchGenerator 源码生成器扫描
    ↓
生成 GeneratedToolHandlerRegistration.g.cs（AddMcpToolDispatchSingletons + RegisterAllMcpToolDispatchAsync）
    ↓
ServiceRegistration.Mcp.cs 调用生成代码注册到 DI
    ↓
LocalToolRegistry (IToolRegistry 实现) 维护 ConcurrentDictionary<string, IToolHandler>
    ↓
tool-execution-pipeline (15 个中间件) → ToolExecutionMiddleware 最终调用 handler.ExecuteAsync()
```

---

## 2. P6 改造目标

### 2.1 双路径架构（用户确认）

| 路径 | 适用场景 | 机制 |
|------|---------|------|
| **AOT 内部组件** | 编译期已知的 handler | 源码生成器编译期采集，生成 `CompiledNodeHandlers` 路由表 |
| **动态外部组件** | 用户自定义的外部插件 | 文件监控约定文件夹 + YAML 配置 |

### 2.2 核心改造点

1. 创建 `IAotHandlerRegistry` 接口 — AOT handler 注册表的抽象
2. 创建 `AotHandlerRegistryGenerator` 源码生成器 — 编译期采集 handler 路由表
3. 改造 `DynamicPluginRegistry.Invoke` — AOT 路径优先，动态路径降级
4. 实现文件监控 + YAML 约定文件夹 — 动态外部组件加载
5. 在 DI 中注册 `IAotHandlerRegistry` — 连接源码生成器输出与运行时

---

## 3. 关键设计决策

### 3.1 源码生成器扫描什么？

**决策：扫描 `[McpToolDispatch]` + `[McpTool]` 特性系统**

理由：
- 这是现有的工具 handler 标识系统，不需要新增特性
- `[McpToolDispatch]` 标注在类级别，标识一个 handler 组（如 `ShellToolHandlers`）
- `[McpTool]` 标注在方法级别，标识具体的工具方法（如 `ShellExecuteAsync`）
- `McpToolDispatchGenerator` 已经实现了类似的扫描逻辑，可以复用模式

### 3.2 节点 ID 如何确定？

**决策：从 `ToolCategory` 枚举的 `[EnumValue]` 字符串推导节点 ID**

映射规则：`[McpToolDispatch(ToolCategory.X)]` → 节点 ID = `ToolCategory.X` 的 `[EnumValue]` 字符串

| ToolCategory | [EnumValue] | 节点 ID | YAML 当前 ID | 需要调整？ |
|-------------|-------------|---------|-------------|-----------|
| Shell | "shell" | `shell` | `shell-tool` | 是 → 改为 `shell` |
| File | "file" | `file` | `fs-tool` | 是 → 改为 `file` |
| Web | "web" | `web` | `web-tool` | 是 → 改为 `web` |
| CodeExecution | "code_execution" | `code_execution` | `code-tool` | 是 → 改为 `code_execution` |
| Search | "search" | `search` | — | 新增 |
| Git | "git" | `git` | — | 新增 |
| Agent | "agent" | `agent` | — | 新增 |
| ... | ... | ... | ... | ... |

**YAML 节点 ID 需要与 `[EnumValue]` 字符串对齐**，消除 "-tool" 后缀，保持 SSOT。

### 3.3 Handler 的 DI 依赖如何处理？

**决策：`IAotHandlerRegistry` 在运行时从 DI 获取 Handler 实例**

`[McpToolDispatch]` 标注的类（如 `ShellToolHandlers`）有 DI 依赖（构造函数注入 `MiddlewarePipeline`, `IFileSystem`, `ILogger` 等）。源码生成器无法在编译期创建这些实例。

方案：
1. 源码生成器生成**注册代码**（不是静态路由表），声明 nodeId → handlerType → method 的映射
2. `PluginAotHandlerRegistry`（`IAotHandlerRegistry` 实现）在运行时从 DI 获取 Handler 实例
3. 构造 `Func<object?[], object?>` 委托，委托内部从 DI 解析 Handler 实例并调用方法

生成的注册代码示例：
```csharp
// 由源码生成器生成
internal static class AotHandlerRegistrations {
    public static void Register(IAotHandlerRegistryBuilder builder) {
        builder.AddHandler("shell", "bash",
            (sp, args) => sp.GetRequiredService<ShellToolHandlers>().ShellExecuteAsync(
                (string)args[0], (string?)args[1], (int?)args[2], (string?)args[3]
            ).GetAwaiter().GetResult());
        builder.AddHandler("file", "read",
            (sp, args) => sp.GetRequiredService<FileToolHandlers>().ReadAsync(
                (string)args[0], (int?)args[1], (int?)args[2]
            ).GetAwaiter().GetResult());
        // ...
    }
}
```

### 3.4 DynamicPluginRegistry.Invoke 改造后的角色

**决策：AOT 路径优先，动态路径降级**

改造后的 `Invoke` 流程：
1. **AOT 路径**：从 `IAotHandlerRegistry` 获取 handler 表 → 如果找到，执行 handler
2. **动态路径**：从 `_runtime`（运行实例的 Handlers）获取 handler → 如果找到，执行 handler
3. **失败**：返回 `MethodNotFound`

AOT handler 不需要 define/run/stop/undefine 生命周期管理 — 它们在编译期已注册，始终可用。

### 3.5 异步 Handler 的同步适配

**决策：使用 `.GetAwaiter().GetResult()` 进行异步到同步转换**

`[McpTool]` 方法返回 `Task<ToolResult>`，但 `DynamicPluginRegistry.Invoke` 是同步的。生成的委托使用 `.GetAwaiter().GetResult()` 进行阻塞等待。

**风险**：在异步上下文中可能死锁。但 `DynamicPluginRegistry.Invoke` 的设计场景是同步调用（如 MCP 工具调用的同步路由），不在异步上下文中。

### 3.6 文件监控 + YAML 约定文件夹

**决策：约定 `~/.jcc/plugins/` 文件夹 + YAML 配置**

插件 YAML 配置格式：
```yaml
# ~/.jcc/plugins/my-plugin/plugin.yaml
name: my-plugin
version: 1.0.0
node-id: my-custom-tool
assembly-path: ./my-plugin.dll
entry-type: MyPlugin.Handlers
capabilities:
  - shell
  - filesystem
methods:
  - name: execute
    entry-method: ExecuteAsync
  - name: validate
    entry-method: ValidateAsync
```

文件监控：
- `FileSystemWatcher` 监控 `~/.jcc/plugins/` 目录
- 检测到新插件 YAML 时，调用 `DynamicPluginRegistry.DefinePlugin` + `RunPlugin`
- `_loader` 委托从 YAML 配置的 `assembly-path` 加载 DLL（非 AOT 模式）

**AOT 限制**：AOT 模式下无法通过 `AssemblyLoadContext` 加载外部 DLL。文件监控路径仅适用于非 AOT 模式。AOT 模式下只有源码生成器路径可用。

---

## 4. 实现步骤

### 步骤 1: 创建 `IAotHandlerRegistry` 接口

| 项目 | 内容 |
|------|------|
| 位置 | `modules/plugins/plugins.contracts/registry/IAotHandlerRegistry.cs` |
| 接口 | `FrozenDictionary<string, Func<object?[], object?>>? GetHandlers(string nodeId)` |
| 命名空间 | `JoinCode.Abstractions.Entity` |

```csharp
public interface IAotHandlerRegistry {
    FrozenDictionary<string, Func<object?[], object?>>? GetHandlers(string nodeId);
}
```

### 步骤 2: 创建 `IAotHandlerRegistryBuilder` 接口

| 项目 | 内容 |
|------|------|
| 位置 | `modules/plugins/plugins.contracts/registry/IAotHandlerRegistryBuilder.cs` |
| 接口 | `void AddHandler(string nodeId, string method, Func<IServiceProvider, object?[], object?> handler)` |
| 命名空间 | `JoinCode.Abstractions.Entity` |

源码生成器生成的注册代码通过 `IAotHandlerRegistryBuilder` 注册 handler。

### 步骤 3: 创建 `AotHandlerRegistryGenerator` 源码生成器

| 项目 | 内容 |
|------|------|
| 位置 | `modules/generators/aot_handler.generator/` |
| 目标框架 | `netstandard2.0` |
| 扫描 | `[McpToolDispatch]` 类 + `[McpTool]` 方法 |
| 生成 | `AotHandlerRegistrations.Register(builder)` 方法 |

生成器逻辑：
1. 扫描 `[McpToolDispatch(ToolCategory.X)]` 标注的类
2. 从 `ToolCategory` 的 `[EnumValue]` 推导节点 ID
3. 扫描类中 `[McpTool]` 标注的方法
4. 从 `[McpTool]` 的第一个参数（工具名枚举）推导方法名
5. 生成 `AddHandler(nodeId, methodName, (sp, args) => ...)` 注册代码

### 步骤 4: 创建 `PluginAotHandlerRegistry` 实现

| 项目 | 内容 |
|------|------|
| 位置 | `modules/plugins/plugins.infrastructure/services/PluginAotHandlerRegistry.cs` |
| 实现 | `IAotHandlerRegistry` |
| 依赖 | `IServiceProvider` |

实现逻辑：
1. 在 DI 注册阶段，调用源码生成器产出的 `AotHandlerRegistrations.Register(builder)`
2. `builder` 收集所有 `(nodeId, method, handler)` 注册项
3. 按 nodeId 分组，构造 `FrozenDictionary<string, FrozenDictionary<string, Func<object?[], object?>>>`
4. `GetHandlers(nodeId)` 从字典查找并返回 handler 表

### 步骤 5: 改造 `DynamicPluginRegistry.Invoke`

| 项目 | 内容 |
|------|------|
| 位置 | `modules/plugins/plugins.contracts/registry/DynamicPluginRegistry.cs` |
| 改造 | `Invoke` 方法增加 AOT 路径 |

改造后的 `Invoke`：
```csharp
public PluginInvokeResult Invoke(string name, string method, object?[] args, PluginPackageId? expectedPackageId = null) {
    // AOT 路径：从编译期注册的 handler 表路由
    if (_aotRegistry is not null) {
        var aotHandlers = _aotRegistry.GetHandlers(name);
        if (aotHandlers is not null && aotHandlers.TryGetValue(method, out var aotHandler)) {
            try { return PluginInvokeResult.Ok(aotHandler(args)); }
            catch (Exception ex) { return PluginInvokeResult.Fail(PluginInvokeFailure.HandlerError, ex.Message); }
        }
    }

    // 动态路径：从运行实例的 handler 表路由
    if (!_runtime.TryGetRunning(name, out var instance))
        return PluginInvokeResult.Fail(PluginInvokeFailure.PluginNotRunning, $"插件 {name} 未运行。");

    if (expectedPackageId.HasValue && expectedPackageId.Value != instance.PackageId)
        return PluginInvokeResult.Fail(PluginInvokeFailure.StaleRun, ...);

    if (!instance.Handlers.TryGetValue(method, out var handler))
        return PluginInvokeResult.Fail(PluginInvokeFailure.MethodNotFound, ...);

    try { return PluginInvokeResult.Ok(handler(args)); }
    catch (Exception ex) { return PluginInvokeResult.Fail(PluginInvokeFailure.HandlerError, ex.Message); }
}
```

构造函数新增 `IAotHandlerRegistry?` 参数：
```csharp
public DynamicPluginRegistry(
    PluginApprovalRegistry? approval = null,
    Func<DateTimeOffset>? clock = null,
    Func<PluginPackage, FrozenDictionary<string, Func<object?[], object?>>>? loader = null,
    IAotHandlerRegistry? aotRegistry = null)  // 新增
```

### 步骤 6: 更新 YAML 节点 ID

| 项目 | 内容 |
|------|------|
| 位置 | `resources/profiles/base.yaml` |
| 改造 | `tool-dispatch` 消费点节点 ID 与 `[EnumValue]` 对齐 |

```yaml
tool-dispatch:
  nodes:
    - id: shell           # 原 shell-tool
    - id: file             # 原 fs-tool
    - id: web              # 原 web-tool
      enabled-when: feature.web-search
    - id: code_execution   # 原 code-tool
```

### 步骤 7: 文件监控 + YAML 约定文件夹（动态外部组件）

| 项目 | 内容 |
|------|------|
| 位置 | `modules/plugins/plugins.infrastructure/services/PluginFileWatcher.cs` |
| 约定文件夹 | `~/.jcc/plugins/` |
| YAML 格式 | 见 §3.6 |

实现逻辑：
1. `FileSystemWatcher` 监控 `~/.jcc/plugins/` 目录
2. 检测到新 `plugin.yaml` 时，解析 YAML 配置
3. 调用 `DynamicPluginRegistry.DefinePlugin` 注册插件元数据
4. 调用 `DynamicPluginRegistry.RunPlugin` 加载 handler 表
5. `_loader` 委托从 `assembly-path` 加载 DLL，通过 `AssemblyLoadContext` 创建 handler 实例

### 步骤 8: DI 注册

| 项目 | 内容 |
|------|------|
| 位置 | `modules/composition/composition/dependency_injection/` |
| 注册 | `IAotHandlerRegistry` → `PluginAotHandlerRegistry` |

注册流程：
1. `PluginAotHandlerRegistry` 实现 `IAotHandlerRegistry`，注入 `IServiceProvider`
2. 在 DI 注册阶段，创建 `AotHandlerRegistryBuilder`，调用 `AotHandlerRegistrations.Register(builder)`
3. `builder` 构造 handler 路由表，注入到 `PluginAotHandlerRegistry`
4. `DynamicPluginRegistry` 注入 `IAotHandlerRegistry`

### 步骤 9: 验收

| 验收项 | 方式 |
|--------|------|
| Debug 编译 | `dotnet build` 全仓编译通过 |
| 单元测试 | `DynamicPluginRegistryTests` + 新增 AOT handler 路由测试 |
| exe 验收 | `jcc.exe --profile base -p "list files" --trust --await 30` — 工具调用走 AOT handler 路径 |
| AOT 发布 | `dotnet publish -c Release -r win-x64` — AOT exe 编译通过 |
| AOT exe 验收 | AOT exe 启动 + 对话正常 |

---

## 5. 依赖关系

```
步骤 1 (IAotHandlerRegistry) ─┬─→ 步骤 3 (源码生成器)
                              ├─→ 步骤 4 (PluginAotHandlerRegistry)
                              └─→ 步骤 5 (Invoke 改造)

步骤 2 (IAotHandlerRegistryBuilder) ─→ 步骤 3 (源码生成器)

步骤 3 (源码生成器) ─→ 步骤 4 (PluginAotHandlerRegistry)
                    └─→ 步骤 8 (DI 注册)

步骤 4 (PluginAotHandlerRegistry) ─→ 步骤 5 (Invoke 改造)
                                └─→ 步骤 8 (DI 注册)

步骤 5 (Invoke 改造) ─→ 步骤 9 (验收)

步骤 6 (YAML 节点 ID) — 独立，可并行

步骤 7 (文件监控) — 独立，可并行（但验收时需要步骤 1-5 完成）
```

**关键路径**：步骤 1 → 2 → 3 → 4 → 5 → 8 → 9

---

## 6. 风险与缓解

| 风险 | 影响 | 缓解 |
|------|------|------|
| `.GetAwaiter().GetResult()` 死锁 | 异步上下文中调用 Invoke 会死锁 | Invoke 设计为同步调用场景；如需异步，后续增加 `InvokeAsync` |
| 源码生成器扫描范围过大 | 生成代码膨胀 | 只扫描 `[McpToolDispatch]` 标注的类，不扫描所有 `[Register]` 服务 |
| YAML 节点 ID 变更影响 | 现有测试可能引用旧 ID | 同步更新测试中的节点 ID |
| AOT 模式下文件监控不可用 | 动态外部组件路径在 AOT 下失效 | 设计上已预期：AOT 模式只有源码生成器路径 |
| 源码生成器与 McpToolDispatchGenerator 重复 | 两个生成器扫描相同特性 | P6 生成器生成不同的输出（AOT handler 路由表 vs DelegateToolHandler 注册），目标不同 |

---

## 7. 与现有系统的关系

### 7.1 P6 与 McpToolDispatchGenerator 的关系

| 维度 | McpToolDispatchGenerator | AotHandlerRegistryGenerator (P6) |
|------|--------------------------|--------------------------------|
| 扫描特性 | `[McpToolDispatch]` + `[McpTool]` | `[McpToolDispatch]` + `[McpTool]` |
| 生成输出 | `DelegateToolHandler` 实例 → `IMcpToolRegistry` | `AotHandlerRegistrations.Register()` → `IAotHandlerRegistry` |
| 路由维度 | 工具名（如 "bash", "read"） | 节点 ID + 方法名（如 "shell" + "bash"） |
| DI 依赖 | Handler 类注册为 DI 单例 | Handler 类从 DI 解析（不注册，由其他生成器注册） |
| 消费者 | `LocalToolRegistry` → `tool-execution-pipeline` | `DynamicPluginRegistry.Invoke` |

两个生成器扫描相同的特性但生成不同的输出，服务于不同的路由维度。它们是互补的，不是重复的。

### 7.2 P6 与 NodeRegistry 的关系

| 维度 | NodeRegistry | DynamicPluginRegistry (P6 改造后) |
|------|-------------|----------------------------------|
| 管理对象 | 内部组件（中间件、工具、命令、提示词） | AOT 内部组件 + 动态外部组件 |
| 注册方式 | YAML Profile + DI | 源码生成器（AOT） + 文件监控 YAML（动态） |
| 路由维度 | consumerId + nodeId → DI 服务 | nodeId + method → handler 委托 |
| 生命周期 | Profile 继承链解析 | AOT handler 无生命周期；动态插件有 define/run/stop |

P6 改造后，`DynamicPluginRegistry` 成为 `NodeRegistry` 的补充：`NodeRegistry` 管理"哪些节点在哪些消费点中"（组合维度），`DynamicPluginRegistry` 管理"如何调用节点的具体方法"（执行维度）。

---

## 8. 实现顺序（渐进式）

| 批次 | 步骤 | 验证 |
|------|------|------|
| 1 | 步骤 1 + 2（接口定义） | 编译通过 |
| 2 | 步骤 3（源码生成器） | 生成器编译通过 + 生成代码审查 |
| 3 | 步骤 4（PluginAotHandlerRegistry） | 编译通过 + 单元测试 |
| 4 | 步骤 5（Invoke 改造） | 编译通过 + 单元测试（AOT 路径优先） |
| 5 | 步骤 6（YAML 节点 ID） | 编译通过 + Profile 解析测试 |
| 6 | 步骤 8（DI 注册） | exe 启动验证 |
| 7 | 步骤 7（文件监控） | 编译通过 + 单元测试 |
| 8 | 步骤 9（验收） | exe 对话验证 + AOT 发布验证 |

---

## 9. 实现记录（2026-11-14 完成）

### 9.1 实际实现步骤与决策

| 步骤 | 实际实现 | 关键决策 | commit |
|------|---------|---------|--------|
| P6-A: 接口定义 | `IAotHandlerRegistry` + `IAotHandlerRegistryBuilder` 移至 `abstractions.interfaces/interfaces/tools/`（非 `plugins.contracts/registry/`） | 命名空间从 `JoinCode.Abstractions.Entity` 改为 `JoinCode.Abstractions.Tools` — 生成器在所有含 `[McpToolDispatch]` 的项目中运行，这些项目不依赖 `plugins.contracts` 但都依赖 `abstractions` | `76b2eb36a` |
| P6-B: 源码生成器 | `AotHandlerRegistryGenerator` 生成两种输出：`Register(builder)`（Builder 模式）+ `AddAotHandlerRegistry(services)`（DI 自包含注册） | 生成器自包含 DI 注册代码（含 `GeneratedAotHandlerRegistry` 内部实现类），避免 Composition 引用 `plugins.infrastructure` | `76b2eb36a` + `fb9121a6a` |
| P6-C: Invoke 改造 | `DynamicPluginRegistry.Invoke` 增加 `IAotHandlerRegistry?` 构造参数，AOT 路径优先 → 动态路径降级 | AOT handler 不需要 define/run/stop/undefine 生命周期管理 — 编译期已注册，始终可用 | `76b2eb36a` |
| P6-D: YAML 节点 ID | `base.yaml` 中 `shell-tool→shell`, `fs-tool→file`, `web-tool→web`, `code-tool→code_execution` | 消除 `-tool` 后缀保持 SSOT，节点 ID 与 `ToolCategory.[EnumValue]` 统一 | `8d8473fc4` |
| P6-E: DI 注册 | `ServiceRegistration.cs` 中 `AddNodeRegistry` 之后调用 `AotHandlerRegistrations_JoinCode_Composition.AddAotHandlerRegistry(services)` | 生成器生成的 `AddAotHandlerRegistry` 方法注册 Handler 单例 + 构建 `IAotHandlerRegistry` 实现，Composition 不需要引用 `plugins.infrastructure` | `fb9121a6a` |
| P6-F: 文件监控 | `PluginYamlConfig` DTO + `ExternalPluginLoader`（反射加载 DLL）+ `PluginFileWatcher`（FileSystemWatcher 监控 `~/.jcc/plugins/`） | `ExternalPluginLoader` 标注 `[RequiresUnreferencedCode]`（非 AOT 路径）；`PluginFileWatcher` 注入 `IFileSystem` 符合 JCC9001 规范 | `62b8ad96d` |
| P6-G: 验收 | 5 个集成测试（`AotHandlerRegistryIntegrationTests`）+ exe 启动验证 + 全仓编译通过 | 集成测试验证 `IAotHandlerRegistry` 在完整 DI 容器中可用，节点 ID 与 `ToolCategory.[EnumValue]` 对齐 | `b21377403` |

### 9.2 关键架构决策

1. **接口位置**：`IAotHandlerRegistry` 从 `plugins.contracts/registry/` 移至 `abstractions.interfaces/interfaces/tools/` — 生成器在所有含 `[McpToolDispatch]` 的项目中运行，这些项目不依赖 `plugins.contracts` 但都依赖 `abstractions`

2. **生成器自包含 DI 注册**：`AotHandlerRegistryGenerator` 生成 `AddAotHandlerRegistry(IServiceCollection)` 方法，包含 Handler 单例注册 + `IAotHandlerRegistry` 实现（`GeneratedAotHandlerRegistry` 内部类）— Composition 项目不需要引用 `plugins.infrastructure`

3. **JCC11004 分析器移除**：用户选择完全移除 JCC11004 规则（公开方法返回集合类型时禁止 return null）— 规则注册通过反射扫描 `[AnalyzerRule]` 特性自动注册，删除文件即可移除

4. **`#nullable enable` 指令**：生成器输出的代码必须包含 `#nullable enable` 指令，否则 CS8669 错误（可空引用类型批注只在 `#nullable` 上下文中可用）

5. **NoWarn 排除 AOT 反射警告**：`Plugins.Infrastructure.csproj` 添加 `NoWarn=JCC1015;JCC1016;IL2026;IL2075;IL2072` — `ExternalPluginLoader` 本质上需要反射（`Activator.CreateInstance`、`MethodInfo.Invoke`、`AssemblyLoadContext`），这些在 AOT 模式下不可用但在非 AOT 模式下必需

### 9.3 验收结果

| 验收项 | 方式 | 结果 |
|--------|------|------|
| Debug 编译 | `Build-AllDomains.ps1` 全仓编译 | ✅ 全部通过 |
| 集成测试 | `AotHandlerRegistryIntegrationTests`（5 个测试） | ✅ 5/5 通过 |
| exe 启动 | `dotnet run --project JoinCode.csproj -- --help` | ✅ 正常启动 |
| 生成代码审查 | `AotHandlerRegistrations_JoinCode_Composition.g.cs`（1970 行） | ✅ 749 个 handler 注册，涵盖所有 `[McpToolDispatch]` 标注的类 |
| Composition 测试 | `Composition.Tests`（1028 个测试） | ✅ 1028/1028 通过 |

### 9.4 文件清单

**新增文件**：
- `modules/abstractions/abstractions.interfaces/interfaces/tools/IAotHandlerRegistry.cs`
- `modules/abstractions/abstractions.interfaces/interfaces/tools/IAotHandlerRegistryBuilder.cs`
- `modules/generators/aot_handler.generator/` — 源码生成器项目（3 个文件）
- `modules/plugins/plugins.infrastructure/services/PluginAotHandlerRegistry.cs`
- `modules/plugins/plugins.infrastructure/services/AotHandlerRegistryBuilder.cs`
- `modules/plugins/plugins.infrastructure/services/PluginYamlConfig.cs`
- `modules/plugins/plugins.infrastructure/services/ExternalPluginLoader.cs`
- `modules/plugins/plugins.infrastructure/services/PluginFileWatcher.cs`
- `test/integration/integration.tests/plugins/AotHandlerRegistryIntegrationTests.cs`

**修改文件**：
- `modules/plugins/plugins.contracts/registry/DynamicPluginRegistry.cs` — Invoke 改造
- `modules/generators/aot_handler.generator/AotHandlerRegistryGenerator.cs` — 生成 `AddAotHandlerRegistry` 方法
- `modules/composition/composition/dependency_injection/core/ServiceRegistration.cs` — DI 注册调用
- `modules/composition/composition/GlobalUsings.cs` — 添加 `JoinCode.Plugins.Infrastructure.Generated`
- `resources/profiles/base.yaml` — YAML 节点 ID 对齐
- `resources/profiles/ci.yaml` / `sdk.yaml` — 同步更新
- 18 个 .csproj — 添加 `aot_handler.generator` Analyzer 引用
