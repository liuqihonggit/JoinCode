# 0040. 企业级状态机框架 — 转换表 + 守卫 + 共享上下文

- 状态：accepted
- 日期：2026-08-29
- 决策者：用户确认
- 增强：[0038](docs/adr/0038-state-machine-flags-guard.md)、[0039](docs/adr/0039-command-interception-state-machine.md)
- 候选清单：[0040-fsm-candidates.md](0040-fsm-candidates.md)
- 后继：[0041](docs/adr/0041-fsm-source-generator.md)（源码生成器 + 特性 + 事件订阅）

## 背景

ADR 0038/0039 引入了 [Flags] 位标志优化，但仅是属性检测层面的改进。用户提供了完整的企业级状态机设计，核心行为流程：

```
获取当前状态 → 查表得到对应的事件函数指针 → 守卫判定条件是否满足 → 满足执行事件 → 转移到下一个状态
```

**此行为流程必须一致地应用到所有改造候选**（不是统一状态枚举，而是统一行为模式）。共享上下文放全局数据，无限循环可包括空闲状态在中间等候。枚举是最佳方式（编译时检查 + IDE 智能提示，比字符串强 100 倍）。

## 决策

**采用转换表 + 守卫 + 共享上下文 + 枚举编译时检查的企业级状态机框架。**

### 核心组件

1. **状态枚举（TState）+ 事件枚举（TEvent）**：编译时检查 + IDE 智能提示
   ```csharp
   public enum CircuitPhase { Closed, Open, HalfOpen, Faulted }
   public enum CircuitEvent { RecordSuccess, RecordFailure, TryProbe, OpenTimeout, Reset }
   ```
   - **熔断作为状态枚举的一个状态值**（如 `Faulted`），转换表直接定义它的转换规则（如 `Faulted → Reset`），不需要额外标记或上下文字段

2. **转换键（TransitionKey）**：**禁止用元组**，用 `readonly record struct` 显式命名
   ```csharp
   public readonly record struct TransitionKey<TState, TEvent>(TState From, TEvent Event)
       where TState : struct, Enum
       where TEvent : struct, Enum;
   ```

3. **转换规则（TransitionRule）**：目标状态 + 守卫 + 动作
   ```csharp
   public sealed record TransitionRule<TState>(
       TState Target,
       TransitionGuard? Guard = null,
       TransitionAction? Action = null);
   ```

4. **转换表（FrozenDictionary<TransitionKey, TransitionRule>）**：查表得转换规则
   ```csharp
   private readonly FrozenDictionary<TransitionKey<TState, TEvent>, TransitionRule<TState>> _table;
   ```

5. **守卫（TransitionGuard delegate）**：条件检查，返回 true 才允许转换
   ```csharp
   public delegate bool TransitionGuard(FsmContext? ctx);
   ```

6. **动作（TransitionAction delegate）**：转换时执行的副作用
   ```csharp
   public delegate void TransitionAction(FsmContext? ctx);
   ```

7. **共享上下文（FsmContext）**：所有状态共享的数据，可选（简单场景不用）
   ```csharp
   public sealed class FsmContext
   {
       public Dictionary<string, object> CustomData { get; } = new();
   }
   ```
   - 熔断标记**不放在上下文**，作为状态枚举的 `Faulted` 状态值

8. **状态接口（IFsmState）**：OnEnter/OnUpdate/OnExit/OnEvent，可选（需要副作用的场景用）
   ```csharp
   public interface IFsmState<TState, TEvent>
       where TState : struct, Enum
       where TEvent : struct, Enum
   {
       TState StateId { get; }
       void OnEnter(FsmContext? context);
       void OnUpdate(FsmContext? context, float deltaTime);
       void OnExit(FsmContext? context);
       void OnEvent(FsmContext? context, TEvent evt);
   }
   ```

9. **状态机核心（StateMachine&lt;TState, TEvent&gt;）**：
   - `Trigger(evt, ctx)`：查表 → 守卫检查 → 执行 Action → SwitchTo
   - `Update(deltaTime)`：每帧调用当前状态的 OnUpdate（可选）
   - `SwitchTo(stateId)`：OnExit 旧 → OnEnter 新（可选）

   ```csharp
   public sealed class StateMachine<TState, TEvent>
       where TState : struct, Enum
       where TEvent : struct, Enum
   {
       private readonly FrozenDictionary<TransitionKey<TState, TEvent>, TransitionRule<TState>> _table;
       private TState _currentState;

       public TransitionResult Trigger(TEvent evt, FsmContext? ctx = null)
       {
           var key = new TransitionKey<TState, TEvent>(_currentState, evt);
           if (!_table.TryGetValue(key, out var rule))
               return TransitionResult.Rejected(_currentState, evt);

           if (rule.Guard is not null && !rule.Guard(ctx))
               return TransitionResult.GuardFailed(_currentState, evt);

           var oldState = _currentState;
           rule.Action?.Invoke(ctx);
           _currentState = rule.Target;
           return TransitionResult.Transitioned(oldState, rule.Target, evt);
       }
   }
   ```

### 分层组合（不是上帝类）

```
┌─────────────────────────────────────────────────┐
│  第4层: FsmManager<T>     多实例管理（可选）        │
├─────────────────────────────────────────────────┤
│  第3层: IFsmState 钩子    OnEnter/OnExit/OnUpdate（可选）│
├─────────────────────────────────────────────────┤
│  第2层: FsmContext        共享数据容器（可选）        │
├─────────────────────────────────────────────────┤
│  第1层: StateMachine<TState,TEvent>  核心：转换表+守卫+事件   │
│         ↑ 所有场景都用这一层，其余按需组合                    │
└─────────────────────────────────────────────────┘
```

核心层只管"状态+转换+守卫+事件"，**不管驱动方式**（事件/数据/轮询由调用方决定）。所有场景共用同一个核心，差异在外层组合。

### AOT 兼容适配

用户原始设计用 `Assembly.GetTypes()` 反射扫描状态，与 NativeAOT 不兼容（ADR 0002）。**改为 DI 注册**：

```csharp
// ❌ 禁止：反射扫描（AOT 不兼容）
public StateMachine RegisterAllStates(Assembly assembly)
    => assembly.GetTypes().Where(t => typeof(IFsmState).IsAssignableFrom(t))...

// ✅ 正确：DI 注入或手动注册
public StateMachine RegisterState(IFsmState state) { _states[state.StateId] = state; return this; }
// 或用 [Register(typeof(IFsmState))] 特性 + 源码生成器自动注册
```

### 禁止用元组作为转换表 key

```csharp
// ❌ 禁止：元组 key（可读性差，无编译时命名检查）
private readonly FrozenDictionary<(TState, TEvent), TransitionRule<TState>> _table;

// ✅ 正确：TransitionKey record struct（显式命名，IDE 智能提示）
private readonly FrozenDictionary<TransitionKey<TState, TEvent>, TransitionRule<TState>> _table;
```

### 应用场景

1. **循环检测器**（取代 ShannonEntropyDetector 当前 switch 实现）：
   - 状态：Monitoring, Suspected, Confirmed, Faulted
   - 事件：Decline, Timeout, Confirm, Recover, Reset
   - 转换：Monitoring+Decline→Suspected, Suspected+Confirm→Confirmed, Suspected+Timeout→Monitoring, Confirmed+Recover→Monitoring
   - 守卫：IsDeclining, InConfirmWindow, IsRecovering
   - 上下文：entropy, declineStreak, firstTriggerTime, triggerCount

2. **熔断器**（取代 UnifiedCircuitBreaker 当前 switch 实现）：
   - 状态：Closed, Open, HalfOpen, Faulted
   - 事件：RecordSuccess, RecordFailure, TryProbe, OpenTimeout, Reset
   - 转换：Closed+RecordFailure→Open(守卫: failures>=threshold), Open+OpenTimeout→HalfOpen, HalfOpen+RecordSuccess→Closed, HalfOpen+RecordFailure→Open
   - 守卫：FailuresExceedThreshold, ElapsedExceedsOpenDuration, ProbeCountUnderMax
   - 上下文：consecutiveFailures, openedAt, halfOpenProbeCount

3. **命令拦截**（取代 CommandInterceptionDispatcher 当前 Guard 链）：
   - 状态：Evaluating, Rewriting, Denied, Redirected, Allowed, Faulted
   - 事件：Match, Rewrite, Deny, Redirect, Allow
   - 转换表 + 守卫

## 替代方案

1. **仅 [Flags] 优化（ADR 0038/0039）**：不够。[Flags] 只是属性检测优化，缺少转换表、守卫、共享上下文的完整框架。
2. **用第三方状态机库（Stateless 等）**：放弃。AOT 兼容性未知（ADR 0002），且不自控。
3. **保留反射扫描**：放弃。NativeAOT 不兼容（ADR 0002）。
4. **元组作为转换表 key**：放弃。可读性差，无编译时命名检查，用户明确反对。

## 后果

- 正面：转换表显式可读；守卫灵活；共享上下文统一数据；枚举编译时检查；高可扩展性（加状态/事件只需加枚举值 + 转换规则）；行为流程一致应用到所有候选
- 负面：框架初期实现成本高；现有循环检测器和命令拦截需重构
- 中性：DI 注册替代反射，AOT 兼容；状态机框架位于 `foundation/Abstractions`；熔断作为状态枚举值，不额外加标记

<!-- 🤖 Auto Decision: 2026-08-29 -->
<!-- 决策: 熔断作为状态枚举的 Faulted 状态值，不放在 FsmContext；转换表 key 用 TransitionKey record struct，禁止元组 -->
<!-- 原因: 用户明确反对元组；熔断作为状态值更简洁，转换表已能表达一切，无需额外标记 -->
<!-- 替代方案: 熔断放 FsmContext.IsFaulted — 放弃，语义混淆；元组 key — 放弃，用户反对 -->
<!-- 验证: 文档更新，未改代码，无需编译 ✅ -->
