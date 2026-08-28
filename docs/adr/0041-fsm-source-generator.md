# 0041. Fsm 源码生成器 + 特性 + 事件订阅

- 状态：accepted
- 日期：2026-08-29
- 决策者：用户确认
- 前驱：[0040](docs/adr/0040-enterprise-fsm-framework.md)（企业级状态机框架）

## 背景

ADR 0040 的 `Fsm<TState,TEvent>` 框架已落地，4 个候选改造完成（DownloadStateMachine、LspServerInstance、MonitorSession、ShannonEntropyDetector、UnifiedCircuitBreaker）。但转换表仍手工编写：

```csharp
private static readonly FrozenDictionary<TransitionKey<LspServerState, LspServerEvent>, TransitionRule<LspServerState>> Transitions =
    new Dictionary<...>
    {
        [new(LspServerState.Stopped, LspServerEvent.Start)] = new(LspServerState.Starting),
        [new(LspServerState.Error, LspServerEvent.Start)] = new(LspServerState.Starting),
        ...
    }.ToFrozenDictionary();
```

**痛点**：
1. 转换表手工编写 — 啰嗦、易错、重复样板代码
2. 守卫/动作与转换表分离 — 守卫方法定义在类里，转换表在静态字段，关联靠人工维护
3. 事件订阅粗粒度 — 当前只有统一 `StateChanged` 事件，外部想订阅特定事件（如"仅当 Start 事件触发时"）需自行 switch

用户提出两个改造方向：
1. 源码生成器 + 特性方式替代手工转换表
2. 为每个事件生成独立 C# event 订阅点

## 决策

### 1. 源码生成器 + 特性声明转换表

用类级 `[Transition]` 特性声明转换骨架，方法级 `[Guard]`/`[Action]` 特性标记守卫/动作方法。生成器扫描特性生成转换表。

**特性定义**（放 `Abstractions/Attributes/`）：

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class StateMachineAttribute : Attribute
{
    public Type StateType { get; }
    public Type EventType { get; }
    public object InitialState { get; }
    public StateMachineAttribute(Type stateType, Type eventType, object initialState) { ... }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class TransitionAttribute : Attribute
{
    public object From { get; }
    public object Event { get; }
    public object To { get; }
    public TransitionAttribute(object from, object evt, object to) { ... }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class GuardAttribute : Attribute
{
    public object From { get; }
    public object Event { get; }
    public GuardAttribute(object from, object evt) { ... }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class ActionAttribute : Attribute
{
    public object From { get; }
    public object Event { get; }
    public ActionAttribute(object from, object evt) { ... }
}
```

**使用方式**：

```csharp
[StateMachine(typeof(LspServerState), typeof(LspServerEvent), LspServerState.Stopped)]
[Transition(LspServerState.Stopped, LspServerEvent.Start, LspServerState.Starting)]
[Transition(LspServerState.Error, LspServerEvent.Start, LspServerState.Starting)]
[Transition(LspServerState.Starting, LspServerEvent.ConnectSucceeded, LspServerState.Running)]
[Transition(LspServerState.Starting, LspServerEvent.ConnectFailed, LspServerState.Error)]
[Transition(LspServerState.Starting, LspServerEvent.BeginStop, LspServerState.Stopping)]
[Transition(LspServerState.Running, LspServerEvent.BeginStop, LspServerState.Stopping)]
[Transition(LspServerState.Error, LspServerEvent.BeginStop, LspServerState.Stopping)]
[Transition(LspServerState.Stopping, LspServerEvent.StopSucceeded, LspServerState.Stopped)]
[Transition(LspServerState.Stopping, LspServerEvent.StopFailed, LspServerState.Error)]
internal sealed partial class LspServerStateMachine
{
    [Guard(LspServerState.Error, LspServerEvent.Start)]
    private static bool CanRestartFromError(FsmContext? ctx) => ctx is LspServerFsmContext { CrashRecoveryCount: var n } && n <= 3;

    [Action(LspServerState.Starting, LspServerEvent.ConnectSucceeded)]
    private static void OnConnected(FsmContext? ctx) => /* 记录时间戳等 */;
}
```

### 2. 生成器为每个事件生成独立 C# event

生成器扫描 `TEvent` 枚举的所有值，为每个值生成独立 `event` 订阅点：

```csharp
// 生成器生成（partial class 补充）
internal sealed partial class LspServerStateMachine
{
    public event EventHandler<TransitionResult<LspServerState, LspServerEvent>>? OnStart;
    public event EventHandler<TransitionResult<LspServerState, LspServerEvent>>? OnConnectSucceeded;
    public event EventHandler<TransitionResult<LspServerState, LspServerEvent>>? OnConnectFailed;
    public event EventHandler<TransitionResult<LspServerState, LspServerEvent>>? OnBeginStop;
    public event EventHandler<TransitionResult<LspServerState, LspServerEvent>>? OnStopSucceeded;
    public event EventHandler<TransitionResult<LspServerState, LspServerEvent>>? OnStopFailed;

    private void DispatchEvent(TransitionResult<LspServerState, LspServerEvent> e)
    {
        switch (e.Event)
        {
            case LspServerEvent.Start: OnStart?.Invoke(this, e); break;
            case LspServerEvent.ConnectSucceeded: OnConnectSucceeded?.Invoke(this, e); break;
            case LspServerEvent.ConnectFailed: OnConnectFailed?.Invoke(this, e); break;
            case LspServerEvent.BeginStop: OnBeginStop?.Invoke(this, e); break;
            case LspServerEvent.StopSucceeded: OnStopSucceeded?.Invoke(this, e); break;
            case LspServerEvent.StopFailed: OnStopFailed?.Invoke(this, e); break;
        }
    }
}
```

外部订阅特定事件：`fsm.OnStart += handler;`，无需在统一 `StateChanged` 里 switch。

### 3. 生成器生成转换表

```csharp
// 生成器生成
internal sealed partial class LspServerStateMachine
{
    private static readonly FrozenDictionary<TransitionKey<LspServerState, LspServerEvent>, TransitionRule<LspServerState>> _table = BuildTable();

    private static FrozenDictionary<TransitionKey<LspServerState, LspServerEvent>, TransitionRule<LspServerState>> BuildTable()
    {
        return new Dictionary<TransitionKey<LspServerState, LspServerEvent>, TransitionRule<LspServerState>>
        {
            [new(LspServerState.Stopped, LspServerEvent.Start)] = new(LspServerState.Starting, CanRestartFromError),
            [new(LspServerState.Error, LspServerEvent.Start)] = new(LspServerState.Starting, CanRestartFromError),
            [new(LspServerState.Starting, LspServerEvent.ConnectSucceeded)] = new(LspServerState.Running, action: OnConnected),
            ...
        }.ToFrozenDictionary();
    }

    private readonly Fsm<LspServerState, LspServerEvent> _fsm;
    public LspServerState CurrentState => _fsm.CurrentState;

    public LspServerStateMachine()
    {
        _fsm = new Fsm<LspServerState, LspServerEvent>(_table, LspServerState.Stopped);
        _fsm.StateChanged += (_, e) => DispatchEvent(e);
    }

    public TransitionResult<LspServerState, LspServerEvent> Trigger(LspServerEvent evt, FsmContext? ctx = null)
        => _fsm.Trigger(evt, ctx);
}
```

### 4. 生成器项目

新建 `generators/Fsm.Generator/`（IIncrementalGenerator），注册到 `Generators.slnx`。生成器职责：
1. 扫描 `[StateMachine]` 特性找到目标类
2. 读取 `StateType`/`EventType`/`InitialState`
3. 扫描类上所有 `[Transition]` 特性构建转换表
4. 扫描方法上 `[Guard]`/`[Action]` 特性关联守卫/动作
5. 读取 `TEvent` 枚举所有值生成独立 event
6. 生成 partial class 补充代码

## 替代方案

### A. 保持手工转换表（不做生成器）

- 优点：无生成器调试成本
- 缺点：样板代码重复、守卫/动作与转换表分离、事件订阅粗粒度
- 放弃原因：用户明确要求生成器方式

### B. 纯类级特性（无方法级守卫/动作）

- 优点：生成器更简单
- 缺点：守卫/动作无法用特性表达，仍需手工关联
- 放弃原因：守卫是状态机的核心能力，不能缺失

### C. 用 Rx IObservable 事件流

- 优点：更强大的事件组合能力
- 缺点：引入 Rx 依赖，AOT 兼容性风险，过度设计
- 放弃原因：C# event 已够用，符合 AOT

## 后果

- 正面：
  - 转换表声明式，编译时生成器验证 From/Event/To 类型一致
  - 守卫/动作与转换表同处一类，关联自动
  - 每个事件独立订阅点，外部无需 switch
  - 减少样板代码，新增状态机只需标特性
- 负面：
  - 生成器调试复杂（Roslyn IncrementalGenerator 学习曲线）
  - 特性参数用 `object` 传递枚举值，失去编译时类型安全（生成器内部验证）
  - 新增生成器项目，编译链多一层
- 中性：
  - 已改造的 5 个类需二次改造用特性（机械替换）

## 实现路径

1. 定义特性（Abstractions/Attributes/FsmAttributes.cs）
2. 新建 `generators/Fsm.Generator/` 项目 + 注册 Generators.slnx
3. 生成器实现（扫描特性 → 生成 partial class）
4. 生成器单元测试
5. 改造 5 个已改造类用特性（DownloadStateMachine、LspServerInstance、MonitorSession、ShannonEntropyDetector、UnifiedCircuitBreaker）

<!-- 🤖 Auto Decision: 2026-08-29 -->
<!-- 决策: 类级 [Transition] + 方法级 [Guard]/[Action] 特性 + 生成器为每个事件生成独立 C# event -->
<!-- 原因: 用户要求生成器方式替代手工转换表;事件订阅需细粒度(每事件独立 event) -->
<!-- 替代方案: 纯类级特性(守卫缺失) / Rx IObservable(AOT 风险) / 保持手工(用户否决) -->
<!-- 验证: ADR 文档,未实现代码,无需编译 ✅ -->
