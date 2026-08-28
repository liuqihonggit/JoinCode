# 0040. 企业级状态机框架 — 转换表 + 守卫 + 共享上下文

- 状态：proposed
- 日期：2026-08-29
- 决策者：用户确认
- 增强：[0038](docs/adr/0038-state-machine-flags-guard.md)、[0039](docs/adr/0039-command-interception-state-machine.md)

## 背景

ADR 0038/0039 引入了 [Flags] 位标志优化，但仅是属性检测层面的改进。用户提供了完整的企业级状态机设计，核心流程：

```
获取当前状态 → 查表得到对应的事件函数指针 → 守卫判定条件是否满足 → 满足执行事件 → 转移到下一个状态
```

共享上下文放全局数据，无限循环可包括空闲状态在中间等候。枚举是最佳方式（编译时检查 + IDE 智能提示，比字符串强 100 倍）。

## 决策

**采用转换表 + 守卫 + 共享上下文 + 枚举编译时检查的企业级状态机框架。**

### 核心组件

1. **共享上下文（FsmContext）**：所有状态共享的数据，状态机引用回传
   ```csharp
   public class FsmContext
   {
       public Dictionary<string, object> CustomData { get; } = new();
       public StateMachine FSM { get; set; }
   }
   ```

2. **状态枚举（FsmStateId）+ 事件枚举（FsmEvent）**：编译时检查 + IDE 智能提示
   ```csharp
   public enum FsmStateId { Idle, Running, Paused, Completed, Dead }
   public enum FsmEvent { Start, Pause, Resume, Complete, Reset }
   ```

3. **状态接口（IFsmState）**：OnEnter/OnUpdate/OnExit/OnEvent
   ```csharp
   public interface IFsmState
   {
       FsmStateId StateId { get; }
       void OnEnter(FsmContext context);
       void OnUpdate(FsmContext context, float deltaTime);
       void OnExit(FsmContext context);
       void OnEvent(FsmContext context, FsmEvent evt);
   }
   ```

4. **转换表（Dictionary<TransitionKey, TransitionRule>）**：查表得转换规则
   - `TransitionKey`：(FromState, Event) 组合键
   - `TransitionRule`：Target + Action + Guard

5. **守卫（TransitionGuard delegate）**：条件检查，返回 true 才允许转换
   ```csharp
   public delegate bool TransitionGuard(FsmContext ctx);
   ```

6. **状态机核心（StateMachine）**：
   - `Trigger(evt)`：查表 → 守卫检查 → 执行 Action → SwitchTo
   - `Update(deltaTime)`：每帧调用当前状态的 OnUpdate
   - `SwitchTo(stateId)`：OnExit 旧 → OnEnter 新

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

### 应用场景

1. **循环检测器**（取代 ShannonEntropyDetector 当前 switch 实现）：
   - 状态：Monitoring, Suspected, Confirmed
   - 事件：Decline, Timeout, Confirm, Recover
   - 转换：Monitoring+Decline→Suspected, Suspected+Confirm→Confirmed, Suspected+Timeout→Monitoring, Confirmed+Recover→Monitoring
   - 守卫：IsDeclining, InConfirmWindow, IsRecovering
   - 上下文：entropy, declineStreak, firstTriggerTime, triggerCount

2. **命令拦截**（取代 CommandInterceptionDispatcher 当前 Guard 链）：
   - 状态：Evaluating, Rewriting, Denied, Redirected, Allowed
   - 事件：Match, Rewrite, Deny, Redirect, Allow
   - 转换表 + 守卫

## 替代方案

1. **仅 [Flags] 优化（ADR 0038/0039）**：不够。[Flags] 只是属性检测优化，缺少转换表、守卫、共享上下文的完整框架。
2. **用第三方状态机库（Stateless 等）**：放弃。AOT 兼容性未知（ADR 0002），且不自控。
3. **保留反射扫描**：放弃。NativeAOT 不兼容（ADR 0002）。

## 后果

- 正面：转换表显式可读；守卫灵活；共享上下文统一数据；枚举编译时检查；高可扩展性（加状态/事件只需加枚举值 + 转换规则）
- 负面：框架初期实现成本高；现有循环检测器和命令拦截需重构
- 中性：DI 注册替代反射，AOT 兼容；状态机框架位于 `foundation/Abstractions` 或 `infrastructure/`
