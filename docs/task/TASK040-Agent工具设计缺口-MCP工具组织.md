# TASK040 — Agent 工具设计缺口：MCP 工具组织

> 来源：《Agent 工具设计》文档"海量的MCP工具"第2、3、4条
> 检索日期：2026-10-09

## 背景

设计要求：分班再分组（大类:gh工具 小类:查询功能），多层级渐进式阅读；情景模式作为工具说明书，-h 启动参数置顶情景模式阅读；切词搜索 + 菜单搜索。

## 已落地部分

| 设计点 | 落地位置 |
|--------|----------|
| 工具分类枚举（60+ 分类，[EnumValue]+源码生成器） | `constants/tool_meta/ToolCategory.cs` |
| 顶层分组（CoreTools 始终发 / McpTools 两阶段加载） | `constants/tool_meta/ToolGroupName.cs` |
| 三家 LLM 适配器 tool_groups 字段 | `AnthropicQueryService.cs:334`/`OpenAIQueryService.cs:324`/`ResponsesQueryService.cs:361` |
| mcp_list 按分类分组输出 + --category 过滤 | `McpCommand.cs:32-60` |
| 两级分组数据模型（Category + GroupName） | `abs_ai/llm/chat/tool/ToolSpec.cs:14,19` |
| 渐进式查询引擎（select/map/list_groups/keyword 四级） | `kit/mcp/skill/ToolSearchEngine.cs` |
| 提示词教导 AI 用渐进式语法 | `ToolsSection.cs:32-33` |
| 两阶段加载（FilterDeferredTools） | `AnthropicQueryService.cs:138-161` |
| -h 多级渐进式展开（options/sub/env/exit/examples/分类/命令） | `ApplicationBuilder.cs:368-432` |
| 桌面情景模式（look/zoom/detect/click + suggested_next） | `DesktopSceneLookToolHandlers.cs` 等 |
| 动态工具帮助（GhToolHelpRenderer 从 schema 实时渲染） | `GhSubCommand.cs:107,117` |
| 切词器（CoarseSplit SIMD + FmmTokenize + AhoCorasick） | `DynamicKeywordConfig.cs:47-182` |
| 工具搜索引擎（KeywordSearch + 评分 + Span 零分配） | `ToolSearchEngine.cs:122-179` |
| 模糊搜索（FuzzyMatch 默认 0.6 阈值） | `ReferenceResolutionOptions.cs:37,42` |
| mcp_search 命令（select/map/list_groups/关键词） | `McpCommand.cs:62-91` |

## 缺口清单

### GAP-040-01 通用 scenario registry + -h 顶层暴露 ⭐ P1 ✅ 已完成

- **当前状态**：✅ 已落地（2026-10-09）— 通用 scenario registry + 源码生成器 + `-h scenario` 入口
- **实现**：
  1. ✅ `[Scenario]` 特性 + `ScenarioInfo` record（`lib/abstractions/abs_core/core_attributes/scenario/`）
  2. ✅ `gen/scenario.generator/` 源码生成器扫描 `[Scenario]` 生成 `ScenarioRegistry` 静态注册表（继承 `AttributeRegistrationGeneratorBase<T>`）
  3. ✅ `ApplicationBuilder.ShowHelp` 增加 `scenario` 主题：`jcc -h scenario` 列出所有；`jcc -h scenario <name>` 展示详情
  4. ✅ `-h` 顶层置顶 scenario 入口（在 options/sub 之前）
  5. ✅ `GetHelpTopic` 支持多级 topic（`jcc -h scenario desktop`）
- **验收标准**：
  - ✅ `jcc -h scenario` 列出所有情景模式（含桌面场景）— E2E 验证通过
  - ✅ `jcc -h scenario desktop` 展示 look→zoom→detect→click 流程 — E2E 验证通过
  - ✅ 新增情景模式只需加 `[Scenario]` 特性，无需改 registry — 源码生成器自动收集
  - ✅ 单元测试 3 个通过（`ScenarioRegistryTests`）
- **复杂度**：中

### GAP-040-02 统一 ToolMenu 抽象层 ⭐ P3 ✅ 已完成

- **当前状态**：✅ 已落地（2026-10-09）— IToolMenu 接口 + ToolMenuRenderer 统一渲染器
- **实现**：
  1. ✅ `IToolMenu` 接口（`lib/abstractions/abs_core/core_attributes/scenario/IToolMenu.cs`）— 菜单提供者抽象
  2. ✅ `ToolMenuRenderer` 统一渲染器（`kit/hands/scenarios/ToolMenuRenderer.cs`）— ToJson（AI 消费）+ ToText（人类可读）
  3. ✅ `DesktopSceneMenuToolHandlers` 实现 `IToolMenu`，`SceneMenuAsync` 用 `ToolMenuRenderer.ToJson` 替代手写 JSON
  4. ✅ `ShowScenarioDetail` 用 `ToolMenuRenderer.ToText` 替代手写终端输出
  5. ✅ `ScenarioMenuJsonContext` 配置 `UnsafeRelaxedJsonEscaping` 不转义中文
- **验收标准**：
  - ✅ 所有菜单输出经统一渲染器，格式一致 — ToJson/ToText 统一
  - ✅ 单元测试 5 个通过（`ToolMenuRendererTests` + `DesktopSceneMenuToolHandlersTests`）
- **复杂度**：中

## 优先级汇总

| 优先级 | 缺口 | 复杂度 | 状态 |
|--------|------|--------|------|
| P1 | GAP-040-01 通用 scenario registry + -h 置顶 | 中 | ✅ 已完成 |
| P3 | GAP-040-02 统一 ToolMenu 抽象层 | 中 | ✅ 已完成 |

## 扩充情景模式（2026-10-09）

除 desktop 外，新增 7 个情景模式（仅需加 `[Scenario]` 特性，生成器自动收集），共 8 个：

| 情景模式 | handler | 工具集 |
|----------|---------|--------|
| build | BuildOutputToolHandlers | build_queue_status/build_cancel |
| desktop | DesktopSceneMenuToolHandlers | look/zoom/detect/click/type/drag |
| environment | EnvironmentToolHandlers | get_environment_state/wait_for_idle/undo_last_action/get_operation_history |
| error_fix | ErrorRecoveryToolHandlers | diagnose_error/fix_file_error/fix_shell_error/fix_merge_conflict |
| macro | MacroToolHandlers | start_recording/stop_recording/play_macro/list_macros |
| observation | ObservationToolHandlers | start_observation/learn_from_observation/optimize_steps/reproduce_from_logic |
| process | ProcessToolHandlers | list_processes/start_process/wait_for_idle/kill_process |
| window | WindowManagementToolHandlers | list_windows/focus_window/move_window/close_window/screenshot |

## 关联

- 设计文档：《Agent 工具设计》"海量的MCP工具"第2、3、4条
- 关联 ADR：[0019](../adr/0019-enum-enumvalue-source-generator.md)（[EnumValue]+源码生成器）、[0020](../adr/0020-encapsulation-requirements.md)
- 关联记忆：`能计算就计算不要构造表；真要建表用特性或 map 形式`
