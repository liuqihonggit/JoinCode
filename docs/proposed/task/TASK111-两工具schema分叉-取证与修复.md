# TASK111 — TaskCreate / AskUserQuestion schema 分叉（TASK103 §2.3 #44 取证）

> 状态：**AskUserQuestion 判不适用；TaskCreate 发现真断链并修复** · 日期 2026-11-22 · 总账编号：TASK103 §2.3 **#44 / TASK071-C4**
> 基准树：`D:\project\claude-code-rust\claude-code-rev-main\src`
> 本仓库：`D:\project\w2`

---

## 0. 结论（先看这里）

原文两半都要分开看：

- **AskUserQuestion：功能已等价，判不适用。** 基准 `questions` 是结构化数组（1-4 问题）+ 权限层 `updatedInput.answers` 回灌 + 工具结果结构字段 `answers`。本仓 `questions` 声明为 string，但派发生成器对非字符串 `JsonElement` 走 `GetRawText()`（`McpToolDispatchGenerator.cs:592`），再经 `LlmJsonHelper.DeserializeValue`/`RepairJson` 解析——**模型按基准发结构化数组照样能用**，不是"传了就被丢"。回传是本仓工具结果模型根本没有结构化输出通道，只有 `WithText`（`UserInteractionToolHandlers.cs:70-76`），所以答案以 `"key"="value"` 文本回模型；bench 与 C# 的 `model-visible` 信息相同。为对齐字段去改 ToolResult 契约会影响所有工具，收益仅字段形态，不做。
- **TaskCreate：发现真断链，已修。** 本仓 prompt 明确让模型传 `subject`/`activeForm`（`TaskToolsPrompt.cs:52-54`），而 handler 只声明 `title`（`TaskToolHandlers.cs:24`），参数名归一表**没有** `subject→title`（`ToolParameterNameRepairer.cs:19-53`）。结果是"照着本仓提示词调用必报缺 title"，同时照基准提示词调用也必失败。修法不动 schema：①提示词改成本仓真正的字段名；②在唯一真相参数别名表加 `subject→title`，让基准习惯写法与旧会话继续可用。

---

## 1. 四问回答

### 问1 专家隐性实践
工具 schema 与提示词必须同源；提示词里出现 schema 不存在的字段是典型"文档撒谎"，模型会稳定失败且每次都归因于自己。跨产品兼容优先走**别名层**而不是双写 schema：schema 是模型可见契约，改动影响面大；别名在唯一真相表里一处登记，宿主/exe/CLI 三条路径同时生效。基准名与本仓名不同时，别名表就是兼容边界。

### 问2 原始素材（一手）
- 基准 TaskCreate：`src/tools/TaskCreateTool/TaskCreateTool.ts:18-33`（`subject` 必填、`description` 必填、`activeForm?`、`metadata?`）、`:130-137`（结果文本）。
- 基准 AskUserQuestion：`src/tools/AskUserQuestionTool/AskUserQuestionTool.tsx:19-31`（question/header/options 2-4/multiSelect/annotations）、`:55-73`（`answers:record<string,string>?` + 顶层 `z.strictObject{questions:array(1-4)}`）、`:224-244`（结果文本）；权限层注入答案：`components/permissions/AskUserQuestionPermissionRequest/AskUserQuestionPermissionRequest.tsx:398-407`（`updatedInput={...input, answers, annotations}`）。
- 本仓 TaskCreate：`modules/storage/vault/todo/TaskToolHandlers.cs:22-31`（`title` 必填 + description/assignee/due_date/priority/tags）；提示词 `modules/abstractions/ai/prompts/tool_prompts/planning/TaskToolsPrompt.cs:50-56`（**subject/activeForm**）；别名表 `modules/abstractions/abstractions.primitives.tools/src/tool_parameters/ToolParameterNameRepairer.cs:19-53`（无 subject）。
- 本仓 AskUserQuestion：`modules/mcp/mcp.tool_handlers/user/core/UserInteractionToolHandlers.cs:25-51`（`string questions` + `LlmJsonHelper.DeserializeValue`）、`:59-76`（Cancelled/Deferred/Answers 文本回传）；派发兜底 `modules/generators/mcp_tool_dispatch.generator/McpToolDispatchGenerator.cs:588-593`（非 string → `GetRawText()`）。
- 结果模型：`abstractions.primitives.models/src/models_chat/interactive/InteractiveModels.cs:23-39`（`Answers` 字典在内部结果 DTO 里存在，但 handler 只把它拼成文本，全仓无 `ToolResult.WithStructuredOutput` 通道）。

### 问3 具体方法与工具
两侧工具定义/提示词/派发代码逐行对照；对本仓 TaskCreate 用"提示词字段集 vs schema 字段集 vs 别名表"三方交叉验证，确认 `subject` 无任何落点；对 AskUserQuestion 追 `JsonElement → string → DeserializeValue` 全链路，确认结构化数组不是失败路径。

### 问4 独立验证来源
注册工具名差异（`TaskCreate`/`AskUserQuestion` vs 本仓 `task_create`/`ask_user_question`）由 `ToolNameResolver.RepairToolName`（去下划线 + 忽略大小写）兜住，且 #38 已裁决命名体系维持 snake_case；基准 schema 里的 `activeForm` 在本仓 `task_create` 没有等价参数，但 `todo_write` 的 `activeForm` 是另一条链（`TodoToolHandlers.cs:25-27`），不能混为一谈。

---

## 2. 差异与判定

| 维度 | 基准 | 本仓 | 判定 |
|---|---|---|---|
| TaskCreate 主键 | `subject` 必填 | `title` 必填 + 无别名 | **真断链：提示词与 schema 脱节** → 修 |
| TaskCreate 描述 | 必填 | 可选 | 差异但本仓更宽松，不动 |
| `activeForm` | 任务工具字段 | `task_create` 无该参数（`todo_write` 有） | 提示词移除，避免误导 |
| AskUserQuestion 入参 | 结构化数组 | string + 结构化数组也接受（GetRawText+修复） | 功能等价 |
| AskUserQuestion 回传 | `answers` 结构字段 + 文本 | 仅文本（无结构化结果通道） | 形态差异，模型可读信息相同 |
| 工具名 | PascalCase | snake_case + 名称修复器 | #38 已裁决，不重开 |

---

## 3. 施工（TaskCreate 断链）

1. `ToolParameterNameRepairer.ParameterAliases` 增 `["subject"] = "title"`。该表是宿主/exe/CLI 三条路径唯一真相；对**声明了 subject 的工具**（如 `gh_pr_merge`，`GitHubToolHandlers.Pr.cs:645`）不生效，因为直接匹配优先（`TryMatchParameter:153` 前先查 schemaProps）。
2. `TaskToolsPrompt.cs:52-54` 改为本仓真实字段 `title`，删掉无消费点的 `activeForm` 描述，并注明可选扩展字段。
3. TDD：别名表测试新增 `subject→title`（含"工具自身声明 subject 时不被劫持"的负向用例）；提示词测试断言不再出现 `subject`/`activeForm` 字段描述。

> 不改 handler schema、不改 ToolResult 契约、不改工具命名体系。

---

## 4. 验收（回填）

- 单测：别名表项目 `Abstractions.Primitives.Tools.Tests` **90/90 绿**（新增 `subject→title` 映射 + "工具自身声明 subject 不被劫持"负向用例）；Brain.Other.Tests **511/511 绿**（新增 `TaskCreatePromptTests` 1 项，锁定字段说明与 handler 一致）。
  - 红→绿：提示词测试首版断言 `NotContain("activeForm")` 被自己新增的说明句"本工具没有 activeForm 参数"命中，改为断言不出现字段条目 `**activeForm**`。测试写法问题，非生产逻辑。
- 编译：`Build-AllDomains.ps1 -Changed` 3 项目全通过；CLI `JoinCode.csproj` 全量单线程重编 0 错误；真机 `--selftest-tui` `HOST SELFTEST OK` EXIT=0。
- 产物：`Abstractions.Primitives.Tools.dll` 的 UTF-16 字符串区 `subject`(offset 96942) 与 `title`(96958) 相邻，证明别名映射进产物；`Abstractions.AI.dll` 含新文案「任务列表中显示的标题就是」（offset 144384），旧字段条目从 `TaskCreateToolPrompt.GetPrompt()` 输出中消失（测试锁定）。
- 总账 #44 划掉并指向本文档。
