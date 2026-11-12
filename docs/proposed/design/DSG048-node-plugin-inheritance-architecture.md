# 万物皆节点，节点皆可继承 — AOT 兼容插件化架构

> 📍 **导航**: [docs/](../README.md) › [design/](README.md) | **前置**: [adr/](../adr/README.md)
> 🔗 **上游索引**: [design/README.md](README.md) — 修改本文档后须同步更新此索引

> 状态: accepted（阶段1 基础设施层已实现并验收）
> 创建: 2026-11-12
> 关联 ADR: 待定（后续阶段实现后创建）
> 取代: 无
> 范围: 运行期统一节点管理 + 数据驱动组合 + 省略即继承 + 可逆注册 + DynamicPluginRegistry AOT 改造

---

## 1. 背景与动机

### 1.1 用户需求

> 1. 当前架构是"写死的"，不够灵活
> 2. 必须按照"万物皆节点，节点皆可继承"的方式组织
> 3. AOT 编译下无法实现动态加载卸载，但仍需要灵活组合
> 4. 可以随时组织某个插件，也可以某种情况不加载这个插件
> 5. 插件可以作为父类，子类通过继承适配不同模型/场景
> 6. 设计要纯粹，特性不应该多名称，减少组合特性和特性参数

### 1.2 当前问题

| 问题 | 具体表现 | 影响 |
|------|---------|------|
| 管道组装硬编码 | `PipelineComposition.cs` 16 条管道手动 `.Use()` 链式编排 | 新增/调整管道必须修改此文件 |
| 泛型中间件手动注册 | `CrashSnapshotMiddleware<TContext>` 等 15+ 个泛型实例手动注册 | C# 开放泛型不能通过 `[Register]` 自动注册 |
| 子系统注册顺序手动 | `AddWorkflowServices` 中 15 个 `AddXxxServices()` 手动编排 | 新增子系统必须修改此方法 |
| 没有统一节点抽象 | 工具/中间件/命令/提示词各有各的注册机制 | 无法统一管理、组合、拆卸 |
| DI 注册不可逆 | 插件卸载不会自动回滚注册 | 卸载残留导致状态不一致 |
| 缺少省略即继承 | 没有 base profile + override 的组合模式 | 无法通过差异配置实现适配 |
| DynamicPluginRegistry AOT 断点 | `LoadHandlerTable` 依赖反射，AOT 下返回空表 | invoke 在 AOT 下永远 MethodNotFound |

---

## 2. 参考工程深度调研

### 2.1 DSH (DeepSeek Harness) — TypeScript/Node.js

**仓库**: https://github.com/deepseek-ai/deepseek-harness (278K+ stars, MIT)
**框架**: 基于 [Cordis](https://github.com/cordiverse/cordis)

#### 2.1.1 Service/Context — 不用特性标注的插件定义

DSH 不用特性标注定义插件。插件通过 **Service interface + Provider 函数** 定义：

```typescript
// Definition — 声明 ctx.llm 接口
declare module '@deepseek-ai/cordis' {
  interface Context { llm: LlmRuntime }
}

// Provider — 实现 Service
export class LlmRuntime extends TypertRemoteService {
  constructor(ctx: Context) { super(ctx, 'llm') }
  registerAdapter(providers: string[], adapter: LlmAdapter): AdapterRegistrationHandle { ... }
}

// Consumer — 通过 ctx.llm 使用
const tools = ctx(Tools);  // 获取 Tools 服务
```

**关键洞察**: Context 是 Proxy 对象，通过原型链实现服务隔离。`ctx(Service)` 是获取服务的唯一方式，不直接 import 实现。

#### 2.1.2 LlmAdapter — "插件作为父类"的具体实现

DSH 的 LLM 适配是"继承适配"的最佳示例：

```typescript
export abstract class LlmAdapter {
  // 唯一必须实现的方法
  abstract stream(options: GenerateOptions): AsyncIterable<StreamChunk>

  // 以下都是可选覆写 — 父类提供默认实现
  providerInfo(provider: string): LlmProviderInfo { return { id: provider, name: provider } }
  providerRetryPolicy(_provider: string): ResolvedRetryPolicy | undefined { return undefined }
  listModels(_provider: string): Promise<readonly LlmModelInfo[]> { return Promise.resolve([]) }
  resolveModel(provider: string, model: string): Promise<LlmResolvedModelInfo> { ... }
  async prepareCall(provider: string, model: string): Promise<PreparedAdapterCall> { ... }
}

// DeepSeek Provider — 继承 LlmAdapter，选择性覆写
export class DeepSeekAdapter extends LlmAdapter {
  override providerInfo(provider: string) { return { id: provider, name: 'DeepSeek' } }
  override providerRetryPolicy(_provider: string) { return this.dependencies.options().retryPolicy }
  override async listModels(provider: string) { return this.dependencies.discoverModels?.(provider) ?? [] }
  stream(options: GenerateOptions): AsyncIterable<StreamChunk> { ... }  // 必须实现
}
```

**关键洞察**: "插件作为父类"= 抽象类 + 默认实现 + 选择性覆写。父类定义契约和默认行为，子类通过 `override` 适配特定模型。这不是数据层面的继承，而是 **OOP 继承的正确用法**。

#### 2.1.3 ctx.effect() — 可逆注册的具体实现

```typescript
// 注册 adapter — 可逆 effect
const dispose = this.ctx.effect(function* (this: LlmRuntime) {
  // 正向操作：注册路由
  this.commitRoutes(owned, this.prepareRoutes(providers, adapter, owned))

  // yield 出 disposer — 反向操作
  yield () => {
    released = true
    for (const provider of owned) this.adapters.delete(provider)
    owned.clear()
    this.emitAdaptersUpdated()
  }
}.bind(this), 'llm.registerAdapter()')

// handle 支持 replace() — 原子替换路由
handle.replace = (next: string[]): void => { ... }
```

**关键洞察**: 可逆注册 = 正向操作 + yield disposer。Fiber 卸载时逆序执行所有 disposer。支持 `replace()` 原子替换。

#### 2.1.4 cordis.patch.yml — 数据驱动组合

```yaml
# base profile 的 patch
- insert:
    - id: llm
      name: '@deepseek-ai/dsh-llm'

    - id: llm-deepseek
      name: '@deepseek-ai/dsh-llm-deepseek-api-key'

    - id: agent-default-model
      name: '@deepseek-ai/dsh-agent-default-model'
      config:
        provider: deepseek-official
        model: deepseek-flash

    - id: tool-web
      name: '@deepseek-ai/dsh-tool-web'
      config:
        fetch: true
        searchTimeoutMs: 60000
      disabled: !!js "!ctx.get('profileContext')"  # 条件禁用
```

**关键格式**:
- `- insert:` 插入行到 profile root
- `id` 唯一标识，后续 patch 通过 id 定址替换
- `config` 整个替换（不合并）
- `disabled: !!js` JavaScript 表达式条件禁用
- **后写者胜** — 同 id 的行，最后一层的 config 完全替换前面的

**Profile 组合**:
| Profile | 组合方式 |
|---------|---------|
| `base` | 共享核心 patch 层 |
| `web-app` | base + 浏览器 GUI 层 |
| `headless` | base + 一次性 CLI 任务层 |
| `sdk-app` | base + SDK JSON-RPC 层 |
| `sdk-minimal` | 独立完整 patch 树（不依赖 base） |

### 2.2 Google Harness² — Python

**仓库**: https://github.com/google-research/harness2 (23K+ stars, Apache 2.0)

#### 2.2.1 SurfaceSpec — 数据驱动适配

```python
@dataclass(frozen=True)
class SurfaceSpec:
    name: str                          # "opencode" 或 "codex"
    always_on: tuple[str, ...]         # 始终注入的文件路径
    always_on_max: int                 # 总字符上限
    skill_dir: str                     # 技能目录路径
    skill_name_rule: str               # 命名规则
    trigger_hint: str                  # 触发提示
    autotrigger_file: str              # 自动触发文件
    trigger_line: str                  # 触发行模板
    subagent_requires_mode: bool       # 子代理是否需要 mode 字段
    plugin_dir: str | None             # 插件目录
    hooks_file: str | None             # hooks 文件路径
    hook_events: frozenset[str]        # 支持的 hook 事件
    compile_skill_scripts: bool        # 是否编译检查技能脚本
    stubs: tuple[tuple[str, str], ...] # README 桩文件
```

**两个 SurfaceSpec 实例**:
```python
OPENCODE_SURFACE = SurfaceSpec(
    name="opencode",
    always_on=("systemprompt.md", "guardrails.md", "LongTermMEMORY.md", "tool_descriptions/tool_guidance.md"),
    always_on_max=24_000,
    skill_dir="skills",
    plugin_dir="plugin",
    ...
)

CODEX_SURFACE = SurfaceSpec(
    name="codex",
    always_on=("AGENTS.md",),
    always_on_max=8_000,
    skill_dir=".agents/skills",
    plugin_dir=None,
    hooks_file=".codex/hooks.json",
    ...
)
```

**关键洞察**: 所有框架差异都是 SurfaceSpec 上的数据字段。共享逻辑（tree.py）只写一次，通过 SurfaceSpec 参数化。这是"数据驱动适配"而非"代码继承"。

#### 2.2.2 "省略即继承"的具体实现

```python
Tree = dict[str, str]  # rel路径 → 文件内容

def write_tree(surface, tree, dest):
    # 先删除所有可编辑表面上的已有文件
    for p in sorted(dest.rglob("*"), reverse=True):
        if is_allowed(surface, rel): p.unlink()

    # 确保 always_on 文件存在（空文件也行）
    for a in surface.always_on:
        if a not in tree and not p.exists():
            p.write_text("", encoding="utf-8")

    # 只写入 tree dict 中的文件 — 未在 dict 中的不会被写入
    for rel, content in tree.items():
        p = dest / rel
        p.write_text(content, encoding="utf-8")
```

**关键洞察**: "省略即继承"= Tree dict 只包含要改动的文件 + write_tree 先删后写。proposer 只发出变更文件，未提及的文件不在 dict 中，不会被写入新目录。这是**数据层面的继承**。

#### 2.2.3 Substrate Protocol — 不用继承的适配

```python
@runtime_checkable
class Substrate(Protocol):
    name: str
    def surface(self) -> SurfaceSpec: ...
    def materialise(self, tree_dir, spec, ctx) -> Install: ...
    def execute(self, task_ref, install, run_ids, timeout) -> list[Path]: ...
    def read_trajectory(self, stream_path, ...) -> str: ...
    def preflight(self) -> None: ...

# OpencodeSubstrate 不继承 Substrate，而是鸭子类型实现
class OpencodeSubstrate:
    def __init__(self, launcher: Launcher | None = None):
        self._launcher = launcher
    def surface(self) -> SurfaceSpec:
        return OPENCODE_SURFACE
```

**关键洞察**: Substrate 是 Protocol（结构化接口），不是基类。适配 = 实现同一 Protocol + 不同的 SurfaceSpec 数据值。

#### 2.2.4 Library — 经验积累

```python
# 磁盘结构
<root>/<run-tag>/<mode>/<domain>/
    INDEX.md                          # 自动生成的索引
    harnesses/<task>/harness/<rel>    # 完整采纳的组件树
    harnesses/<task>/EXPERIENCE.md    # 为什么选择 + 可迁移教训

# 有效经验的必需字段
def has_experience(experience):
    return all(experience.get(key) for key in ("cue", "lesson", "evidence"))
```

**关键洞察**: 经验复用是构造性的 — domain 目录只含同 domain 条目，无需搜索过滤。每次 sweep 从空开始（按 run/mode 隔离）。

### 2.3 两个参考工程的核心设计模式总结

| 模式 | DSH 实现 | Harness² 实现 | 共同原则 |
|------|---------|--------------|---------|
| 不用特性标注 | Service interface + Provider 函数 | 文件系统 + SurfaceSpec | 通过接口/数据定义，而非特性标注 |
| 不用传统继承 | 注册 adapter 到 ctx.llm | Protocol + SurfaceSpec 数据 | 适配 = 实现接口 + 不同数据值 |
| 差异编码为数据 | cordis.patch.yml | SurfaceSpec dataclass | 配置层面表达差异 |
| 可逆注册 | ctx.effect() + disposer | N/A（文件操作） | 注册时声明清理逻辑 |
| 省略即继承 | patch id 替换（后写者胜） | Tree dict 省略 + write_tree 先删后写 | 只声明差异，未提及的从 base 继承 |
| 插件作为父类 | LlmAdapter 抽象类 + override | Substrate Protocol + SurfaceSpec | 抽象类/接口 + 默认实现 + 选择性覆写 |

### 2.4 AOT 约束的关键差异

两个参考工程都不受 AOT 约束（TypeScript/Python），可以运行时动态加载。当前项目是 C# + NativeAOT，必须**编译期确定所有插件类型**，但可以**运行期通过数据配置决定加载哪些、如何组合**。

---

## 3. 当前项目插件体系现状

### 3.1 已有的插件基础设施（AOT 兼容性）

| 组件 | AOT 兼容 | 说明 |
|------|---------|------|
| PluginManager (三种 Host) | ✅ | Workflow/External/Native 三种加载方式都 AOT 兼容 |
| WorkflowPluginBase | ✅ | `new TPlugin()` 编译时已知，无反射 |
| WorkflowPluginHost | ✅ | ServiceCollection + ServiceProvider |
| ExternalPluginHost | ✅ | Process + stdio |
| NativePluginHost | ✅ | NativeLibrary + P/Invoke |
| DynamicPluginRegistry | ⚠️ | define/stop/undefine/inspect 可用；run 的 ALC 加载不可用；invoke 的 handler 表为空 |
| PluginAlc (ALC) | ❌ | `LoadFromAssemblyPath` 依赖反射，AOT 下被 trim |
| PluginDependencyGraph | ✅ | 纯内存数据结构 |
| CapabilityGate | ✅ | FrozenSet + 运行时校验 |

### 3.2 已有的源码生成器体系

15 个 IIncrementalGenerator，838 处 `[Register]`，30+ 自定义特性。这些是 C# + AOT 下最接近 DSH Service interface + Provider 函数的等价物。

### 3.3 硬编码点清单

| 硬编码点 | 文件 | 问题 | 应改为 |
|---------|------|------|--------|
| 16 条管道中间件顺序 | `PipelineComposition.cs` | 手动 `.Use()` 链 | 数据驱动组合 |
| 泛型中间件实例化 | `PipelineComposition.cs` | 15+ 个手动注册 | 源码生成器生成 |
| 子系统注册顺序 | `ServiceRegistration.cs` | 15 个手动编排 | 数据驱动排序 |
| ToolExecution 管道 | `ServiceRegistration.Mcp.cs` | 15 个手动 .Use() | 数据驱动组合 |

### 3.4 没有统一"节点"抽象

各类型各自独立：IAgent、ICommand、IMiddleware<T>、MCP 工具、提示词模板等，没有统一的 INode 抽象。

---

## 4. 架构设计方向

### 4.1 核心原则

基于两个参考工程的深度调研，确定以下原则：

| 原则 | 来源 | 说明 |
|------|------|------|
| **不新增特性** | 用户要求 + DSH/Harness² 都不用特性标注 | 现有 [Register] 等保持不变，统一管理在运行期实现 |
| **运行期统一管理** | DSH Context | NodeRegistry 从 DI 容器解析所有服务，统一管理为"节点" |
| **数据驱动组合** | DSH cordis.patch.yml + Harness² SurfaceSpec | NodeProfile YAML 配置决定哪些节点激活、如何组合 |
| **省略即继承** | Harness² Tree dict | base profile 定义默认组合，子 profile 只声明差异 |
| **两种继承形态** | DSH LlmAdapter + Harness² SurfaceSpec | 代码层面（抽象类+覆写）+ 数据层面（Profile 省略即继承） |
| **可逆注册** | DSH ctx.effect() | 节点注册时声明 cleanup，卸载时自动逆序回滚 |
| **AOT 优先** | 项目约束 | 所有设计必须 AOT 兼容 |

### 4.2 "万物皆节点"的运行期实现

**不新增 [Node] 特性**。现有 [Register] 等保持不变。"节点"是运行期的统一管理抽象：

```
DI 容器（编译期 [Register] 自动注册）
  ↓
NodeRegistry（运行期统一管理）
  ├── 按节点 ID 索引（节点本身无 NodeKind 固有属性）
  ├── 消费点从 YAML 配置获取需要的节点列表
  │     chat-pipeline.middleware: [crash-snapshot-mw, rate-limit-mw, ...]
  │     tool-dispatch.tools: [shell-tool, fs-tool, web-tool]
  │     command-dispatch.commands: [shell-command, fs-command]
  │     prompt-assembly.prompts: [security-prompt, system-prompt]
  ├── 按 Profile 过滤激活/排除
  └── 按 Priority 排序（管道组装等）
```

**关键设计**: NodeKind 不是节点的固有属性，而是消费点的分类需求。一个节点可以出现在多个消费点（如 shell-tool 同时在 tool-dispatch 和 command-dispatch 中）。消费点从 YAML 配置自由搭配需要的节点列表，不需要优先级推断，不会冲突。

### 4.3 "节点皆可继承"的两种形态

**形态1：代码层面继承**（类似 DSH LlmAdapter）

```csharp
// 父类定义契约和默认行为
public abstract class LlmAdapter {
    public abstract IAsyncEnumerable<StreamChunk> Stream(GenerateOptions options);
    public virtual ProviderInfo GetProviderInfo(string provider) => new(provider, provider);
    public virtual RetryPolicy? GetRetryPolicy(string provider) => null;
    public virtual Task<IReadOnlyList<ModelInfo>> ListModels(string provider) => Task.FromResult<IReadOnlyList<ModelInfo>>([]);
}

// 子类通过 override 适配特定模型
public class DeepSeekAdapter : LlmAdapter {
    public override IAsyncEnumerable<StreamChunk> Stream(GenerateOptions options) { ... }
    public override ProviderInfo GetProviderInfo(string provider) => new(provider, "DeepSeek");
    public override RetryPolicy? GetRetryPolicy(string provider) => _options.RetryPolicy;
}
```

**形态2：数据层面继承**（类似 Harness² Tree dict + DSH cordis.patch.yml）

```yaml
# base profile — 消费点定义它需要哪些节点（不是节点声明自己是什么类型）
name: base
consumers:
  chat-pipeline:
    middleware:
      - id: crash-snapshot-mw
        priority: 100
      - id: rate-limit-mw
        priority: 200
      - id: security-check-mw
        priority: 300
      - id: metrics-mw
        priority: 400
  tool-dispatch:
    tools:
      - shell-tool
      - fs-tool
      - web-tool
  command-dispatch:
    commands:
      - shell-command
      - fs-command
  prompt-assembly:
    prompts:
      - security-prompt
      - system-prompt
  agent-registry:
    agents:
      - default-agent
  scenario-registry:
    scenarios:
      - default-scenario

# headless profile — 继承 base，移除 GUI 相关
name: headless
inherits: base
excludes:
  - gui-module
  - theme-command
  - vim-mode-plugin

# ci profile — 继承 headless，覆写某些节点属性
name: ci
inherits: headless
excludes:
  - web-tool
overrides:
  - consumer: chat-pipeline
    node: shell-tool
    priority: 200
    config:
      timeout: 600
```

**省略即继承机制**：子 profile 只声明差异（excludes/overrides/includes），未提及的节点从父 profile 继承。多层继承链逐级合并。

### 4.4 "可随时组织某个插件，也可以某种情况不加载"的运行期实现

通过 NodeProfile 的 `excludes` 和 `includes` 实现：

```yaml
# 某种情况不加载某个插件
name: minimal
inherits: base
excludes:
  - web-tool           # 不加载 Web 工具
  - metrics-mw         # 不加载指标中间件

# 随时组织某个插件（运行期切换 Profile）
# 启动时: jcc --profile minimal
# 运行时: /profile base  （切换到全功能模式）
```

条件禁用（类似 DSH 的 `disabled: !!js`）：

```yaml
# 条件禁用 — 根据运行时条件决定是否加载
name: base
consumers:
  tool-dispatch:
    tools:
      - id: web-tool
        enabled-when: "feature.web-search"   # 当 feature flag 开启时才加载
```

---

## 5. 具体设计方案

### 5.1 NodeRegistry — 运行期统一节点管理

```csharp
public sealed class NodeRegistry {
    private readonly FrozenDictionary<string, NodeEntry> _nodes;

    // 从 DI 容器构建 — 编译期 [Register] 注册的所有服务自动成为节点
    public static NodeRegistry Build(IServiceProvider sp) {
        // 扫描 DI 容器中所有已注册的服务
        // 按节点 ID（类名或 [Register] 的接口类型名）索引
        // 节点本身无 NodeKind 固有属性
        ...
    }

    public INode? GetNode(string nodeId);
    public IReadOnlyList<INode> GetNodesForConsumer(string consumerId, ResolvedProfile profile);
    public IReadOnlyList<IMiddleware<TContext>> GetMiddlewareForPipeline<TContext>(string pipelineName, ResolvedProfile profile);
}
```

### 5.2 INode — 运行期节点适配接口

```csharp
public interface INode {
    string NodeId { get; }
    NodeMetadata Metadata { get; }
    NodeState State { get; }
}

// Node 是运行期适配器，包装任意 DI 服务为 INode
internal sealed class Node<TService> : INode {
    public string NodeId { get; }        // 从类名或接口类型名推断
    public NodeMetadata Metadata { get; } // 从 NodeProfile 配置加载
    public TService Service { get; }      // DI 容器解析的实际服务
}
```

### 5.3 消费点驱动 — 不用 NodeKind 枚举

节点本身无 NodeKind 固有属性。消费点从 YAML 配置获取需要的节点列表：

```csharp
// 消费点接口 — 每种消费点定义自己的节点获取方式
public interface INodeConsumer {
    string ConsumerId { get; }
    IReadOnlyList<INode> ResolveNodes(NodeRegistry registry, ResolvedProfile profile);
}

// 具体消费点示例
public sealed class PipelineConsumer : INodeConsumer {
    public string ConsumerId => "chat-pipeline";
    public IReadOnlyList<INode> ResolveNodes(NodeRegistry registry, ResolvedProfile profile) {
        // 从 profile 中获取 chat-pipeline.middleware 列表
        // 按 priority 排序
        // 从 registry 中解析每个节点
    }
}

public sealed class ToolDispatchConsumer : INodeConsumer {
    public string ConsumerId => "tool-dispatch";
    public IReadOnlyList<INode> ResolveNodes(NodeRegistry registry, ResolvedProfile profile) {
        // 从 profile 中获取 tool-dispatch.tools 列表
        // 从 registry 中解析每个节点
    }
}
```

### 5.4 NodeProfile — 数据驱动组合配置（YAML）

```yaml
# profiles/base.yaml — 消费点驱动，不是节点声明 kind
name: base
consumers:
  chat-pipeline:
    middleware:
      - id: crash-snapshot-mw
        priority: 100
      - id: rate-limit-mw
        priority: 200
      - id: security-check-mw
        priority: 300
      - id: metrics-mw
        priority: 400
  tool-dispatch:
    tools:
      - shell-tool
      - fs-tool
      - web-tool
  command-dispatch:
    commands:
      - shell-command
      - fs-command
  prompt-assembly:
    prompts:
      - security-prompt
      - system-prompt
  agent-registry:
    agents:
      - default-agent
  scenario-registry:
    scenarios:
      - default-scenario

# profiles/headless.yaml
name: headless
inherits: base
excludes:
  - gui-module
  - theme-command
  - vim-mode-plugin

# profiles/sdk.yaml
name: sdk
inherits: headless
excludes:
  - web-tool
  - metrics-mw
  - crash-snapshot-mw
includes:
  - sdk-api-tool
overrides:
  - consumer: chat-pipeline
    node: shell-tool
    priority: 200
    config:
      timeout: 600
```

### 5.5 NodeProfileResolver — 省略即继承的合并器

```csharp
public sealed class NodeProfileResolver {
    // 解析继承链，合并出最终消费点配置
    public ResolvedProfile Resolve(string profileName) {
        var profile = LoadProfile(profileName);
        if (profile.Inherits is null) return ResolveSelf(profile);

        var parent = Resolve(profile.Inherits);  // 递归解析父 profile
        return Merge(parent, profile);            // 合并：父 + 子的差异
    }

    private ResolvedProfile Merge(ResolvedProfile parent, NodeProfile child) {
        // 1. 从父继承所有消费点配置
        var consumers = new Dictionary<string, ConsumerConfig>(parent.Consumers);

        // 2. 排除子 profile 明确移除的节点（从所有消费点中移除）
        foreach (var nodeId in child.Excludes)
            foreach (var consumer in consumers.Values)
                consumer.RemoveNode(nodeId);

        // 3. 添加子 profile 明确新增的消费点或节点
        foreach (var consumer in child.Includes)
            consumers[consumer.Key] = consumer.Value;

        // 4. 覆写子 profile 明确修改的节点属性（在指定消费点中）
        foreach (var override in child.Overrides)
            if (consumers.TryGetValue(override.Consumer, out var consumer))
                consumer.OverrideNode(override.Node, override.Priority, override.Config);

        return new ResolvedProfile(consumers);
    }
}
```

### 5.6 数据驱动的管道组装

当前 `PipelineComposition.cs` 的硬编码改为数据驱动：

```csharp
// 当前：硬编码
var chatPipeline = new StreamMiddlewarePipelineBuilder<ChatMiddlewareContext, ChatStreamEvent>()
    .Use(sp.GetRequiredService<CrashSnapshotMiddleware<ChatMiddlewareContext>>())
    .Use(sp.GetRequiredService<RateLimitMiddleware<ChatMiddlewareContext>>())
    // ... 14 个中间件手动编排
    .Build();

// 改后：数据驱动
var chatPipeline = PipelineBuilder.FromProfile<ChatMiddlewareContext, ChatStreamEvent>(
    profile: resolvedProfile,                    // NodeProfile 合并结果
    registry: sp.GetRequiredService<NodeRegistry>()  // 编译期生成的节点注册表
);
// 从 profile 的 pipeline.chat.middleware 列表 + priority 排序自动组装
```

### 5.7 可逆注册 — NodeContext.RegisterEffect

类似 DSH 的 `ctx.effect()`：

```csharp
public sealed class NodeContext {
    private readonly Stack<Func<ValueTask>> _cleanupStack = new();

    // 注册可逆副作用 — 返回值 + cleanup 委托
    public T RegisterEffect<T>(T value, Func<ValueTask> cleanup) {
        _cleanupStack.Push(cleanup);
        return value;
    }

    // 卸载时逆序回滚所有副作用
    public async ValueTask UnloadAsync() {
        while (_cleanupStack.TryPop(out var cleanup)) {
            await cleanup().ConfigureAwait(false);
        }
    }
}
```

使用示例：

```csharp
public async ValueTask LoadAsync(NodeContext ctx, CancellationToken ct) {
    // 注册命令 — 卸载时自动移除
    ctx.RegisterEffect(
        CommandRegistry.Register("my-command", MyHandler),
        () => CommandRegistry.Unregister("my-command")
    );

    // 注册事件订阅 — 卸载时自动取消
    ctx.RegisterEffect(
        EventBus.Subscribe<MyEvent>(MyHandler),
        sub => sub.Unsubscribe()
    );
}
```

### 5.8 DynamicPluginRegistry AOT 改造

将 handler 加载从反射改为**编译期源码生成器注册**：

1. 新增 `[NodeHandler]` 特性 — 标记方法为节点 handler
2. 新增 NodeHandlerGenerator — 扫描 `[NodeHandler]`，生成 `CompiledNodeHandlers` 类
3. DynamicPluginRegistry.Invoke 改为从 `CompiledNodeHandlers` 路由

```csharp
// 编译期生成的 handler 注册代码
internal static class CompiledNodeHandlers {
    private static readonly FrozenDictionary<string, FrozenDictionary<string, Func<object?[], object?>>> _handlers = ...;

    public static FrozenDictionary<string, Func<object?[], object?>>? GetHandlers(string nodeId)
        => _handlers.TryGetValue(nodeId, out var h) ? h : null;
}

// DynamicPluginRegistry.Invoke 改造
public InvokeResult Invoke(string name, string method, object?[] args) {
    var handlers = CompiledNodeHandlers.GetHandlers(name);
    if (handlers is null) return InvokeResult.PluginNotRunning;
    if (!handlers.TryGetValue(method, out var handler)) return InvokeResult.MethodNotFound;
    try { return InvokeResult.Ok(handler(args)); }
    catch (Exception ex) { return InvokeResult.HandlerError(ex); }
}
```

### 5.9 泛型中间件自动注册

新增 MiddlewareRegistrationGenerator，扫描 `[MiddlewareContext(typeof(XxxContext))]` 特性，自动生成泛型实例化注册代码：

```csharp
// 标注
[MiddlewareContext(typeof(ChatMiddlewareContext))]
public class CrashSnapshotMiddleware<TContext> : IMiddleware<TContext> { ... }

// 生成器自动生成
services.AddSingleton<IMiddleware<ChatMiddlewareContext>>(
    sp => new CrashSnapshotMiddleware<ChatMiddlewareContext>(sp.GetRequiredService<ILogger>())
);
```

---

## 6. 实施计划

| 阶段 | 内容 | 优先级 | 依赖 | 状态 |
|------|------|--------|------|------|
| P1 | INode + NodeMetadata + NodeState + INodeConsumer + INodeRegistry | 高 | 无 | ✅ 已完成 |
| P2 | NodeProfile YAML + NodeProfileResolver（省略即继承） | 高 | P1 | ✅ 已完成 |
| P3 | NodeProfileLoader + NodeRegistry + Node<TService> + 诊断命令 + DI 注册 | 高 | P1, P2 | ✅ 已完成 |
| P4 | 数据驱动的管道组装（替代 PipelineComposition.cs） | 高 | P1-P3 | ❌ 待实施 |
| P5 | NodeContext 可逆注册（复用现有 EffectScope） | 中 | P1 | ❌ 待实施 |
| P6 | DynamicPluginRegistry AOT 改造 | 中 | P1 | ❌ 待实施 |
| P7 | 泛型中间件自动注册（MiddlewareRegistrationGenerator） | 中 | P1 | ❌ 待实施 |
| P8 | LlmAdapter 式继承适配模式推广 | 低 | P1-P7 | ❌ 待实施 |

---

## 7. 验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| INode + NodeMetadata + NodeState | 运行期统一节点接口 | ✅ `modules/abstractions/composition/node/interfaces/` | ✅ 编译通过 |
| INodeConsumer + INodeRegistry | 消费点驱动节点获取 | ✅ `modules/abstractions/composition/node/interfaces/` | ✅ 编译通过 |
| NodeProfile + ConsumerConfig + ResolvedProfile | Profile 数据结构 | ✅ `modules/abstractions/composition/node/profile/` | ✅ 编译通过 |
| NodeProfileLoader (YAML 内嵌资源) | Profile YAML 加载 | ✅ `modules/composition/composition/node_registry/NodeProfileLoader.cs` | ✅ `jcc --dump-nodes` 输出正确 |
| NodeProfileResolver (省略即继承) | 继承链解析合并 | ✅ `modules/composition/composition/node_registry/NodeProfileResolver.cs` | ✅ `jcc --dump-profile sdk` 验证排除/新增/覆写 |
| NodeRegistry + NodeEntry + Node<TService> | 运行期节点注册表 | ✅ `modules/composition/composition/node_registry/NodeRegistry.cs` | ✅ 编译通过 + DI 注册 |
| base/headless/sdk/ci Profile YAML | 四个预设 Profile | ✅ `resources/profiles/` | ✅ `jcc --dump-nodes` 全部输出正确 |
| --dump-nodes / --dump-profile / --profile CLI | 诊断命令 | ✅ `modules/apps/cli/core/commands/core/Program.cs` | ✅ exe 启动验收通过 |
| ServiceRegistration DI 注册 | NodeRegistry + ProfileResolver 注入 | ✅ `modules/composition/composition/dependency_injection/core/ServiceRegistration.cs` | ✅ 编译通过 |
| 数据驱动的管道组装 | PipelineComposition.cs 不再硬编码 | ❌ 阶段2 | ❌ |
| NodeContext 可逆注册 | 节点卸载时自动回滚 | ❌ 阶段4（复用现有 EffectScope） | ❌ |
| DynamicPluginRegistry AOT 改造 | invoke 在 AOT 下能路由 | ❌ 阶段5 | ❌ |
| 泛型中间件自动注册 | CrashSnapshotMiddleware 等不再手动注册 | ❌ 阶段6 | ❌ |
| LlmAdapter 式继承适配 | 抽象类+默认实现+覆写模式推广 | ❌ 阶段7 | ❌ |

---

## 8. 风险与缓解

| 风险 | 影响 | 缓解 |
|------|------|------|
| NodeRegistry 从 DI 容器扫描可能遗漏 | 某些节点未被识别 | 提供 `--dump-nodes` 诊断命令；CI 验证节点数量 |
| Profile YAML 继承链复杂 | 多层继承难以理解 | 限制继承深度 ≤3；提供 `--dump-profile` 查看合并结果 |
| 管道组装数据驱动后调试困难 | 中间件顺序问题难以定位 | Profile 中记录每个中间件的 source profile；日志输出组装过程 |
| 可逆注册的 cleanup 异常 | 回滚失败导致状态不一致 | cleanup 异常不传播，记录警告日志；提供强制清理命令 |
| AOT 下 CompiledNodeHandlers 遗漏 | 某些 handler 未注册 | CI 中增加 AOT 发布测试 |

---

## 9. 决策记录

| # | 问题 | 决策 | 日期 | 理由 |
|---|------|------|------|------|
| 1 | NodeProfile 配置格式 | **YAML** | 2026-11-12 | 更适合人类编辑，DSH 也用 YAML，支持注释、多行字符串 |
| 2 | 是否新增 [Node] 特性 | **不新增** | 2026-11-12 | 保持纯粹，不增加特性名称；统一管理在运行期实现 |
| 3 | "继承"的实现方式 | **两种形态** | 2026-11-12 | 代码层面（抽象类+覆写）+ 数据层面（Profile 省略即继承） |
| 4 | NodeProfile 存储位置 | **项目内嵌资源** | 2026-11-12 | AOT 兼容，不需要外部文件依赖；用户自定义 Profile 放用户目录覆盖 |
| 5 | 运行期 Profile 切换 | **支持** | 2026-11-12 | 运行期可通过命令切换，需处理节点卸载+重新加载的状态一致性（依赖可逆注册机制） |
| 6 | NodeKind 推断规则 | **消费点驱动，非节点固有属性** | 2026-11-12 | 节点本身无状态类型标注；消费点通过 YAML 配置自由搭配需要的节点列表。一个节点可出现在多个消费点 |
| 7 | 条件禁用语法 | **Feature flag 名称** | 2026-11-12 | `enabled-when: \"feature.web-search\"` — 简单的 feature flag 名称，当 flag 开启时才加载 |
| 8 | 继承适配推广范围 | **数据层面继承为主，代码层面继承只在不可避免场景** | 2026-11-12 | 与 DSH 一致：大部分场景用 Profile YAML 数据层面继承；只有 LLM API 适配等不可避免的代码逻辑差异用抽象类+覆写。开发者日常只需理解 YAML 配置 |

---

## 10. 所有决策已确认
