# 0018. 循环检测器状态机风格

- 状态：superseded by 0038
- 日期：2026-08-29
- 决策者：项目架构组
- 取代原因：显式枚举无法表达状态组合，[Flags] 位标志 + 守卫可降低状态爆炸。详见 [0038](docs/adr/0038-state-machine-flags-guard.md)

## 背景

循环/异常检测器（OutputLoop、LogicFingerprint、ToolCallSequence、ShannonEntropy）及干预中间件，早期用隐式 `if-else` + 标志变量管理状态，逻辑分散、难测试、难扩展。

## 决策

**状态机模式**：检测器内部用显式状态枚举 + switch 表达式实现状态转换。

- 状态定义：`enum XxxDetectionState { Monitoring, Suspected, Confirmed }`
- 转换驱动：`Record(input)` 方法内 `_state switch { ... }` 链式流转
- 每次返回的结果携带 `State` 字段，调用方可观察当前状态

**时间窗口二次确认（去抖）**：
- 检测器触发后不立即干预，进入 `Suspected` 状态等待二次确认
- 确认窗口内（如 5s）再次触发 → `Confirmed`（确认为真死循环）
- 窗口超时 → 复位到 `Monitoring`（误报消除）
- 时钟通过 `Func<DateTimeOffset>? clock = null` 注入，测试可控、生产用 `DateTimeOffset.UtcNow`

**配置统一到 Options 子配置类**：检测器所有参数集中到 `LoopInterventionOptions` 的子配置类（如 `ShannonEntropyConfig`），不散落在构造函数默认值。

**干预层显式状态枚举**：干预级别用 `enum InterventionLevel { None, Soft, Hard, Compact }` + 决策方法 `ClassifyIntervention(count)`，不用 `if-else` 链。

## 替代方案

1. **隐式 if-else + 标志变量**：放弃。逻辑分散、难测试、难扩展。
2. **用第三方状态机库**：放弃。引入依赖，且 AOT 兼容性未知（见 ADR 0002）。
3. **立即干预不二次确认**：放弃。误报高，正常循环会被误判为死循环。

## 后果

- 正面：状态转换显式可读；测试可控（时钟注入）；误报低（二次确认）
- 负面：状态枚举和 switch 表达式代码量略多
- 中性：`InformationEntropyGuardian` 从 `LoopInterventionOptions` 统一创建所有检测器（生产路径），测试可直接传入检测器实例
