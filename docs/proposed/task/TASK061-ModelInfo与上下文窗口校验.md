# TASK061 — ModelInfo 信息类 + 上下文窗口配置校验（1M 时代）

> 状态：in_progress（2026-10-21 开工）
> 前置：巨型请求修复（4c27592ab）完结；用户指令："主流模型上下文已达 1M，模型配置里写模型名称+上下文大小，不对的直接报错；建一个 modelInfo 信息类"

## 调研结论

| 资产 | 现状 | 缺口 |
|------|------|------|
| ModelEntry（abs_core/configuration/llm） | Id/DisplayName/ContextWindow/Description 配置 DTO | 无自校验；无"当前模型"聚合信息类 |
| ContextWindowResolver（guard） | env > ModelConfigLoader.FindModel > ProviderDefinition > **兜底 200K** | **FindModel 命中但 ContextWindow=0 直接返回 0**（agnes 9 模型全 0 实锤）→ 下游 DecidePreflight 抛不友好 ArgumentOutOfRange；兜底 200K 掩盖配置错误（1M 模型被错误当 200K） |
| ContextFoldService（brain） | 折叠后仍超紧急阈值抛 ContextOverflowException（骨架已有） | 入口 ctxMax=0 时不友好 |
| providers_llm/{vendor}/models.json | contextWindow 字段已有 | agnes 9 模型全 0（脏数据）；加载零校验 |

## 方案（三步）

### Step 1 — ModelInfo 信息类（abstractions）
`ModelInfo` sealed record：ModelId/Provider/DisplayName/ContextWindow + `ValidateOrThrow()`
（contextWindow<=0 → ConfigurationException 带修复指引：模型名+供应商+providers_llm 路径）；
静态 `FromEntry(provider, ModelEntry)` 投影。

### Step 2 — ContextWindowResolver 严格化（guard）
`ResolveCurrentContextWindow`：FindModel 命中但 ContextWindow<=0 → 抛 ConfigurationException
（不再静默透传 0）；找不到模型仍兜底 200K（未知模型不阻断，日志警告）。

### Step 3 — agnes 脏数据处置
agnes models.json 9 模型 contextWindow=0 — 校验报错即用户要求的"直接报错"；
不猜数据（多模态模型窗口未知），错误信息带修复指引由用户补值。

## 验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| ModelInfo + ValidateOrThrow（abstractions） | resolver/切换链复用 | 待做 | 待真机 |
| resolver 0 值严格化（guard） | 启动/请求前窗口解析 | 待做 | 待真机（agnes 报错/sensenova 正常） |
| 错误友好呈现 | TUI StartupErrorPrinter 链 | 已有 | 复用 |
