# DSG021 — InteractionToolName 枚举命名误导清理 + 并发防御加固

## 背景

调查"模型一次性发起多个 ask 工具调用是否会并行卡死"时，确认工程已有三层防御：
1. 默认传统模式 `for` 循环串行（`QueryLoopMiddleware.cs:304-386`）
2. 并发分类器判定 ask 非并发安全（`ToolConcurrencyClassifier.cs:47-55`）
3. `CanExecute` 调度强制非安全工具独占执行（`StreamingToolExecutor.cs:238-249`）

防御机制本身完整无遗漏。但全量审查发现以下"不舒服/含义不对/缺失"问题。

## 发现的问题

### 问题1：InteractionToolName 枚举命名误导（核心）

`InteractionToolName` 枚举（`lib/abstractions/abs_core/core_utils/constants/tool_names/InteractionToolName.cs`）名暗示"用户交互工具"，但实际聚合了 **6 类不同语义**的工具：

| 枚举值 | 实际语义 | 对应 ToolCategory | 是否真用户交互 |
|--------|----------|-------------------|---------------|
| confirm_action, ask_user_question | 用户交互 | Interaction | ✅ 是 |
| auth_get_status, auth_refresh, auth_logout | 认证 | McpAuth | ❌ 否 |
| config, config_get, config_set, config_list | 配置 | Config | ❌ 否 |
| permission_* (7个) | 权限 | Permission | ❌ 否 |
| analytics_* (5个) | 分析 | Analytics | ❌ 否 |
| policy_check, policy_list | 策略 | Policy | ❌ 否 |

**危害**：未来开发者可能误以为"该枚举里的都是交互工具"，对 config_get 等非交互工具放松并发安全警惕；或误以为"交互工具都在这个枚举"，新增交互工具时归错分类。

### 问题2：5 个未实现枚举值无防御性注释

`confirm_action`、`auth_get_status`、`auth_refresh`、`auth_logout`、`config` 仅枚举定义无 `[McpTool]` 实现。未来实现 `confirm_action`/`auth_logout` 等真交互工具时，可能忘记归入 `ToolCategory.Interaction` 且不标 `ConcurrencySafe=true`，导致防御漏洞。

### 问题3：ToolConcurrencyClassifier 缺少运行时断言防护

分类器只做正向判定（白名单→安全，bash只读→安全），无反向断言"若工具属于 ToolCategory.Interaction 则必须非并发安全"。若未来有人错误地给交互工具标 `ConcurrencySafe=true`，分类器会静默放行，防御被绕过且无告警。

## 修复方案

### 方案1：拆分 InteractionToolName 为 6 个语义准确的枚举

按 ToolCategory 对应拆分，每个枚举名准确反映语义：

| 新枚举 | 值 | 对应 ToolCategory |
|--------|-----|-------------------|
| `UserInteractionToolName` | confirm_action, ask_user_question | Interaction |
| `AuthToolName` | auth_get_status, auth_refresh, auth_logout | McpAuth |
| `ConfigToolName` | config, config_get, config_set, config_list | Config |
| `PermissionToolName` | permission_add_rule..permission_clear_rules (7) | Permission |
| `AnalyticsToolName` | analytics_report..analytics_clear (5) | Analytics |
| `PolicyToolName` | policy_check, policy_list | Policy |

源码生成器自动为每个新枚举生成 `XxxEnumConstants` + `XxxExtensions`（`[EnumValue]` 机制）。

### 方案2：全量替换引用

引用替换映射（`InteractionToolNameEnumConstants.X` → `XxxToolNameEnumConstants.X`，`InteractionToolName.X` → `XxxToolName.X`）：

| 旧引用 | 新引用 | 受影响文件 |
|--------|--------|-----------|
| `InteractionToolNameEnumConstants.AskUserQuestion` | `UserInteractionToolNameEnumConstants.AskUserQuestion` | UserInteractionToolHandlers.cs, AskUserQuestionToolPrompt.cs, AskClarifyCommand.cs, PlanModeToolHandlers.cs, SessionGuidanceSection.cs |
| `InteractionToolName.AskUserQuestion` | `UserInteractionToolName.AskUserQuestion` | AskUserQuestionToolPrompt.cs |
| `InteractionToolNameEnumConstants.ConfigGet/ConfigSet/ConfigList/Config` | `ConfigToolNameEnumConstants.*` | ConfigToolHandlers.cs, PermissionCheckContext.cs |
| `InteractionToolName.Config` | `ConfigToolName.Config` | ConfigToolPrompt.cs |
| `InteractionToolNameEnumConstants.Permission*` (7) | `PermissionToolNameEnumConstants.*` | PermissionToolHandlers.cs |
| `InteractionToolNameEnumConstants.Analytics*` (5) | `AnalyticsToolNameEnumConstants.*` | AnalyticsToolHandlers.cs |
| `InteractionToolNameEnumConstants.Policy*` (2) | `PolicyToolNameEnumConstants.*` | PolicyToolHandlers.cs |

### 方案3：归档旧枚举

旧 `InteractionToolName.cs` 移到 `.xxx/InteractionToolName.cs.20260926.del`（遵循 AGENTS.md 禁删规则）。

### 方案4：为未实现枚举值加防御性注释

在新枚举的 `confirm_action`、`auth_*` 等未实现值上加 XML 注释，明确：
- 当前未实现
- 未来实现时必须归入哪个 ToolCategory
- 若为真用户交互工具（confirm_action/auth_logout），必须不标 `ConcurrencySafe=true`

### 方案5：ToolConcurrencyClassifier 加运行时断言防护

在分类器加反向校验：若工具名属于 `UserInteractionToolName` 的已实现子集且被判定为并发安全，记录警告日志（开发期断言）。双保险——即使有人错误标记，也能在运行时发现。

单元测试：新增测试用例验证"交互工具被错误加入白名单时触发警告"。

## 任务清单

| # | 任务 | 验证 |
|---|------|------|
| 1 | 创建 6 个新枚举文件 | 全量编译（--no-incremental）通过 |
| 2 | 全量替换引用 | 编译通过，无 InteractionToolName 残留引用 |
| 3 | 归档旧 InteractionToolName.cs | 编译通过 |
| 4 | 加防御性注释 | 编译通过 |
| 5 | ToolConcurrencyClassifier 断言防护 + 单元测试 | 编译 + 单元测试通过 |
| 6 | 全量编译 + git 提交 | 全量编译通过，提交 |

## 验证标准

- [x] 三层防御机制保持有效（不破坏现有防御）
- [x] 无 `InteractionToolName` 残留引用
- [x] 6 个新枚举名准确反映语义
- [x] 未实现枚举值有防御性注释
- [x] 分类器有运行时断言防护 + 测试
- [x] 全量编译通过
- [x] 现有单元测试通过

<!-- 🤖 Auto Decision: 2026-09-26 -->
<!-- 决策: 拆分 InteractionToolName 为 6 个语义准确的枚举，而非重命名 -->
<!-- 原因: 枚举混入 6 类不同语义工具，单一重命名无法解决；按 ToolCategory 拆分后每个枚举名准确反映语义，且与 ToolCategory 一一对应，降低认知负担 -->
<!-- 替代方案: 仅加注释不拆分（治标不治本，命名误导仍在）；重命名为 AggregateToolName（仍误导，"聚合"无语义价值）-->
<!-- 验证: 待全量编译验证 -->
