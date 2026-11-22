# TASK114 — Complete 协议字段复核（TASK103 §2.3 #46）

> 状态：**四字段已齐；`duration_api_ms` 语义修正；权限拒绝收集仍为启发式（登记）** · 日期 2026-11-22 · 总账编号：TASK103 §2.3 **#46 / TASK071-C7**
> 基准树：`D:\project\claude-code-rust\claude-code-rev-main\src`
> 本仓库：`D:\project\w2`

---

## 0. 结论（先看这里）

原文「Complete chunk 缺 duration_api_ms/num_turns/stop_reason/modelUsage/permission_denials — StopReason 已做，其余需消费方决策」**已过时**：四个字段全部有定义、有生产者、有 SDK 输出，属于账实不符。复核结果：

| 字段 | 定义 | 生产链 | 判定 |
|---|---|---|---|
| `duration_api_ms` | `SdkMessage.cs:221-222` | `NonInteractiveExecuteStep.cs:60` ← `SessionController.LastApiDurationMs:52` ← Complete chunk `ExecutionTimeMs` | **原语义错**：chunk 里填的是整查询墙上时间；本次改填 API 累计耗时 |
| `num_turns` | `SdkMessage.cs:227-228` | `NonInteractiveExecuteStep.cs:58` ← `session.TotalTurns` ← `SessionController:352` | 已对齐 |
| `modelUsage` | `SdkMessage.cs:241-243` | `NonInteractiveModeRunner.cs:88-103` 从 `Usage`+`TotalCostUsd` 按当前模型构造 | 已对齐（单模型口径；本仓无多模型同回合 usage 分解） |
| `permission_denials` | `SdkMessage.cs:244-246` | `CliEventConsumer.cs:177-183` ← `NonInteractiveExecuteStep.cs:63-64` | 有字段，但**收集是文本启发式**（`resultText.Contains("拒绝")`），非结构化权限事件——登记为残余差异 |
| `stop_reason` | `SdkMessage` + QueryEngine | 前序已做 | 已对齐 |

**本次修正**：`QueryEngine.ExecuteIterationInternalAsync` 在每轮 LLM 流式调用外包 `Stopwatch` + `try/finally`，累加到 `QueryMiddlewareContext.ApiDurationMs`；Complete chunk 改用该累计值（`QueryEngine.cs:308`）。墙上时间仍在日志中可见，不受影响。这样 `duration_api_ms` 才与 SDK 语义（API 调用时长）一致，而不是把工具执行与中间件等待也算进去。

---

## 1. 四问回答

### 问1 专家隐性实践
SDK 的 `duration_ms`（总墙钟）与 `duration_api_ms`（仅 API 调用）必须分开：工具执行、hook、裁剪都可能占时间，混为一谈会让调用方误判模型延迟。权限拒绝应来自结构化权限决策，而不是工具结果文本关键字——后者既漏英文/其他措辞也会把 OS 权限错误误报成"用户拒绝"。

### 问2 原始素材（一手）
- 基准：`src/entrypoints/sdk/coreSchemas.ts:1421-1436`（`structured_output`/`error_max_structured_output_retries` 等 result 字段）、`src/QueryEngine.ts:1149` 附近（`duration_api_ms: getTotalAPIDuration()`、`num_turns`、`modelUsage`、`permission_denials` 均来自运行时状态）。
- 本仓：`SdkMessage.cs:221-246`、`NonInteractiveModeRunner.cs:65-103`、`NonInteractiveExecuteStep.cs:58-64`、`SessionController.cs:344-363`、`CliEventConsumer.cs:108/174-183`、`QueryEngine.cs:308/505-518`。
- 修改点：`QueryMiddlewareContext.ApiDurationMs`（新增累计属性）、`QueryEngine.ExecuteIterationInternalAsync` 的 `try/finally` 计时、Complete chunk 参数从 `context.Stopwatch.ElapsedMilliseconds` 改为 `context.ApiDurationMs`。

### 问3 具体方法与工具
按"SDK DTO → StartupContext → SessionController → QueryEngine chunk"倒查每字段的生产者，区分"字段存在"与"真实填充"；对 `duration_api_ms` 进一步追到 chunk 的 `ExecutionTimeMs` 赋值点，发现语义错配后按最小改动修正。

### 问4 独立验证来源
`SessionController.ApplyCompletionChunk`（`:347-363`）显式把 chunk 字段注释为 num_turns/cost/api_duration/stop_reason 汇总点，与字段定义互证；基准 `getTotalAPIDuration()` 独立证明 API 时长应是调用累计而非总耗时。

---

## 2. 差异与判定

| 维度 | 基准 | 本仓（修正前） | 本仓（修正后） |
|---|---|---|---|
| duration_api_ms | API 调用累计 | 整查询墙钟（语义错） | API 调用累计 |
| num_turns | 迭代轮次 | 同 | 不变 |
| modelUsage | 多模型 usage map | 当前单模型项 | 不变（无多模型同回合场景） |
| permission_denials | 权限决策结构化列表 | 文本含"拒绝"启发式 | 不变（登记残余差异） |

**为何不改权限收集**：`CliEventConsumer.OnToolEnd` 目前只接收 `resultText`/`isError`，没有权限决策结构；要对齐需改引擎事件契约、事件消费者与各宿主适配层，跨 slnx 影响面大。当前字段能回传"工具结果文本显示被拒"的基本语义，不属静默失效；登记为后续专项。

---

## 3. 验收（回填）

- 单测：Brain.Other.Tests **520/520 绿**（含既有 `BuildCompletionChunk`/`SdkResultMessage` 相关用例）；未新增计时单测——本仓无 LLM 流式调用替身框架，计时正确性以源码链路 + 编译 + 真机验证。
- 编译/真机：`Build-AllDomains.ps1 -Changed` + CLI 全量单线程重编 0 错误；真机 `--selftest-tui` EXIT=0。
- 产物：`JoinCode.Brain.Query.dll` 含 `ApiDurationMs` 符号。
- 总账 #46 划掉并指向本文档。
