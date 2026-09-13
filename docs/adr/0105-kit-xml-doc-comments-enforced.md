# 0105. kit/ 工程强制 XML 文档注释完整 — CS1591 升为编译错误

- 状态：accepted
- 日期：2026-09-14
- 决策者：项目架构组

## 背景

### 问题起源

kit/ 下各工程（composition、pipelines、prompts、mcp_tool_dispatch、mcp、slash、brain、hands）的 public 类型和方法大量缺少 XML 文档注释。根目录 `Directory.Build.props` 全局 NoWarn 包含 CS1591，导致缺少注释不报错，IntelliSense 信息丢失、调用方无法理解意图、AI 生成代码时容易顺手删掉注释造成不可逆信息损失。

### 现状

- 全局 `TreatWarningsAsErrors=true` 已启用，但 CS1591 被 NoWarn 屏蔽
- kit/ 七个工程中 public 成员注释覆盖率参差不齐
- 生成器生成的代码（如 PromptSection.Generator 输出的枚举/分析器）也缺注释

## 决策

### 1. 每个工程 csproj 强制开启 XML 文档生成 + 移除 CS1591 屏蔽

在每个 kit/ 工程的 csproj 中加入：

```xml
<PropertyGroup>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  <DocumentationFile>$(OutputPath)$(AssemblyName).xml</DocumentationFile>
  <NoWarn>$(NoWarn.Replace('CS1591;', '').Replace(';CS1591', '').Replace('CS1591', ''))</NoWarn>
</PropertyGroup>
```

配合全局 `TreatWarningsAsErrors=true`，CS1591（缺少对公共可见类型或成员的 XML 注释）升为编译错误。

### 2. 注释范围 — public + protected 均需注释

CS1591 覆盖所有公共可见成员，包括：
- public class/record/struct/enum/delegate
- public 方法、属性、字段、事件
- public 枚举值
- **protected 成员**（对派生类可见，算公共可见）
- protected internal 成员

internal/private 成员不需要 XML 注释。

### 3. 生成器模板必须加注释

源码生成器（如 `PromptSection.Generator`、`McpToolDispatch.Generator`）生成的 public 代码需在生成模板中加 `/// <summary>` 注释，否则消费方工程编译报 CS1591。

### 4. 注释风格

- **简体中文**
- 特性行在 class 上方时，注释插在特性行之前（顺序：注释→特性→class）
- 方法参数用 `/// <param name="xxx">说明</param>`
- 方法返回值用 `/// <returns>说明</returns>`

### 5. 大规模工程交给 subagent 分组处理

kit/mcp（131 文件、1462 个错误）等大规模工程，主代理先编译统计错误分布并按目录分组，再派多个 subagent 并行修复。subagent 只执行修复，不做统计/编译/git 操作。

## 替代方案（考虑过但放弃）

### A. 仅在 Release 模式强制（Debug 宽容）

放弃。Debug 模式不报错会导致开发者本地编译通过但 CI 失败，反馈链太长。且本地 IntelliSense 也依赖 XML 注释，Debug 模式应同样生成。

### B. 用 Roslyn 分析器自定义规则替代 CS1591

放弃。CS1591 是编译器内置规则，零成本启用；自定义分析器需额外开发维护，且无法比内置规则更准确。

### C. 逐工程渐进式开启（不一次性全量）

部分采纳。实施按工程规模从小到大推进（composition→pipelines→prompts→mcp_tool_dispatch→mcp→…），但每个工程一旦开启就必须 100% 注释完整，不留半完成状态。

## 影响

- **IntelliSense 恢复**：所有 public 成员有中文文档提示，调用方一目了然
- **AI 生成代码防护**：AI 不会顺手删注释（删了编译报错），信息损失可逆
- **API 文档生成**：XML 文件可被 DocFX/Sandcastle 等工具消费生成独立 API 文档
- **新增 public 成本**：每次新增 public 成员必须同时写注释，轻微增加编码成本
- **生成器维护成本**：生成器模板修改时需同步更新生成代码的注释

## 验证

- ✅ kit/composition — 编译 0 错误 0 警告（commit 3cedb7fab）
- ✅ kit/pipelines — 编译 0 错误 0 警告（commit 85edb92e3）
- ✅ kit/prompts — 编译 0 错误 0 警告（commit 2b15d0fa0）
- ✅ kit/mcp_tool_dispatch — 编译 0 错误 0 警告（commit 74eb34c45）
- ✅ kit/mcp — 编译 0 错误 0 警告（commit b596f5aee）
- ⏳ kit/slash — 待处理
- ⏳ kit/brain — 待处理
- ⏳ kit/hands — 待处理

### llm/ 工程（沿用本决策，2026-09-14）

llm/ 工程不在 kit/ 范围内，但沿用本 ADR 决策强制 XML 注释完整。`llm/Directory.Build.props` 已配置 `GenerateDocumentationFile=true`（Debug 模式），各 csproj 通过 `NoWarn.Replace` 移除继承的 CS1591 屏蔽并升级为错误。

- ✅ llm/core — 0 缺漏（原有注释已完整）
- ✅ llm/agents — 662 处缺漏全部补全，编译 0 错误 0 警告（commit 685e9138b）。4 子代理并行补全 613 处 + 主代理兜底 58 处（含 override/protected 字段/枚举值，子代理分析脚本漏掉的类别）
- ⏳ llm/reasoning — 304 处缺漏，待处理
