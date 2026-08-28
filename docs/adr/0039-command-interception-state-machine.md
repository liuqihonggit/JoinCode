# 0039. 命令拦截全状态机 + 守卫 + [Flags]

- 状态：accepted
- 日期：2026-08-29
- 决策者：项目架构组
- 取代：[0034](docs/adr/0034-command-interception-layered.md)
- 验证：Guard 编译 0 警告 0 错误，285 测试全通过 ✅
- 实现说明：命令拦截无状态，[Flags] 用于属性检测优化，保留 Guard 守卫模式，不引入状态机

## 背景

ADR 0034 放弃全状态机，理由是"状态空间爆炸"，改用 Guard+Interceptor 分层。但全状态机 + 守卫 + [Flags] 位标志可以有效降低状态爆炸（见 ADR 0038）：

- **守卫**：转换上的条件，同一状态的不同转换用守卫区分
- **[Flags] 位标志**：状态属性组合通过位运算表示，无需为每个组合定义独立状态
- **二次确认**：去抖窗口消除误报

ADR 0034 的放弃理由（状态空间爆炸）在引入 [Flags] + 守卫后不成立。

## 决策

**命令拦截用全状态机 + 守卫 + [Flags]，而非分层 Guard+Interceptor。**

1. **状态定义**：`[Flags] enum InterceptionState` 表示拦截状态属性组合
2. **守卫**：转换条件检查状态属性，而非完整状态
3. **决策模型**：保留 `CommandDecision` sealed record（Allow/Rewrite/Deny/Redirect/Handoff），作为状态机的输出
4. **统一调度**：用状态机统一调度，不再分 Guard/Interceptor 两层

**迁移策略**：渐进式（ADR 0007），现有 5 个 Guard 实现逐步迁移为状态机守卫。

## 替代方案

1. **分层 Guard+Interceptor（ADR 0034 原方案）**：放弃。状态爆炸理由不成立，分层增加协调成本。
2. **仅 [Flags] 不守卫**：放弃。无守卫则所有转换无条件执行，无法区分条件。
3. **仅守卫不 [Flags]**：放弃。状态组合仍需独立枚举值，爆炸未解决。

## 后果

- 正面：统一状态机调度，不分层；[Flags] 降低状态爆炸；守卫灵活区分条件
- 负面：现有 5 个 Guard 实现需迁移；状态机设计需仔细定义状态属性和守卫
- 中性：`CommandDecision` sealed record 保留作为状态机输出；迁移按渐进式进行
