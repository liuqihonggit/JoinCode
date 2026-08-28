# 0014. MCP 工具覆盖原则

- 状态：accepted
- 日期：2026-08-29
- 决策者：项目架构组

## 背景

项目已有 63 个 Handler 类、296 个 McpTool 方法、覆盖 53 个 ToolCategory。新增工具时需明确归属和实现规范，避免随意新增枚举值和重复造轮子。

## 决策

新增工具原则：
1. **新工具必须归属已有 ToolCategory 枚举值**，除非有充分理由新增枚举
2. **新增 ToolCategory 枚举值需同步更新 `ToolHypergraphPresets`**（如有关联工具链）
3. **优先用 `[McpTool]` + 源码生成器模式**，禁止手动实现 `IToolHandler`
4. **工具描述用中文**（对齐 ErrorRecoveryToolHandlers 风格）
5. **新增工具后必须更新 `ToolCategory` 枚举的 `[EnumValue]` 并全量重建**

## 替代方案

1. **允许自由新增 ToolCategory**：放弃。枚举值膨胀，难以管理，且破坏工具链预设。
2. **手动实现 IToolHandler**：放弃。绕过源码生成器的自动注册和 schema 生成，易出错。
3. **工具描述用英文**：放弃。与现有 ErrorRecoveryToolHandlers 风格不一致，且项目面向中文用户。

## 后果

- 正面：工具归属清晰；源码生成器自动注册；中文描述一致
- 负面：新增 ToolCategory 需全量重建（`--no-incremental`）
- 中性：当前 296 工具/53 Category，覆盖率持续跟踪
