// 此文件已拆分为以下7个文件，原始 AotSafetyAnalyzer 类已废弃：
// - AotSafetyHelpers.cs       (共享辅助方法)
// - AotSafetyRules.cs         (JCC1xxx AOT安全+代码规范基础)
// - AsyncSafetyRules.cs       (JCC2xxx/3xxx 异步安全+交互输入)
// - ConcurrencyRules.cs       (JCC3xxx/4xxx/5xxx 并发+即发即忘+资源泄漏)
// - PerformanceRules.cs       (JCC5xxx/6xxx 性能优化)
// - DeadCodeRules.cs          (JCC7xxx 死代码检测)
// - CodeOrganizationRules.cs  (JCC8xxx/9xxx/10xxx 代码组织+文件IO+枚举规范)
//
// 原始类 AotSafetyAnalyzer 已移除 DiagnosticAnalyzer 注册，
// 避免空壳被 Roslyn 加载。所有规则已迁移到独立的 Analyzer 类中。
