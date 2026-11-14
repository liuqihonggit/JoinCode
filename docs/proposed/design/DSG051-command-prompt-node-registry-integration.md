# DSG051: command-dispatch / prompt-assembly 纳入 NodeRegistry 统一管理

> 📍 **导航**: [docs/](../README.md) › [design/](README.md) | **前置**: [DSG048](DSG048-node-plugin-inheritance-architecture.md)
> 状态: proposed
> 创建: 2026-11-14
> 关联: [DSG048](DSG048-node-plugin-inheritance-architecture.md), [DSG049](DSG049-runtime-profile-switching.md)

---

## 1. 问题

DSG048 已建立 NodeRegistry 架构，管道中间件和工具分发已纳入节点管理。但 `command-dispatch` 和 `prompt-assembly` 两个消费点仍走独立路径，未纳入 NodeRegistry：

**command-dispatch**：
- 当前通过 `[ChatCommand]` 特性 + 源码生成器 → `ChatCommandRegistry` → `SlashCommandRunner` 独立路径
- Profile YAML 中 `command-dispatch` 消费点有占位配置（`shell-command`, `fs-command`），但无对应 INode 实现
- `CommandDispatchConsumer` 是占位类，生产代码从未调用

**prompt-assembly**：
- 当前通过 `[PromptSection]` 特性 + 源码生成器 → `PromptSectionRegistration` → `DefaultSystemPromptProvider` → `SystemPromptBuilder` → `SystemPromptMiddleware` 独立路径
- Profile YAML 中 `prompt-assembly` 消费点有占位配置（`system-prompt`, `security-prompt` 等），但无对应 INode 实现
- `PromptAssemblyConsumer` 是占位类，生产代码从未调用

**统一管理的价值**：
- Profile 切换时命令和提示词也能动态变化（如 headless 模式禁用某些命令/提示词）
- 继承链（Inherits/Excludes/Overrides）对命令和提示词同样生效
- 统一的可观测性（`--dump-nodes` 包含所有消费点）

## 2. 目标

- `command-dispatch` 消费点通过 NodeRegistry 管理命令节点
- `prompt-assembly` 消费点通过 NodeRegistry 管理提示词节点
- Profile YAML 配置控制命令和提示词的启用/禁用/排序
- 现有 `[ChatCommand]` 和 `[PromptSection]` 特性 + 源码生成器机制保留（作为节点工厂的数据源）
- 渐进式改造，不破坏现有执行链路

## 3. 设计

### 3.1 command-dispatch 纳入 NodeRegistry

#### 3.1.1 命令粒度决策

**选择：按命令分类（粗粒度）**，与 Profile YAML 现有配置对齐。

Profile YAML `base.yaml` 已定义的分类：
```yaml
command-dispatch:
  nodes:
    - id: shell-command      # shell 相关命令（/bash, /exec 等）
    - id: fs-command         # 文件系统命令（/read, /write 等）
    - id: info-command       # 信息查询命令（/help, /status 等）
    - id: session-command    # 会话管理命令（/compact, /clear 等）
    - id: config-command     # 配置命令（/settings, /vendor 等）
    - id: agent-command      # Agent 命令（/agent, /goal 等）
```

每个分类节点内部包含多个具体命令，由源码生成器按 `[ChatCommand]` 特性的 `Category` 字段自动归类。

#### 3.1.2 CommandCategoryNode — 命令分类节点

```csharp
public sealed class CommandCategoryNode : INode {
    public string Id { get; }                    // "shell-command", "fs-command" 等
    public int Priority { get; }
    public FrozenList<IChatCommand> Commands { get; }  // 该分类下的所有命令

    public CommandCategoryNode(string id, int priority, FrozenList<IChatCommand> commands) {
        Id = id;
        Priority = priority;
        Commands = commands;
    }
}
```

#### 3.1.3 节点工厂注册

在 `DataDrivenPipelineComposition.BuildNodeFactories()` 中追加 `command-dispatch` 消费点的节点工厂：

```csharp
// command-dispatch 节点工厂
foreach (var category in ChatCommandCategoryEnumConstants.All) {
    var categoryId = $"{category.ToValue()}-command";
    factories[(consumerId: "command-dispatch", nodeId: categoryId)] = sp => {
        var registry = sp.GetRequiredService<ChatCommandRegistry>();
        var commands = registry.GetByCategory(category);
        return new CommandCategoryNode(categoryId, priority: 100, commands);
    };
}
```

#### 3.1.4 SlashCommandRunner 改造

**当前**：`SlashCommandRunner` 直接从 DI 获取 `ChatCommandRegistry`，通过 `CmdMap` 路由。

**改造后（双路查询）**：
1. 优先从 `NodeRegistry.GetServicesForConsumerAsync("command-dispatch", profile)` 获取启用的命令分类节点
2. 从节点中提取 `Commands` 列表，构建 `CmdMap`
3. 如果 NodeRegistry 中无配置，回退到直接从 `ChatCommandRegistry` 获取所有命令

### 3.2 prompt-assembly 纳入 NodeRegistry

#### 3.2.1 提示词节点粒度

**选择：按 PromptSection 分类（粗粒度）**，与 Profile YAML 现有配置对齐。

Profile YAML `base.yaml` 已定义的分类：
```yaml
prompt-assembly:
  nodes:
    - id: system-prompt       # 系统提示词（角色定义、行为约束）
    - id: security-prompt     # 安全提示词（安全策略、权限边界）
    - id: tool-guidance-prompt # 工具使用指导
    - id: memory-prompt       # 记忆/上下文提示词
```

每个分类节点内部包含多个 `[PromptSection]`，由源码生成器按特性字段自动归类。

#### 3.2.2 PromptSectionCategoryNode — 提示词分类节点

```csharp
public sealed class PromptSectionCategoryNode : INode {
    public string Id { get; }                         // "system-prompt", "security-prompt" 等
    public int Priority { get; }
    public FrozenList<SystemPromptSection> Sections { get; }  // 该分类下的所有 section

    public PromptSectionCategoryNode(string id, int priority, FrozenList<SystemPromptSection> sections) {
        Id = id;
        Priority = priority;
        Sections = sections;
    }
}
```

#### 3.2.3 节点工厂注册

在 `DataDrivenPipelineComposition.BuildNodeFactories()` 中追加 `prompt-assembly` 消费点的节点工厂：

```csharp
// prompt-assembly 节点工厂
foreach (var category in PromptSectionCategoryEnumConstants.All) {
    var categoryId = $"{category.ToValue()}-prompt";
    factories[(consumerId: "prompt-assembly", nodeId: categoryId)] = sp => {
        var provider = sp.GetRequiredService<ISystemPromptProvider>();
        var sections = provider.GetSectionsByCategory(category);
        return new PromptSectionCategoryNode(categoryId, priority: 100, sections);
    };
}
```

#### 3.2.4 SystemPromptMiddleware 改造

**当前**：`SystemPromptMiddleware` 通过 `SystemPromptBuilder` + `DefaultSystemPromptProvider` 获取 sections。

**改造后（双路查询）**：
1. 优先从 `NodeRegistry.GetServicesForConsumerAsync("prompt-assembly", profile)` 获取启用的提示词分类节点
2. 从节点中提取 `Sections` 列表，按 Priority 排序后传入 `SystemPromptBuilder`
3. 如果 NodeRegistry 中无配置，回退到直接从 `DefaultSystemPromptProvider` 获取所有 sections

### 3.3 改造范围

| 组件 | 文件路径 | 改造内容 |
|------|---------|---------|
| `CommandCategoryNode` | 新增 `modules/composition/composition/node_registry/consumers/CommandCategoryNode.cs` | 命令分类节点 |
| `PromptSectionCategoryNode` | 新增 `modules/composition/composition/node_registry/consumers/PromptSectionCategoryNode.cs` | 提示词分类节点 |
| `DataDrivenPipelineComposition` | `modules/composition/pipelines/registration/DataDrivenPipelineComposition.cs` | 追加 command-dispatch / prompt-assembly 节点工厂注册 |
| `SlashCommandRunner` | `modules/apps/engine/commands/slash/SlashCommandRunner.cs` | 双路查询：NodeRegistry → 回退 ChatCommandRegistry |
| `SystemPromptMiddleware` | `modules/cognition/brain.context_services/preprocess/SystemPromptMiddleware.cs` | 双路查询：NodeRegistry → 回退 DefaultSystemPromptProvider |
| `ChatCommandRegistry` | `modules/composition/slash/infrastructure/command_system/ChatCommandRegistry.cs` | 追加 `GetByCategory` 方法 |
| `ISystemPromptProvider` | `modules/cognition/prompts/contracts/ISystemPromptProvider.cs` | 追加 `GetSectionsByCategory` 方法 |
| `DefaultSystemPromptProvider` | `modules/cognition/prompts/core/DefaultSystemPromptProvider.cs` | 实现 `GetSectionsByCategory` |
| Profile YAML | `resources/profiles/base.yaml` | 完善 command-dispatch / prompt-assembly 节点配置 |

### 3.4 不变组件

| 组件 | 原因 |
|------|------|
| `[ChatCommand]` 特性 + 源码生成器 | 仍是命令注册的数据源，只是消费方式改变 |
| `[PromptSection]` 特性 + 源码生成器 | 仍是提示词注册的数据源，只是消费方式改变 |
| `ChatCommandRegistry` | 仍负责命令的注册和解析，追加 `GetByCategory` 方法 |
| `SystemPromptBuilder` | 仍负责提示词组装，只是 sections 来源改为 NodeRegistry |
| `PromptSectionRegistration` | 仍由源码生成器生成，作为 `DefaultSystemPromptProvider` 的数据源 |

### 3.5 渐进式实现

**阶段1：适配层（不改变现有执行链路）**
- 新建 `CommandCategoryNode` 和 `PromptSectionCategoryNode`
- 在 `BuildNodeFactories()` 中注册 command-dispatch / prompt-assembly 节点工厂
- `ChatCommandRegistry` 追加 `GetByCategory` 方法
- `ISystemPromptProvider` 追加 `GetSectionsByCategory` 方法
- 现有执行链路保持不变

**阶段2：双路查询（优先 NodeRegistry，回退原路径）**
- `SlashCommandRunner` 增加从 `NodeRegistry` 获取命令的路径
- `SystemPromptMiddleware` 增加从 `NodeRegistry` 获取提示词节点的路径
- 优先从 NodeRegistry 查，查不到回退到原路径

**阶段3：Profile 配置完善**
- 完善 `base.yaml` 中 command-dispatch / prompt-assembly 的节点列表
- 验证 Profile 继承链对命令和提示词的生效（如 headless 模式禁用某些命令）

**阶段4：移除旧路径（可选，需评估风险）**
- 移除 `SlashCommandRunner` 对 `ChatCommandRegistry` 的直接依赖
- 移除 `SystemPromptMiddleware` 对 `DefaultSystemPromptProvider` 的直接依赖
- `CommandDispatchConsumer` / `PromptAssemblyConsumer` 成为真正的消费入口

## 4. 风险与缓解

| 风险 | 缓解 |
|------|------|
| 改造核心执行链路（命令执行/提示词组装） | 阶段1-2 双路查询保证回退安全 |
| 命令分类粒度与现有 `[ChatCommand]` Category 不匹配 | 需审查所有 `[ChatCommand]` 的 Category 字段，确保与 Profile YAML 分类对齐 |
| 提示词 section 分组逻辑迁移 | 阶段1 只追加 `GetSectionsByCategory` 方法，不改原有 `GetSections` 逻辑 |
| Profile 切换时命令/提示词动态变化 | 与 DSG050 管道动态重建配合，NodeRegistry 查询时从 `IProfileManager.Current` 获取 Profile |

## 5. 验收

| 验收项 | 方法 |
|--------|------|
| NodeRegistry 包含 command-dispatch 节点 | `--dump-nodes` 输出包含 command-dispatch 消费点 |
| NodeRegistry 包含 prompt-assembly 节点 | `--dump-nodes` 输出包含 prompt-assembly 消费点 |
| 命令执行正常 | 双路查询下所有斜杠命令正常执行 |
| 提示词组装正常 | 双路查询下系统提示词内容正确 |
| Profile 排除命令生效 | headless Profile 中排除某些命令，执行时不可用 |
| Profile 排除提示词生效 | headless Profile 中排除某些提示词，组装时不包含 |

## 6. 待确认决策

1. **命令分类标准**：当前 `[ChatCommand]` 的 `Category` 字段值是否与 Profile YAML 中的 `shell-command`/`fs-command` 等分类对齐？需要审查所有命令的 Category。
2. **提示词分类标准**：当前 `[PromptSection]` 是否有 Category 字段？如果没有，需要追加分类字段还是按其他方式归类？
3. **阶段4是否执行**：移除旧路径（直接从 ChatCommandRegistry/DefaultSystemPromptProvider 获取）是否值得风险？双路查询长期保留是否可接受？
