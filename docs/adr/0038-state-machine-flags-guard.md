# 0038. 状态机 + 守卫 + [Flags] 位标志

- 状态：accepted
- 日期：2026-08-29
- 决策者：项目架构组
- 取代：[0018](docs/adr/0018-loop-detector-state-machine.md)
- 验证：Brain 编译 0 警告 0 错误，767 测试全通过 ✅
- 实现说明：EntropyDetectionState 改为 [Flags] 位标志（None=0/Monitoring=1/Suspected=2/Confirmed=4），预留状态组合扩展性

## 背景

ADR 0018 用显式状态枚举（`Monitoring, Suspected, Confirmed`）+ switch 表达式实现状态机。当前状态互斥，但未来可能需要状态组合（如 `Monitoring | Retrying`、`Suspected | Timeout`），显式枚举无法表达组合，需为每个组合定义独立值，导致状态爆炸。

## 决策

**状态机 + 守卫 + [Flags] 位标志，降低状态爆炸。**

1. **[Flags] 位标志枚举**：状态定义为位标志，组合通过位运算表示
   ```csharp
   [Flags]
   enum DetectionState : byte
   {
       Monitoring = 1,
       Suspected  = 2,
       Confirmed  = 4,
       Retrying   = 8,  // 新增属性，无需为 Monitoring|Retrying 定义独立状态
   }
   ```
2. **守卫（Guard）**：转换上的条件，检查状态属性而非完整状态
   ```csharp
   // 守卫检查属性组合，而非具体状态
   if ((state & DetectionState.Suspected) != 0 && InConfirmWindow())
       state = DetectionState.Confirmed;
   ```
3. **二次确认（去抖）**：保留 ADR 0018 的时间窗口二次确认
4. **状态转换用 switch 表达式 + 守卫**：保留显式转换风格，守卫区分条件

**效果**：N 个状态属性 → 2^N 种组合，但只需 N 个枚举值 + 守卫，无需为每个组合定义独立状态。

## 替代方案

1. **显式枚举（ADR 0018 原方案）**：放弃。状态组合需为每个组合定义独立值，状态爆炸。
2. **用 int 位掩码手写**：放弃。无类型安全，易出错。
3. **用第三方状态机库**：放弃。AOT 兼容性未知（ADR 0002）。

## 后果

- 正面：状态组合用位运算表达，无需独立枚举值；新增状态属性只需加一个位，不影响现有；守卫检查属性组合灵活
- 负面：[Flags] 枚举组合语义需开发者理解位运算；调试时状态值是数字需解码
- 中性：当前状态互斥时 [Flags] 退化为普通枚举，无额外开销；未来扩展状态组合时受益
