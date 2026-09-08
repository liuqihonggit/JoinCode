# 0075. Actor 全双工改造 — DuplexActorBase 基类设计

- 状态：superseded by 0076
- 日期：2026-09-08
- 决策者：项目架构组
- 关联 ADR：[0074](0074-actor-supervisor-tree.md)、[0052](0052-asynclock-unified-mutex-file-access.md)、[0060](0060-asynclock-sync-trylock-fireandforget-deadlock.md)
- 取代原因：用户决策直接改 ActorBase（无后向兼容需求），不新建 DuplexActorBase。详见 [0076](0076-actor-duplex-inplace-upgrade.md)

## 背景

现有 `ActorBase<TCommand>`（foundation/AsyncLock/src/ActorBase.cs）是生产级单消费者 Actor 实现，采用单工模式：

- 单消费者 Channel（`SingleReader = true`）
- 多生产者（`SingleWriter = false`）
- 串行处理，无锁
- 响应通过 `TaskCompletionSource` 或回调返回

**问题**：
1. **Actor 无法主动推送消息** — 必须等待外部请求
2. **不支持流式输出** — 与 `IAgentOutputChannelManager` 集成需要额外代码
3. **双向通信需手动实现** — 如 `StreamingToolExecutorActor` 用 `ToolCompletedCommand` 回传

**现有用例**：
| Actor | 文件 | 当前模式 |
|-------|------|----------|
| `StreamingToolExecutorActor` | core/execution/Brain/src/Context/Services/Chat/StreamingToolExecutorActor.cs | 单工 + 命令回传 |
| `McpRequestRegistryActor` | services/Mcp/src/Client/McpRequestRegistryActor.cs | 单工 + TCS |
| `PersistencePipeline` | infrastructure/Infrastructure/IO/PersistencePipeline.cs | 单工 + TCS |

## 决策

### 决策1：新增 `DuplexActorBase<TIn, TOut>` 基类，保持 `ActorBase` 不变

**选择**：
```
ActorBase<TCommand>              (现有，不变，单工)
  └─ DuplexActorBase<TIn, TOut>  (新增，双工)
       └─ SupervisedActor<TCommand> (现有，继承 ActorBase)
```

**理由**：
- 现有 Actor 零改动，向后兼容
- 需要双工的 Actor 继承 `DuplexActorBase`
- 符合开心路径：简单场景用 `ActorBase`，流式场景用 `DuplexActorBase`
- 避免强制改造破坏现有代码（渐进式）

**替代方案（放弃）**：
- 改造 `ActorBase` 增加输出Channel — 破坏现有代码，过度设计
- 强制所有 Actor 双工 — 简单场景不需要，浪费资源

### 决策2：双Channel设计 — 输入Channel + 输出Channel

**选择**：
```csharp
public abstract class DuplexActorBase<TIn, TOut> : IAsyncDisposable
{
    private readonly Channel<TIn> _inputChannel;   // 外部 → Actor
    private readonly Channel<TOut> _outputChannel;  // Actor → 外部
    
    // 输入：外部发送命令
    protected internal ValueTask SendAsync(TIn cmd, CancellationToken ct = default);
    
    // 输出：Actor 主动推送
    protected bool TryPublish(TOut msg);
    
    // 外部拉取输出
    public IAsyncEnumerable<TOut> OutputAsync(CancellationToken ct);
}
```

**理由**：
- 输入Channel 复用 `ActorBase` 的串行处理逻辑
- 输出Channel 用 `UnboundedChannel`（Actor 主动推送，无背压）
- `IAsyncEnumerable<TOut>` 支持流式拉取，AOT 友好

**输出Channel 设计**：
| 特性 | 选择 | 原因 |
|------|------|------|
| 容量 | `Unbounded` | Actor 主动推送，外部可能延迟拉取 |
| 单读单写 | `SingleReader=true, SingleWriter=true` | 仅 Actor Consumer 写入，外部单消费者拉取 |
| 背压 | 不启用 | 输出是事件流，丢弃比阻塞更合理 |

### 决策3：输出流用 `IAsyncEnumerable<T>`，不用事件

**选择**：
```csharp
// 方案A：事件（放弃）
public event EventHandler<TOut>? OutputReceived;

// 方案B：IAsyncEnumerable（采用）
public IAsyncEnumerable<TOut> OutputAsync(CancellationToken ct);
```

**理由**：
- `IAsyncEnumerable` 支持 `await foreach` 流式消费
- 事件需要外部维护订阅，生命周期管理复杂
- 与 `AgentOutputChannelManager` 的 `ReadAllAsync` 模式一致
- AOT 友好，无反射

### 决策4：背压策略 — 输入有背压，输出无背压

**选择**：
```csharp
// 输入：复用 ActorBackpressure，有界 + 水位线 + 超时
protected DuplexActorBase(ActorBackpressure? inputBackpressure = null);

// 输出：无界 Channel，无背压
// 外部拉取慢时，输出Channel 堆积，但不会阻塞 Actor
```

**理由**：
- 输入背压防止 OOM（已有 `ActorBackpressure` 配置）
- 输出是事件流，Actor 不应被外部拉取速度阻塞
- 外部可自行决定丢弃或缓冲

### 决策5：与 `IAgentOutputChannelManager` 集成

**选择**：
```csharp
// Actor 可直接写入全局输出管理器
public sealed class MyActor : DuplexActorBase<MyCommand, MyEvent>
{
    private readonly IAgentOutputChannelManager _outputManager;
    
    protected override async ValueTask HandleAsync(MyCommand cmd, CancellationToken ct)
    {
        var result = await ProcessAsync(cmd, ct);
        TryPublish(new MyEvent(result));  // 输出到本地Channel
        _outputManager.Write(UniqueId, Name, result.Text, AgentOutputChunkType.Text);  // 输出到全局
    }
}
```

**理由**：
- 本地 `TryPublish` 用于 Actor 内部状态变更通知
- 全局 `IAgentOutputChannelManager` 用于前台显示
- 两者可同时使用，职责分离

### 决策6：改造 `StreamingToolExecutorActor` 验证模式

**选择**：
```csharp
// 当前：通过命令回传
private sealed record ToolCompletedCommand(QueuedTool Tool, StreamingToolResult Result, bool IsConcurrencySafe) : IToolCommand;

// 改造后：直接 TryPublish
private void HandleToolCompleted(QueuedTool tool, StreamingToolResult result, bool isConcurrencySafe)
{
    TryPublish(result);  // 主动推送到输出Channel
    _completedBuffer.Add(result);
    _executingCount--;
    // ...
}
```

**理由**：
- 验证 `DuplexActorBase` 实用性
- 消除 `ToolCompletedCommand` 命令，简化代码
- 外部可直接 `OutputAsync()` 拉取结果，无需 TCS

## 架构图

```
                    ┌─────────────────────────────────────┐
                    │       DuplexActorBase<TIn, TOut>    │
                    │  ┌─────────────┐    ┌─────────────┐  │
                    │  │ inputChannel│    │outputChannel│  │
                    │  │  (bounded)  │    │ (unbounded) │  │
                    │  └──────┬──────┘    └──────┬──────┘  │
                    │         │                  │         │
                    │    SendAsync()        TryPublish()  │
                    │         │                  │         │
                    │   ┌─────▼─────┐    ┌─────▼─────┐    │
                    │   │ Consumer  │    │  Output   │    │
                    │   │  循环     │    │  Stream   │    │
                    │   └─────┬─────┘    └─────┬─────┘    │
                    └─────────┼────────────────┼─────────┘
                              │                │
           外部调用方 ────────┘                └─────── 外部拉取方
           SendAsync(cmd)                     OutputAsync()

           Actor 主动推送：TryPublish(event)
```

## 好处

| 方面 | 说明 |
|------|------|
| **流式输出** | Actor 可主动推送，支持实时事件流 |
| **简化代码** | 消除 TCS/命令回传，如 `StreamingToolExecutorActor` |
| **与全局集成** | 直接写入 `IAgentOutputChannelManager`，前台实时显示 |
| **向后兼容** | `ActorBase` 保留，新 Actor 继承 `DuplexActorBase`，渐进式改造 |
| **AOT 友好** | `IAsyncEnumerable` 无反射，NativeAOT 兼容 |
| **测试便利** | 输出流可直接 `await foreach` 验证 |

## 坏处

| 方面 | 应对 |
|------|------|
| 复杂度增加 | 双Channel 需维护两个生命周期 |
| 输出Channel 堆积 | 外部拉取慢时可能 OOM — 监控 `outputChannel.Reader.Count` |
| 调试难度 | 双向流调试复杂 — 结构化日志带 Direction 标记 |
| 过度设计风险 | 简单场景不需要双工 — 保持 `ActorBase` 可用 |
| 背压不对称 | 输入有背压、输出无背压 — 文档明确说明 |

## 实现计划

### 第一步：新建 `DuplexActorBase<TIn, TOut>`

```
foundation/AsyncLock/src/DuplexActorBase.cs
foundation/AsyncLock/tests/Unit/DuplexActorBaseTest.cs
```

### 第二步：改造 `StreamingToolExecutorActor` 验证

```
core/execution/Brain/src/Context/Services/Chat/StreamingToolExecutorActor.cs
```

### 第三步：推广到其他 Actor（全部改造）

| Actor | 改造方式 | 收益 |
|-------|----------|------|
| `McpRequestRegistryActor` | 继承 `DuplexActorBase<IRequestCommand, JsonRpcResponse>` | 消除 TCS 字典，直接 `OutputAsync` 消费响应 |
| `PersistencePipeline` | 继承 `DuplexActorBase<PersistRequest, Unit>` | 输出流可监控持久化进度 |
| `RouterActor` | 继承 `DuplexActorBase<IRouterCommand, RouterEvent>` | 输出流可监控路由事件 |
| `GatewayActor` | 继承 `DuplexActorBase<IGatewayCommand, GatewayEvent>` | 输出流可监控熔断/限流事件 |
| `BuildWorker` | 继承 `DuplexActorBase<ICommand, BuildEvent>` | 输出流可监控编译进度 |
| `PriorityMailbox` | 继承 `DuplexActorBase<PriorityCommand, PriorityEvent>` | 输出流可监控优先级事件 |
| `SupervisedActor` | 继承 `DuplexActorBase<TCommand, SupervisorEvent>` | 输出流可监控子 Actor 生命周期 |
| `PersistentMailbox` | 包装 `DuplexActorBase` | 持久化装饰器模式 |

## 验证

- [ ] `DuplexActorBase` 单元测试：输入发送、输出拉取、并发安全
- [ ] `ActorBase` 保留：所有现有 Actor 编译通过
- [ ] `StreamingToolExecutorActor` 改造后单元测试：流式输出
- [ ] `McpRequestRegistryActor` 改造后单元测试：消除 TCS 字典
- [ ] `PersistencePipeline` 改造后单元测试：输出流监控
- [ ] `RouterActor` 改造后单元测试：路由事件流
- [ ] `GatewayActor` 改造后单元测试：熔断事件流
- [ ] `PriorityMailbox` 改造后单元测试：优先级事件流
- [ ] `SupervisedActor` 改造后单元测试：子 Actor 生命周期事件
- [ ] `BuildWorker` 改造后单元测试：编译进度事件
- [ ] 与 `IAgentOutputChannelManager` 集成测试
- [ ] 编译通过（Debug + Release）
- [ ] AOT 兼容（无 dynamic、无反射 emit）
- [ ] 性能测试：双Channel vs 单Channel 开销

## 参考

- [0074-actor-supervisor-tree.md](0074-actor-supervisor-tree.md) — Actor 监督树设计
- [0052-asynclock-unified-mutex-file-access.md](0052-asynclock-unified-mutex-file-access.md) — AsyncLock 设计
- [0060-asynclock-sync-trylock-fireandforget-deadlock.md](0060-asynclock-sync-trylock-fireandforget-deadlock.md) — 死锁防护
- `foundation/AsyncLock/src/ActorBase.cs` — 现有 ActorBase 实现
- `core/ai/Agents/src/Coordinator/Core/Messaging/AgentOutputChannelManager.cs` — 输出管理器
