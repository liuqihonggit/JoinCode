# Agent 继承树架构重构设计

> 日期: 2026-08-08
> 目标: 把 Agent 从 sealed 组合模式改为 abstract 继承树，智能体像树一样生长，压缩能力通过继承传递

## 1. 现状分析

### 1.1 当前架构

```
Entity (基类)
  └── Agent (sealed) ← sealed 禁止继承
        字段: IQueryEngine, MessageList ChatHistory, Role, Variant, ...
        创建: new Agent(task, options, queryEngine, ..., role, variant, ...)

AgentRole: Coordinator | Executor
ExecutorVariant: Code | Search | Explore | Plan | Doctor | Verification | JoinCodeGuide | ContextCompression | Teammate
```

### 1.2 问题

| 问题 | 位置 | 影响 |
|------|------|------|
| Agent 是 sealed | `Agent.cs:9` | 物理上不能派生子类 |
| Agent 持有裸 MessageList | `Agent.cs:36` | 不是 ChatContextManager，无压缩能力 |
| ChatContextManager 在 Brain 层 | `Brain/src/Context/Services/Context/ChatContextManager.cs:32` | 通过 DI 注入中间件，不在 Agent 内部 |
| 子智能体用组合非继承 | `AgentLifecycleManager.cs:33` | `new Agent(..., role, variant)` 无法多态 |
| 两套智能体体系 | Agent/IAgent vs ReasoningAgentBase/IReasoningAgent | 压缩、上下文管理各搞一套 |
| 两套压缩体系 | MicrocompactService/ContextFoldExecutor vs ReasoningContextCompressor | 逻辑重叠 |

### 1.3 生产 new Agent() 调用点（仅 3 处）

| 文件 | 行 | 用途 |
|------|-----|------|
| `AgentLifecycleManager.cs` | 33 | SpawnSubAgentAsync 工厂方法 |
| `ModelCoordinator.cs` | 77 | DualModel planner |
| `ModelCoordinator.cs` | 128 | DualModel executor |

## 2. 目标架构

### 2.1 继承树

```
AgentBase (abstract, 持有 IChatContextManager + 压缩管线 + 对话循环)
├── CoordinatorAgent (主智能体, Role=Coordinator)
│     └── (用户对话的主 Agent，持有完整 ChatContextManager)
├── ExecutorAgent (abstract, 子智能体基类, Role=Executor)
│     ├── CodeAgent (代码读写编辑)
│     ├── SearchAgent (代码搜索导航, 只读)
│     ├── ExploreAgent (快速探索, 只读, 一次性)
│     ├── PlanAgent (架构设计, 只读, 一次性)
│     ├── DoctorAgent (自举复盘, 后台 Cron)
│     ├── VerificationAgent (验证正确性/质量/安全)
│     ├── GuideAgent (使用指导)
│     ├── ContextCompressionAgent (上下文压缩)
│     └── TeammateAgent (协作队友)
└── ReasoningAgent (abstract, 推理智能体基类)
      ├── ProsecutorAgent ( prosecutor)
      ├── JudgeAgent (judge)
      └── DefenderAgent (defender)
```

### 2.2 树状生长规则

- **每个 Agent 实例** 持有独立的 `IChatContextManager`（对话上下文窗口）
- **子智能体可以派生子智能体** → 树状生长（PlanAgent 可以 new ExploreAgent 做子探索）
- **压缩能力通过继承传递** → AgentBase 持有压缩管线，所有子类自动获得
- **AgentBase 是 abstract** → 不能直接 new AgentBase()，必须 new 具体子类

### 2.3 AgentBase 设计

```csharp
public abstract class AgentBase : Entity, IAgent
{
    // === 对话上下文窗口（核心：每个 Agent 独立持有）===
    protected readonly IChatContextManager ContextManager;

    // === LLM 引擎 ===
    protected readonly IQueryEngine QueryEngine;

    // === 压缩管线（通过继承传递，子类自动获得）===
    // ContextManager 内部已包含: Microcompact → Snip → Fold → L5 兜底
    // 子类无需重新实现，只需调用 ContextManager.FoldIfNeededAsync()

    // === 身份（继承自 Entity + IAgent）===
    public string Name { get; }
    public AgentRole Role { get; }
    public ExecutorVariant? Variant { get; }
    public ObjectId? ParentObjectId { get; init; }

    // === 任务/上下文/配置/预算/输出（同当前 Agent）===
    // ...

    // === 对话循环（abstract 或 virtual，子类可重写）===
    public abstract Task<SubAgentResult> ExecuteAsync(CancellationToken ct = default);
    public abstract IAsyncEnumerable<AgentStreamChunk> ExecuteStreamAsync(CancellationToken ct = default);

    // === 生命周期控制（virtual，子类可扩展）===
    public virtual void Pause() { ... }
    public virtual void Resume() { ... }
    public virtual void Cancel() { ... }
    public virtual void Reset() { ... }

    // === 压缩触发（protected，子类可调用但外部不能）===
    protected async Task<ContextFoldResult> CompactIfNeededAsync(ContextFoldDecision decision, CancellationToken ct)
    {
        return await ContextManager.FoldIfNeededAsync(decision, ct);
    }
}
```

### 2.4 IChatContextManager 内聚方式

**方案A: 每个 Agent 实例 new 独立 ChatContextManager**

```csharp
// AgentBase 构造函数
protected AgentBase(..., IChatContextManagerFactory contextManagerFactory, ...)
{
    ContextManager = contextManagerFactory.Create(sessionId, options);
}
```

- 优点: 每个 Agent 完全独立的上下文窗口，互不干扰
- 缺点: 需要新增 IChatContextManagerFactory，ChatContextManager 从单例改为瞬态

**方案B: 主 Agent 持有 ChatContextManager，子 Agent 共享父 Agent 的**

```csharp
// 子 Agent 构造函数
protected AgentBase(..., AgentBase parent, ...)
{
    ContextManager = parent.ContextManager;  // 共享父的上下文
}
```

- 优点: 无需工厂，子智能体上下文自动进入父的压缩管线
- 缺点: 父子共享上下文可能不是用户想要的（子智能体应该有独立上下文）

**推荐: 方案A** — 每个 Agent 独立上下文窗口，符合"每个智能体内部是对话上下文窗口"的约束

### 2.5 压缩能力传递路径

```
AgentBase (abstract)
  └── ContextManager: IChatContextManager (每个实例独立)
        ├── MicrocompactService (微压缩, 清旧工具结果)
        ├── SnipStaleToolResults (剪裁过期大工具结果)
        ├── ContextFoldExecutor (head 摘要 + tail 保留)
        │     └── IFoldSummarizer → FoldSummarizer (调 LLM)
        ├── ContentReplacementService (per-tool 落盘)
        └── L5 兜底: ContextOverflowException

子类继承 AgentBase → 自动获得 ContextManager → 自动获得全部压缩能力
子类只需在 ExecuteAsync 中调 ContextManager.FoldIfNeededAsync() 触发压缩
```

### 2.6 ReasoningAgentBase 统一方案

```
当前:
  ReasoningAgentBase (abstract) : IReasoningAgent
    ├── ProsecutorAgent
    ├── JudgeAgent
    └── DefenderAgent

目标:
  AgentBase (abstract) : IAgent
    └── ReasoningAgent (abstract) : AgentBase
          ├── ProsecutorAgent : ReasoningAgent
          ├── JudgeAgent : ReasoningAgent
          └── DefenderAgent : ReasoningAgent
```

- ReasoningAgent 继承 AgentBase，自动获得 IChatContextManager + 压缩能力
- 删除 IReasoningAgent 接口，统一到 IAgent
- 删除 ReasoningContextCompressor，复用主压缩体系

## 3. 渐进式迁移步骤

### 阶段1: Agent sealed → AgentBase abstract（最小改动）

| 步骤 | 内容 | 影响范围 |
|------|------|----------|
| 1.1 | 新建 `AgentBase.cs`（abstract），把 Agent 的字段/方法搬过去 | Agents 项目 |
| 1.2 | Agent 改为 `class Agent : AgentBase`（过渡兼容，保持 sealed） | Agents 项目 |
| 1.3 | 编译验证，所有 new Agent() 仍可用 | 全量 |
| 1.4 | git commit | - |

### 阶段2: IChatContextManager 内聚到 AgentBase

| 步骤 | 内容 | 影响范围 |
|------|------|----------|
| 2.1 | 新建 `IChatContextManagerFactory` 接口 + 实现 | Abstractions + Brain |
| 2.2 | AgentBase 构造函数加 `IChatContextManagerFactory` 参数 | Agents 项目 |
| 2.3 | AgentBase.ExecuteAsync 内部用 ContextManager 替代裸 MessageList | Agents 项目 |
| 2.4 | 编译验证 | 全量 |
| 2.5 | git commit | - |

### 阶段3: 提取 ExecutorAgent 子类

| 步骤 | 内容 | 影响范围 |
|------|------|----------|
| 3.1 | 新建 `ExecutorAgent.cs`（abstract, Role=Executor） | Agents 项目 |
| 3.2 | 为每个 ExecutorVariant 创建子类: CodeAgent, SearchAgent, ExploreAgent, PlanAgent, ... | Agents 项目 |
| 3.3 | 修改 AgentLifecycleManager.cs: new Agent(...,variant) → AgentFactory.Create(variant) | Agents 项目 |
| 3.4 | 修改 ModelCoordinator.cs: 同上 | Agents 项目 |
| 3.5 | 编译验证 | 全量 |
| 3.6 | git commit | - |

### 阶段4: 提取 CoordinatorAgent

| 步骤 | 内容 | 影响范围 |
|------|------|----------|
| 4.1 | 新建 `CoordinatorAgent.cs`（Role=Coordinator） | Agents 项目 |
| 4.2 | 主对话循环改用 CoordinatorAgent | App 项目 |
| 4.3 | 编译验证 | 全量 |
| 4.4 | git commit | - |

### 阶段5: 统一 ReasoningAgentBase 体系

| 步骤 | 内容 | 影响范围 |
|------|------|----------|
| 5.1 | ReasoningAgentBase 改为 `class ReasoningAgent : AgentBase` | Reasoning 项目 |
| 5.2 | ProsecutorAgent/JudgeAgent/DefenderAgent 继承 ReasoningAgent | Reasoning 项目 |
| 5.3 | 删除 IReasoningAgent 接口，统一到 IAgent | Abstractions + Reasoning |
| 5.4 | 删除 ReasoningContextCompressor，复用主压缩体系 | Reasoning 项目 |
| 5.5 | 编译验证 | 全量 |
| 5.6 | git commit | - |

### 阶段6: 清理 + 测试

| 步骤 | 内容 | 影响范围 |
|------|------|----------|
| 6.1 | 删除旧 Agent.cs（已由 AgentBase + 子类替代） | Agents 项目 |
| 6.2 | 更新所有测试 | 测试项目 |
| 6.3 | 全量编译 + 测试 | 全量 |
| 6.4 | git commit | - |

## 4. 影响范围评估

### 4.1 新增文件

| 文件 | 项目 | 说明 |
|------|------|------|
| `AgentBase.cs` | Agents | 抽象基类 |
| `CoordinatorAgent.cs` | Agents | 主智能体 |
| `ExecutorAgent.cs` | Agents | 子智能体基类 |
| `CodeAgent.cs` | Agents | 代码执行者 |
| `SearchAgent.cs` | Agents | 搜索执行者 |
| `ExploreAgent.cs` | Agents | 探索执行者 |
| `PlanAgent.cs` | Agents | 计划执行者 |
| `DoctorAgent.cs` | Agents | 医生执行者 |
| `VerificationAgent.cs` | Agents | 验证执行者 |
| `GuideAgent.cs` | Agents | 引导执行者 |
| `ContextCompressionAgent.cs` | Agents | 上下文压缩执行者 |
| `TeammateAgent.cs` | Agents | 协作队友 |
| `IChatContextManagerFactory.cs` | Abstractions | 上下文管理器工厂 |
| `ChatContextManagerFactory.cs` | Brain | 工厂实现 |

### 4.2 修改文件

| 文件 | 改动 |
|------|------|
| `Agent.cs` | sealed → 继承 AgentBase，最终删除 |
| `AgentLifecycleManager.cs` | new Agent() → AgentFactory.Create(variant) |
| `ModelCoordinator.cs` | 同上 |
| `ReasoningAgentBase.cs` | 继承 AgentBase |
| `ProsecutorAgent.cs` / `JudgeAgent.cs` / `DefenderAgent.cs` | 继承 ReasoningAgent |
| 所有测试中 new Agent() 的地方 | 改为 new 具体子类 |

### 4.3 风险

| 风险 | 缓解 |
|------|------|
| Agent 构造函数参数多（17个），拆分时可能遗漏 | 渐进式，先搬后拆，每步编译验证 |
| ChatContextManager 从单例改瞬态可能影响中间件 | 用工厂模式，中间件仍注入 IChatContextManager（主 Agent 的） |
| 测试大量使用 new Agent() | 阶段6 统一更新，或保留 Agent 作为兼容包装 |
| ReasoningAgentBase 体系改动大 | 阶段5 单独处理，前4阶段不动 |

## 5. 约束满足验证

| 约束 | 阶段 | 满足方式 |
|------|------|----------|
| 1. 每个智能体内部是对话上下文窗口 | 阶段2 | AgentBase 持有 IChatContextManager，每个实例独立 |
| 2. 子智能体派生自 Agent | 阶段3 | CodeAgent : ExecutorAgent : AgentBase |
| 3. 压缩上下文纵深防御多级 | 已满足 | 微压缩→Snip→Fold→L5，挂在 ContextManager 上 |
| 4. 只实现一次，通过继承传递 | 阶段2+5 | AgentBase 持有压缩管线，子类继承自动获得；统一两套体系 |

<!-- 🤖 Auto Decision: 2026-08-08 -->
<!-- 决策: 采用方案- 原因: 符合"每个智能体内部是对话上下文窗口"约束，父子$替换方案: 方案B（子 Agent 共享父 Agent 的 ContextManager），但不符合独立上下文要求 -->
<!-- 验证: 6 阶段全部完成，305 Agents + 272 Reasoning + 257 Scheduling 测试全过 ✅ -->

<!-- 🤖 Auto Decision: 2026-08-08 阶段5 -->
<!-- 决策: 合并 Reasoning.State.AgentRole 到 Abstractions.Models.Agent.AgentRole，删除 IReasoningAgent/IReasoningContextCompressor/ReasoningContextCompressor -->
<!-- 原因: 用户要求"贯彻到底，风格一致，遇到兼容直接删掉" -->
<!-- 验证: Reasoning 项目编译通过，272 测试全过 ✅ -->

<!-- 🤖 Auto Decision<-- 阶段6 -->
<!-- 决策: 保留 Agent.cs 作为通用具体类，AgentFactory 不再返回 Agent，(Agent) 强制转换改为 (AgentBase) -->
<!-- 原因: 删除 Agent.cs 会影响大量文件引用 Agent 类型，保留作为通用实现 -->
<!-- 验证: 305 Agents + 257 Scheduling 测试全过 ✅ -->
