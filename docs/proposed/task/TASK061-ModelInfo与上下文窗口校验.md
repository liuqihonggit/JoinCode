# TASK061 — ModelInfo 信息类 + 上下文窗口配置校验（1M 时代）

> 状态：implemented（2026-10-21 三层落地 + 全绿 + 真机验证；剩余用户验收 agnes 场景报错呈现）
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
| ModelInfo + ValidateOrThrow（abstractions） | resolver/两源投影 | ✅ 4 测试 | ✅ 单元 13/13 |
| resolver 0 值严格化（guard） | 启动/请求前窗口解析 | ✅ 2 测试 | ✅ 1284/1284 + 真机 sensenova 正常 |
| 请求前预算守卫（QueryLoopMiddleware.GuardContextBudget 孤儿接线） | 每轮迭代 historySnapshot 后 | ✅ 2 测试 | ✅ 933/933 |
| IChatContextManager.DecidePreflightWithCurrentSpecs | 中间件守卫消费面 | ✅ | ✅ |

## 最终基线

- guard.config.tests：1281 → **1284**（+ModelInfo 4 + resolver 2 - 既有调整）
- brain.context.tests：931 → **933**（+守卫 2）
- 提交：5a5d7fa89
- 真机：sensenova 窗口临时置 0 → 报错链验证通过后恢复；正常路径回复 `ok` 恢复

## 额外发现（调研结论）

- **DecidePreflight 是孤儿**（接口+实现齐全但无任何消费方）——请求前预算守卫此前根本没挂主链，本任务接线修复
- agnes 9 模型 contextWindow 全 0（真实脏数据）——严格化后被使用到时立即报错（带修复指引），非当前 profile 不阻断启动

<!-- 🤖 Auto Decision: 2026-10-21 -->
<!-- 决策: 三层修复（信息类+配置校验+请求前守卫）而非单点校验 -->
<!-- 原因: 用户三诉求（ModelInfo 信息类/配置不对直接报错/超窗直接报错）分别对应元数据/加载校验/运行守卫三层，且调研发现 DecidePreflight 孤儿缺陷（守卫从未挂主链）一并接线 -->
<!-- 替代方案: 仅配置加载校验（治标——0 值被兜底 200K 掩盖、超窗请求仍发 API）-->
<!-- 验证: 1284+933 全绿 + 真机 sensenova 正常/0 值报错链验证 ✅ -->
