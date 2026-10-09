# TASK039 — Agent 工具设计缺口：参数错误提示

> 来源：《Agent 工具设计》文档"首次调用优化"第3、4条 + "海量的MCP工具"第1条
> 检索日期：2026-10-09

## 背景

设计要求：参数宽容和转换（用转换表，不用白名单，穿透到处理器+守卫）；错误提示诱导 AI 下一个工具调用；可能性错误列 表单+调查工具；参数名+参数类型都提示。

## 已落地部分

| 设计点 | 落地位置 |
|--------|----------|
| 长选项别名转换表（14 工具组） | `GhCommandResolver.cs:357-403` LongAliasMap |
| 短选项别名表 + 短选项映射表 | `GhCommandResolver.cs:449-549` |
| 穿透架构（各子命令 DetectUnknownOptions 自守卫） | `FlatSubCommandRouter.cs:105-121` |
| 连字符→下划线宽容 | `GhCommandResolver.cs:275-276` |
| key=value 不带 -- 宽容 | `GhCommandResolver.cs:251-253` |
| 布尔值多种写法宽容 | `GhCommandResolver.cs:291` |
| --json field1,field2 转 --json_fields | `GhCommandResolver.cs:142-153` |
| JSON 修复（LlmJsonHelper.RepairJson） | `McpCommand.cs:263-282` |
| 工具名归一化（RepairToolName） | `misc/ToolNameResolver.cs:42-54` |
| "你是不是想用 --xxx?" 诱导 | `GhCommandResolver.cs:709-715` SuggestOption |
| 工具名建议 + 下一步引导 | `McpCommand.cs:21-24,99-104` |
| 模糊匹配（精确>前缀>子串>Levenshtein） | `ToolNameResolver.cs:103-140` |
| Rust 风格报错（行列+箭头） | `CliStructuredError.cs:43-86` |
| 错误码注册表（Code+Message+Hint+Retryable） | `CliErrorCatalog.cs` |
| 可能性错误表单（编号+原因+调查命令） | `GitHubRunLogFilterRunner.cs:479-502` BuildZeroMatchHint |
| 下一步提示常量（7 场景） | `GitHubRunLogHints.cs` |
| 错误诱导正确写法 | `GhCommandResolver.cs:690-704` |

## 缺口清单

### GAP-039-01 参数类型提示未展示 ⭐ P1

- **当前状态**：部分落地（`GhParam` 已捕获 `IsBoolean` 类型信息，`GhParamSchemaParser:201-216` 解析了 boolean 类型，但错误文案未展示完整类型）
- **缺什么**：设计要求"错误和缺失的参数要提示参数名称+参数类型"，当前 `UnknownOptionError`/`MissingPositionalError`/`ArgMissingRequired` 只显示参数名，未显示类型（integer/string/boolean/array/object）
- **证据**：
  - `GhCommandResolver.cs:710` UnknownOptionError 只列 `--{p.Name}`
  - `GhCommandResolver.cs:717-719` MissingPositionalError 只显示 `<{s.Name}>`
  - `CliErrorCatalog.cs:125-129` ArgMissingRequired 只显示 `缺少必需参数: {paramName}`
- **建议方案**：
  1. `GhParam` 增加 `TypeHint` 字段（从 schema `type` 字段提取，已部分有 IsBoolean）
  2. `UnknownOptionError` 改为 `--{p.Name}({p.TypeHint})`
  3. `MissingPositionalError` 改为 `缺少必需参数: {s.Name} (类型: {s.TypeHint})`
  4. `ArgMissingRequired` 同步加类型
- **验收标准**：
  - `gh pr view --bad` 报错列出 `--name(string), --number(integer), --json_fields(array)...`
  - 缺少必需参数时显示 `缺少必需参数: pr_number (类型: integer)`
- **复杂度**：低

### GAP-039-02 反向人格压制（鼓励探索安全区外） ⭐ P3

- **当前状态**：未落地（现有人格压制偏向"禁止滥用"，缺"鼓励探索安全区外"反向压制）
- **缺什么**：设计文档"AI 不喜欢选工具，训练数据注定不敢用安全区外工具，需要围绕模型进行人格压制"——当前 `ToolsSection.cs:17` 只说"不要滥用 Bash"，没有"专用工具优先，安全区外工具可以用不要怕"的引导
- **建议方案**：在 `ToolsSection` 追加反向压制提示词："专用工具优于 Bash，但遇到专用工具无法覆盖的场景时，Bash/系统工具可以用，不要因为训练数据习惯而回避"
- **验收标准**：ToolsSection 输出含双向压制（禁止滥用 + 鼓励合理使用）
- **复杂度**：低

### GAP-039-03 脚本触发动态备份提示 ⭐ P2

- **当前状态**：未落地（现有备份提示是静态 `ReplacementMethodologySection.cs:33`"每次替换前必须 git 备份"，非脚本触发时动态注入）
- **缺什么**：设计要求"每次触发脚本就系统提示词让它自行备份或推荐用户备份"。文件级备份机制完备（`FileHistoryService.BackupBeforeWriteAsync` 写前自动备份），但"触发脚本→提示备份"的提示词注入链路缺失
- **建议方案**：
  1. 在 Bash 工具执行中间件检测命令是否含破坏性动作（rm/mv/Move-Item/del/格式化/重定向覆盖等）
  2. 命中时通过 `SystemReminderManager.AddReminderAsync` 注入一次性备份提示
  3. 用 `CooldownService.ShouldTrigger("bash-destructive-hint", 5min)` 避免重复注入
- **验收标准**：AI 调 Bash 执行 `rm` 类命令时，下次调用前注入"建议先 git stash 备份"提示；5 分钟内不重复
- **复杂度**：中

## 优先级汇总

| 优先级 | 缺口 | 复杂度 |
|--------|------|--------|
| P1 | GAP-039-01 参数类型提示 | 低 |
| P2 | GAP-039-03 脚本触发动态备份提示 | 中 |
| P3 | GAP-039-02 反向人格压制 | 低 |

## 关联

- 设计文档：《Agent 工具设计》"首次调用优化"第3、4条、"海量的MCP工具"第1条
- 关联 ADR：[0080](../adr/0080-manual-exe-testing-guide.md)
- 关联记忆：`jcc 参数设计要贴合 AI 运行时调用习惯并宽容处理错误`
