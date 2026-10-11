# ADR 0135: WellKnownParam 枚举 + 源码生成器消除参数描述重复

## 状态

accepted

## 上下文

项目有 231 个 MCP 工具、873 个参数，其中 869 处 `[McpToolParameter("描述文本")]` 的描述是手写字符串字面量。源码生成器（`mcp_tool_dispatch.generator`）只透传描述文本，不做去重或校验。

**重复最严重的描述**：

| 次数 | 描述文本 | 变体数 |
|------|---------|--------|
| 104 | `工作目录(可选)` | 2 种变体 |
| 96 | `仓库(可选,默认当前仓库)` | 3 种变体 |
| 33 | `输出档位(0=gh风格...)` | 4 种变体 |
| 17 | `数量限制(默认 30)` | 3 种默认值变体 |

**根因**：无统一数据源，每个工具独立手写描述，违反 AGENTS.md 第 11 条"硬编码变委托"和枚举唯一数据源原则。

**未来扩展瓶颈**：rg/mcp 等命令参数规模将超过 gh 现有 145 工具/732 参数，当前架构下新增参数需手写描述，容易产生新的不一致变体。

## 决策

采用 **源码生成器 + WellKnownParam 枚举** 方案：

1. **WellKnownParam 枚举** — 定义 30+ 个公共参数，每个用 `[EnumValue]` 绑定参数名、`[ParamMeta]` 绑定描述/必填/默认值
2. **param_metadata.generator** — 新源码生成器，扫描 `WellKnownParam` 枚举生成 `WellKnownParamDescriptions`（描述常量）和 `WellKnownParamMeta`（元数据字典）
3. **增强 `[McpToolParameter]`** — 新增 `McpToolParameterAttribute(WellKnownParam)` 构造重载，生成器编译期从 `[ParamMeta]` 自动填充描述
4. **`mcp_tool_dispatch.generator` 增强** — 扫描到 `WellKnown` 构造参数时，编译期从 `WellKnownParam` 枚举的 `[ParamMeta]` 特性内联描述文本

## 替代方案（考虑过但放弃）

| 方案 | 优点 | 缺点 | 放弃原因 |
|------|------|------|---------|
| B: 运行时字典查找 | 实现简单 | 违反 AOT、运行时开销、无编译期校验 | AOT 兼容性是硬约束 |
| C: 常量类手写 | 最简单 | 无生成器校验、仍需手写引用、无法自动填充 | 不满足"自动填充"需求 |
| D: 分析器强制 + 手写修复 | 不改架构 | 869 处手写修复、无自动填充、治标不治本 | 无法消除重复 |

## 实施过程

| 步骤 | 内容 | commit |
|------|------|--------|
| Phase 1 | 创建 `WellKnownParam` 枚举(30+参数) + `[ParamMeta]` 特性 | `b7b8bc5d2` |
| Phase 2 | 创建 `param_metadata.generator` 生成器 | `b7b8bc5d2` |
| Phase 3 | 增强 `[McpToolParameter]` + 修改 `McpToolDispatchGenerator` | `8341442a7` |
| Phase 6 | 批量替换 276 处参数描述（13 个 gh 文件） | `e512a80` |

## 后续

- Phase 4: 提取 `GitHubCommonOptions` 参数组模板（用 `[McpToolOptions]` 减少方法签名参数数量）
- Phase 5: 统一 rg 子命令参数定义（消除手写 switch-case）
- 编译期校验分析器规则：扫描 `[McpToolParameter("...")]` 字符串字面量，如果参数名匹配 WellKnown 但描述不一致则发出 warning

## 影响

- 276 处手写参数描述消除，统一为 `WellKnownParam` 枚举引用
- working_dir/repo/verbosity/limit 的所有不一致变体已统一
- 新增公共参数只需在 `WellKnownParam` 枚举添加一个成员，所有工具可复用
- AOT 兼容：生成器用 netstandard2.0 编译期运行，生成代码用 FrozenDictionary，无反射
