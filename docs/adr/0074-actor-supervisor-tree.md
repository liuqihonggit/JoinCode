# 0074. Actor 监督树 — Router/Gateway/Supervisor/PersistentMailbox 四层扩展

- 状态：accepted
- 日期：2026-09-08
- 决策者：项目架构组
- 关联 ADR：[0052](0052-asynclock-unified-mutex-file-access.md)、[0060](0060-asynclock-sync-trylock-fireandforget-deadlock.md)

## 背景

现有 `ActorBase<TCommand>`（foundation/AsyncLock/src/ActorBase.cs）是生产级单消费者 Actor 实现，已有 `StreamingToolExecutorActor`、`McpRequestRegistryActor` 两个落地用例。但缺少四个架构编排能力：

1. **Router Actor** — 多 Worker 负载分发（BuildQueueService 当前单消费者串行编译）
2. **LLM Gateway Actor** — 统一限流/重试/熔断（现有散落在各处）
3. **监督策略** — 父子 Actor 重启（现有 OnConsumerError 只记录日志）
4. **持久化邮箱** — 崩溃恢复（现有 Channel 是内存队列）

参考 actor.txt 的 Coding Agent 架构编排思想（非基类实现，现有 ActorBase 已全面更优）。

## 决策

### 决策1：两层基类 — ActorBase 保持扁平，新增 SupervisedActor 提供树形

**选择**：
```
ActorBase<TCommand>              (现有，不变)
  └─ SupervisedActor<TCommand>   (新增，继承 ActorBase + 树形监督)
```

**理由**：
- 现有 `StreamingToolExecutorActor`、`McpRequestRegistryActor` 不需要监督，保持 ActorBase 不破坏它们
- 需要监督的 Actor（Router/Gateway）继承 SupervisedActor 获得树形能力
- 符合开心路径：简单场景用 ActorBase，复杂场景用 SupervisedActor
- 避免强制树形破坏现有代码（渐进式）

**替代方案（放弃）**：
- 强制所有 Actor 继承 SupervisedActor — 破坏现有代码，过度设计
- ActorBase 继承 ServiceEntity — Actor 不需要 ObjectId/SessionId，语义不符

### 决策2：监督策略用策略模式 + 枚举，不用继承

**选择**：
```csharp
public enum SupervisorDirective { Resume, Restart, Escalate, Stop }
public sealed record SupervisorStrategy(int MaxRestarts, TimeSpan Within, Func<Exception, SupervisorDirective> Decider);
```

**理由**：
- 策略对象可注入、可替换，符合"约定大于配置"
- 枚举 + switch 表达式实现状态机（对齐 ADR 0018 循环检测器风格）
- 不用继承避免策略类爆炸

**预定义策略**：
- `SupervisorStrategy.OneForOne` — 只重启失败的子 Actor
- `SupervisorStrategy.AllForOne` — 重启所有子 Actor
- `SupervisorStrategy.Escalate` — 向上抛给父 Actor

### 决策3：RouterActor 用路由策略接口，不硬编码轮询

**选择**：
```csharp
public interface IRouterStrategy<TMessage>
{
    ActorRef<TMessage> Select(IReadOnlyList<ActorRef<TMessage>> workers, TMessage message);
}
```

**预定义策略**：`RoundRobinStrategy`、`LeastLoadStrategy`、`ConsistentHashStrategy`

**理由**：可替换、可扩展，符合"替换便利"原则

### 决策4：LLM Gateway Actor 复用现有 ResiliencePolicy，不重新实现熔断

**选择**：LlmGatewayActor 内部组合 `ResiliencePolicy` + `ProcessRestartManager` 已有能力

**理由**：不重复造轮子（八荣八耻：以复用现有为荣）

### 决策5：持久化邮箱用装饰器模式，不侵入 ActorBase

**选择**：
```csharp
public sealed class PersistentMailbox<TCommand> : IActorMailbox<TCommand>
{
    private readonly IActorMailbox<TCommand> _inner;  // 装饰内层 Channel
    private readonly IPersistentStore _store;
}
```

**理由**：持久化是横切关注点，用装饰器而非继承，可任意组合

### 决策6：背压设计 — 有界大容量 + 水位线告警 + 发送超时 + 可选优先级

**选择**：
- `ActorBackpressure` 配置记录：Capacity + FullMode + HighWatermark + CriticalWatermark + SendTimeout
- 预定义：`CodingAgentTask`(2000)、`LlmGateway`(200)、`Router`(1000)、`Build`(100)
- `WatermarkReached` 事件：生产者订阅降速
- `SendAsync` 超时重载：超时抛 TimeoutException，不无限阻塞
- `PriorityMailbox`（可选）：多通道优先级队列，用户交互 > LLM > 后台编译

**理由**：
- Coding Agent 场景 LLM/编译慢，消息会堆积，无界队列 OOM 风险
- 容量大但不无限：2000 任务缓冲足够，超出说明系统过载
- 水位线告警让生产者主动降速，而非被动等到通道满
- 发送超时防止生产者永久阻塞（UI 卡死）
- 优先级保证用户交互响应性

**容量选择依据**：
- 任务队列 2000：用户可批量提交大量编码任务，不丢
- LLM 200：LLM 慢(10-30s/次)，200 缓冲约 30-60 分钟任务量
- 编译 100：编译串行，100 足够多仓库并发
- Router 1000：分发快，1000 足够 Worker 调度

### 决策7：树形生长范围 — 仅 SupervisedActor 树形，ActorBase 保持扁平

**需要树形**：
- RouterActor → WorkerActor（Router 监督 Worker）
- LlmGatewayActor → ProviderActor（Gateway 监督 Provider）
- 通用 SupervisedActor → 子 SupervisedActor

**不需要树形**：
- PersistentMailbox（装饰器，非父子）
- 现有 StreamingToolExecutorActor、McpRequestRegistryActor（独立 Actor）

**理由**：AGENTS.md "任何资源类都必须树状生长" — Actor 是资源类，但树形能力通过 SupervisedActor 可选获得，不强制所有 Actor 树形

## 架构图

```
                    ┌──────────────────┐
                    │  ActorBase<TCmd> │ (现有，不变)
                    │  Channel mailbox │
                    │  Consumer 循环   │
                    └────────┬─────────┘
                             │ inherits
                    ┌────────▼─────────┐
                    │ SupervisedActor  │ (新增)
                    │ Parent/Children  │
                    │ SupervisorStrategy│
                    │ Restart/Resume   │
                    └────────┬─────────┘
                             │ inherits
          ┌──────────────────┼──────────────────┐
          │                  │                  │
  ┌───────▼───────┐  ┌──────▼───────┐  ┌──────▼───────────┐
  │ RouterActor   │  │LlmGatewayActor│  │PersistentMailbox │
  │ +Workers[]    │  │ +Providers[]  │  │ (装饰器,非继承)  │
  │ +RouteStrategy│  │ +Resilience   │  │ +IPersistentStore│
  └───────────────┘  └───────────────┘  └──────────────────┘
```

## 好处

| 方面 | 说明 |
|------|------|
| 容错性 | 子 Actor 崩溃自动重启，不影响兄弟（OneForOne） |
| 可观测性 | 树形结构递归上报指标，Parent 汇总 Children 状态 |
| 生命周期 | 父停止级联停止所有子，无需手动管理 |
| 资源隔离 | 每个 Worker 独立状态，崩溃不蔓延 |
| 限流统一 | LLM Gateway 统一管理 API 并发，避免超限 |
| 崩溃恢复 | PersistentMailbox 重放未处理消息 |
| 向后兼容 | ActorBase 不变，现有 Actor 零改动 |
| 背压保护 | 有界大容量 + 水位线告警 + 发送超时，防 OOM 和永久阻塞 |
| 优先级保证 | PriorityMailbox 用户交互优先于后台任务 |

## 坏处

| 方面 | 应对 |
|------|------|
| 复杂度增加 | 监督策略实现复杂 → 先做 OneForOne，AllForOne 后续 |
| 性能开销 | 每层 Actor 有 Channel 开销 → 仅热点路径用 SupervisedActor |
| 调试难度 | 树形调试复杂 → 每个 Actor 带 Id + ParentId 结构化日志 |
| 状态恢复 | Actor 重启后状态丢失 → 配合 PersistentMailbox |
| 过度设计风险 | 简单场景不需要监督 → ActorBase 仍可用，不强制树形 |
| 背压调参复杂 | 容量/水位/超时需调参 → 预定义 CodingAgent/LlmGateway/Router/Build 四档 |

## 实现计划

1. `SupervisedActor<TCommand>` — 树形基类 + 监督策略（foundation/AsyncLock）
2. `RouterActor<TMessage>` + `IRouterStrategy` — 多 Worker 分发（core/execution/Scheduling）
3. `LlmGatewayActor` — 限流/重试/熔断（core/ai/Llm）
4. `PersistentMailbox<TCommand>` — 持久化装饰器（foundation/AsyncLock）
5. 单元测试 + E2E 测试
6. 现有 BuildQueueService 增强：可选 RouterActor 多 Worker 模式

## 验证

- [ ] SupervisedActor 单元测试：父子关系、监督策略、重启计数
- [ ] RouterActor 单元测试：路由策略、Worker 崩溃恢复
- [ ] LlmGatewayActor 单元测试：限流、重试、熔断
- [ ] PersistentMailbox 单元测试：持久化、崩溃恢复
- [ ] 编译通过（Debug + Release）
- [ ] AOT 兼容（无 dynamic、无反射 emit）
