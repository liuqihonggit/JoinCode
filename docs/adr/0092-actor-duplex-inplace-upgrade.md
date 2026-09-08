# 0092. Actor 全双工改造 — 直接改 ActorBase（无后向兼容）

- 状态：accepted
- 日期：2026-09-08
- 决策者：用户
- 关联 ADR：[0074](0074-actor-supervisor-tree.md)、[0052](0052-asynclock-unified-mutex-file-access.md)

## 背景

原计划新建 `DuplexActorBase<TIn, TOut>` 保持 `ActorBase` 不变。用户决策：项目不需要后向兼容，直接改 `ActorBase` 为双工，大修大改。

## 决策

### 决策1：直接改 `ActorBase<TCommand>` → `ActorBase<TCommand, TOut>`

**选择**：
```csharp
// 旧：单工
public abstract class ActorBase<TCommand> : IAsyncDisposable

// 新：双工
public abstract class ActorBase<TCommand, TOut> : IAsyncDisposable
```

**理由**：
- 项目无后向兼容需求（AGENTS.md 基础规范1）
- 避免两套基类（ActorBase + DuplexActorBase）的维护负担
- 所有 Actor 统一双工能力，消除"是否需要双工"的决策负担
- `Unit` 类型用于不需要输出的 Actor（如 PersistencePipeline）

**放弃的替代方案：新建 `DuplexActorBase<TIn, TOut>`**
- 保持 `ActorBase<TCommand>` 不变，新增 `DuplexActorBase` 继承
- 双 Channel 设计（输入有界 + 输出无界）、`IAsyncEnumerable<TOut>` 输出流、输入有背压输出无背压
- 放弃原因：两套基类维护负担，项目无后向兼容需求

### 决策2：双 Channel + IAsyncEnumerable 输出

双 Channel（输入有界 + 输出无界）、`IAsyncEnumerable<TOut>` 输出流、输入有背压输出无背压、`IAgentOutputChannelManager` 集成。

### 决策3：OutputCount 用 Interlocked 计数器

**选择**：
```csharp
private int _outputCount;

protected bool TryPublish(TOut msg)
{
    if (_outputChannel.Writer.TryWrite(msg))
    {
        Interlocked.Increment(ref _outputCount);
        return true;
    }
    return false;
}

public async IAsyncEnumerable<TOut> OutputAsync(...)
{
    await foreach (var item in _outputChannel.Reader.ReadAllAsync(ct))
    {
        Interlocked.Decrement(ref _outputCount);
        yield return item;
    }
}
```

**理由**：
- .NET 10 的 `PartitionedUnboundedChannel.Reader.Count` 抛 `NotSupportedException`
- 用 `Interlocked` 计数器避免依赖 `Reader.Count`
- `OutputAsync` 改为自定义迭代器以 hook 递减

## 改造范围

| Actor | 改造 | 状态 |
|-------|------|------|
| `ActorBase<TCommand, TOut>` | 核心双工改造 | ✅ 完成 |
| `StreamingToolExecutorActor` | `ActorBase<IToolCommand, StreamingToolResult>` | ✅ 完成 |
| `McpRequestRegistryActor` | 双工 + 注释恢复 | ✅ 完成 |
| `PersistencePipeline` | `ActorBase<PersistRequest, Unit>` | ✅ 完成 |
| `GatewayActor` | 双工适配 | ✅ 完成 |
| `RouterActor` | 双工适配 | ✅ 完成 |
| `SupervisedActor` | 双工适配 | ✅ 完成 |
| `PersistentMailbox` | 双工适配 | ✅ 完成 |
| `PriorityMailbox` | 双工适配 | ✅ 完成 |
| `BuildQueueRouter` | 双工适配 | ✅ 完成 |

## 验证

- [x] 全解决方案编译通过（0 警告 0 错误）
- [x] AsyncLock 106 个测试通过
- [x] Brain.Context 785 个测试通过
- [x] Infra.IO 148 个测试通过
- [x] Mcp 209 个测试通过
- [x] AOT 兼容（无 dynamic、无反射 emit）

## 参考

- `0091-actor-duplex-upgrade.md` — 原方案（已归档到 .xxx/）
- `foundation/AsyncLock/src/ActorBase.cs` — 改造后实现
- `foundation/AsyncLock/src/Unit.cs` — Unit 类型（无输出 Actor 用）
