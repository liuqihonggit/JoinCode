# TASK113 — 结构化输出体系复核（TASK103 §2.3 #45a）

> 状态：**体系已实现（账实不符）；另修一处 env 注释断链** · 日期 2026-11-22 · 总账编号：TASK103 §2.3 **#45a / TASK071-C6**
> 基准树：`D:\project\claude-code-rust\claude-code-rev-main\src`
> 本仓库：`D:\project\w2`

---

## 0. 结论（先看这里）

原文「需整体设计，C# 无限重试风险」**已过时**：TASK071-C6 在提交 `938fe0bc1`（2026-11-10）已完整落地，总账 #45a 未回填属账实不符。复核四条链路全部有活消费：

1. **合成工具注册**：`McpInitModule.RegisterSyntheticOutputToolAsync`（`modules/apps/engine/app/modules/McpInitModule.cs:148-185`），`--json-schema` 提供时按 schema 动态创建，对齐基准 `createSyntheticOutputTool`。
2. **验证与结果**：`SyntheticOutputToolHandler`（`modules/cognition/hands_core/tool_handlers/system_tools/handlers/SyntheticOutputToolHandler.cs:18-39`），通过 → `ToolResult.StructuredOutput`，失败 → is_error tool_result 让模型重试。
3. **重试上限**：`QueryEngine.cs:383-391` 计数并在 `StructuredOutputCallCount >= MaxStructuredOutputRetries`（默认 5）时发 `error_max_structured_output_retries` chunk（`:479-487`），对齐基准 `QueryEngine.ts:1004-1029` 的 `>= maxRetries` 判定与默认 `MAX_STRUCTURED_OUTPUT_RETRIES=5`。**不是无限重试**。
4. **SDK 回传**：`QueryEngine.cs:308` 的 Complete chunk 带 `structuredOutput` → `SessionController.cs:360` → `NonInteractiveExecuteStep.cs:62` → `NonInteractiveModeRunner.cs:73` → `SdkMessage.cs:249-250` 的 `structured_output` 字段（仅 success）。

**新发现的小断链**：`QueryMiddlewareContext.cs:173-176` 与 `CommandLineOptions.cs:252` 的注释都写"可通过 `MAX_STRUCTURED_OUTPUT_RETRIES` 环境变量覆盖"，但全仓只有注释命中，没有任何代码读取该 env（`grep` 证据）。本次补上解析，让注释成真；不改默认值、不改协议。

---

## 1. 四问回答

### 问1 专家隐性实践
"默认 5 次、可 env 覆盖"是可运维性要求：某些任务 schema 复杂，模型需要多轮修正；写死 5 会让这类任务永远失败。注释承诺了 env 却没有消费点，比没有该特性更糟——运维会以为调了 env 生效，实际毫无变化。

### 问2 原始素材（一手）
- 基准：`src/QueryEngine.ts:1004-1029`（`countToolCalls(SYNTHETIC_OUTPUT_TOOL_NAME)` + `parseInt(process.env.MAX_STRUCTURED_OUTPUT_RETRIES || '5')` + `>=` 判定 + error result）、`tools/SyntheticOutputTool/SyntheticOutputTool.ts:28-63/112-122`、`cli/print.ts:952`（subtype 分支）、`entrypoints/sdk/coreSchemas.ts:1421`（`structured_output: z.unknown().optional()`）。
- 本仓：`McpInitModule.cs:148-185`、`SyntheticOutputToolHandler.cs:18-39`、`QueryEngine.cs:308/380-391/479-487`、`QueryMiddlewareContext.cs:163-176`、`SdkMessage.cs:249-250`、`NonInteractiveModeRunner.cs:73`。
- 断链证据：`grep -rni "structured_output_retries|StructuredOutputRetries" modules --include=*.cs` 只命中 9 处注释/常量名，**无 `Environment.GetEnvironmentVariable` 调用**。

### 问3 具体方法与工具
按"注册→验证→计数→回传"四段追调用链；用全仓 grep 对注释承诺的 env 做产生点检查（与 #35/#36/#37 同法：配置键存在不等于被消费）。

### 问4 独立验证来源
基准 `QueryEngine.ts` 与 `cli/print.ts`/`coreSchemas.ts` 三处互证 error subtype 与结果字段；本仓 `SyntheticOutputToolHandlerTests`（14 项）锁定验证成功/失败/构造边界，`QueryEngineTests` 锁定 error chunk 形态。

---

## 2. 差异与判定

| 维度 | 基准 | 本仓 | 判定 |
|---|---|---|---|
| 合成工具 | 按 schema 动态创建 | `--json-schema` → 动态注册 | 已对齐 |
| 结构化结果 | `result.structured_output` | `SdkMessage.structured_output` | 已对齐 |
| 重试上限 | 默认 5，env 可覆盖，`>=` 判定 | 默认 5、`>=` 判定，env **仅注释** → 本次补 | 已对齐（补断链后） |
| 失败 subtype | `error_max_structured_output_retries` | 同字符串 | 已对齐 |

---

## 3. 施工

在 `QueryMiddlewareContext` 增加纯函数 `ResolveMaxStructuredOutputRetries(string?)`（非法/≤0 回退 5），属性初始化器读 `MAX_STRUCTURED_OUTPUT_RETRIES`。测试覆盖：合法值、空串、非法值、0/负值回退。行为不变项：默认值仍 5，chunk 文案与 StopReason 不变。

---

## 4. 验收（回填）

- 单测：Brain.Other.Tests **520/520 绿**（新增 9 个 env 解析 Theory 用例：合法 7/1/5、空/空白/null/非数字/0/负数回退 5）；`Hands.ToolHandlers.Tests` 筛选 `SyntheticOutput` **12/12 绿**。
- 编译/真机：`Build-AllDomains.ps1 -Changed` 1 项目通过；CLI 全量单线程重编 0 错误；真机 `--selftest-tui` `HOST SELFTEST OK` EXIT=0。
- 产物：`JoinCode.Brain.Query.dll` 含 UTF-8 `ResolveMaxStructuredOutputRetries`，UTF-16 含 `MAX_STRUCTURED_OUTPUT_RETRIES`——注释承诺的 env 现在有真实消费点。
- 总账 #45a 划掉并指向本文档。
