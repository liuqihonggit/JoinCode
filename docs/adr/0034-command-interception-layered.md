# 0034. 命令拦截分层 Guard+Interceptor+Dispatcher

- 状态：superseded by 0039
- 日期：2026-08-29
- 决策者：项目架构组
- 取代原因：放弃全状态机的理由（状态爆炸）在引入 [Flags] + 守卫后不成立。详见 [0039](docs/adr/0039-command-interception-state-machine.md)

## 背景

命令拦截需要处理拒绝/改写/转交/放行等多种行为，涉及有状态和无状态混合的逻辑。若用单一状态机处理所有情况，状态空间爆炸，难以维护。

## 决策

**分层决策 + 链式调度，按"是否有状态/异步"分两层**：

- **Guard 层**：无状态、纯决策、可同步。适合拒绝/改写/转交/放行。按 Priority 升序执行
- **Interceptor 层**：有状态、异步。处理需交互/状态的决策
- **Dispatcher**：链式调度，先 Guard 链再 Interceptor 链

**决策模型**：`CommandDecision`（sealed record 层次）

**不强制全状态机**：Guard 层纯函数无副作用，Interceptor 层才需状态，两者分离避免状态空间爆炸。

定位文件：`docs/design/命令拦截架构改造.md`

## 替代方案

1. **全状态机**：放弃。状态空间爆炸，所有拦截逻辑共享状态，难以维护。
2. **单一中间件管道**：放弃。无状态/有状态混在一起，无法按 Priority 升序同步执行纯决策。
3. **仅 Guard 层**：放弃。部分拦截需交互/状态（如权限确认），纯 Guard 无法处理。

## 后果

- 正面：职责分离；Guard 层纯函数易测试；按 Priority 升序高效
- 负面：两层需协调；新增拦截器需判断归属 Guard 还是 Interceptor
- 中性：Sed/Build 保留独立中间件，不迁移为 Interceptor（阶段C 决策）
