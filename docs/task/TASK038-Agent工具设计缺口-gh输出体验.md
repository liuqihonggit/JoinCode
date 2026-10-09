# TASK038 — Agent 工具设计缺口：gh 输出体验

> 来源：《Agent 工具设计》文档"首次调用优化"第2条 + "行号和分页"整节
> 检索日期：2026-10-09
> 检索方式：5 个 explore 子代理并行检索 jcc 代码库

## 背景

AI 使用 gh 工具查 CI 错误时翻查层级多、`gh view` 偶发卡死。设计要求：置顶错误、按需拉取、行号定位、分页不断半句、多阅读模式、并行下载缓存、数据清洗剔除 ANSI。

## 已落地部分（无需改动）

| 设计点 | 落地位置 |
|--------|----------|
| 置顶错误 + pass 折叠 | `kit/mcp/git_hub/GitHubToolHandlers.Pr.cs:400-422`、`GitHubToolHandlers.Run.cs:295-386` |
| 渐进式披露"尚未拉取" | `GitHubToolHandlers.Run.cs:378` |
| SectionOrder 优先级 | `GitHubRunLogFilter.cs:56-63` |
| 分页不断半句（TruncateAtNewline） | `kit/hands/tool_handlers/core/services/ToolResultTruncator.cs:36-47` |
| 阅读模式三档 verbosity | `GitHubToolHandlers.cs:114-119`（0=gh风格/1=精简JSON/2=完整JSON） |
| 并行下载（Channel + SemaphoreSlim=8） | `GitHubRunLogFilterRunner.cs:116-162` |
| 持久化缓存（IKvStore/LSM-Tree, TTL=7天） | `GitHubRunLogFilterRunner.cs:11-249` |
| 优先拉取错误部分（GetFailedJobLogsAsync） | `GitHubRunLogFilterRunner.cs:37-46` |
| 逐级展开提示（RunListFailureHint 四级） | `GitHubRunLogHints.cs:10-15` |
| 日志格式化（ParseLine 状态机 + Span 零 GC） | `GitHubLogParser.cs:18-64` |
| 剔除 pass 噪声（markers 过滤 + AsParallel） | `GitHubRunLogFilter.cs:101-116` |
| 历史构造错误链路（NoStackTraceHint） | `GitHubRunLogHints.cs:62-66` |
| 截断分页诱导（skip_lines 续读） | `GitHubRunLogFilter.cs:79-96` |

## 缺口清单

### GAP-038-01 剔除 ANSI 颜色 ⭐ P0 ✅ 已完成(0adf4326f)

- **当前状态**：未落地
- **缺什么**：GitHub Actions 日志（dotnet test 彩色输出、npm 彩色输出）含 ANSI 转义序列（`\x1b[31m`等），当前未清洗，污染 LLM 上下文、浪费 token
- **证据**：全代码库搜 `StripAnsi`/`RemoveAnsi`/`AnsiRegex`/`\x1b`/`\u001b` 无果；现有 `TerminalColors`/`AnsiStyleEnumConstants` 是**输出端添加**颜色，不是**清洗日志中已有**的 ANSI
- **建议方案**：
  1. 在 `kit/mcp/git_hub/` 新增 `AnsiStripper.cs`，用 `Span<char>` 零 GC 实现，正则 `\x1b\[[0-9;]*m` 或手写状态机
  2. 在 `GitHubLogParser.ParseLine` 入口和 `GitHubApiClient.ReadLogStreamLinesAsync` 入口统一调用
  3. 单元测试：覆盖 SGR/光标移动/擦除/256色/真彩色序列
- **验收标准**：
  - 含 ANSI 的日志经清洗后纯文本
  - 不含 ANSI 的日志原样通过（零开销）
  - Span 零 GC（用 `MemoryExtensions.IndexOf` 定位 `\x1b`）
- **复杂度**：低

### GAP-038-02 gh 输出加注行号 ⭐ P1

- **当前状态**：部分落地（仅 read 工具 `FileToolHandlers.AddLineNumbers:85` 实现，gh 输出未加注）
- **缺什么**：`gh run view` / `gh pr checks` 输出未加注行号，AI 难以定位具体行
- **建议方案**：
  1. 抽取 `AddLineNumbers` 为公共扩展方法（`kit/hands/tool_handlers/core/services/`）
  2. 在 `GitHubRunLogFilter.SkipAndTruncate` 输出时加行号前缀
  3. 在 `GitHubToolHandlers.Pr.cs` pr checks 输出时加行号
- **验收标准**：gh run view --log 输出每行带 `N→` 前缀；行号连续；可被 `read` 工具的 `StripLineNumberPrefixes` 剥离
- **复杂度**：中

### GAP-038-03 阅读模式系统变量切换 ⭐ P2

- **当前状态**：部分落地（verbosity 是工具参数，每次调用传入；全局 `JCC_OUTPUT_FORMAT` 只控 text/json 二档）
- **缺什么**：设计要求"系统变量切换"三档（默认/精简json/完整json），当前 `JCC_OUTPUT_FORMAT` 不覆盖 gh 的 verbosity 三档
- **建议方案**：扩展 `JCC_OUTPUT_FORMAT` 接受 `gh_style`/`compact_json`/`full_json` 三值，`FormatGhOutput` 优先读环境变量作默认值
- **验收标准**：设 `JCC_OUTPUT_FORMAT=compact_json` 后 `gh run view` 不传 verbosity 也输出精简 JSON
- **复杂度**：低

### GAP-038-04 行号定位跳转 ⭐ P2

- **当前状态**：未落地（只有 `skip_lines` 偏移分页，无行号直接跳转）
- **缺什么**：设计要求"提供行号定位功能"，给定行号直接跳转到该行
- **建议方案**：gh run view 新增 `start_line=N` 参数，等价于 `skip_lines=N-1`，但语义更直观
- **验收标准**：`gh run view <id> --start_line 100` 从第 100 行开始输出
- **复杂度**：中

### GAP-038-05 格式识别失败时截断分页 ⭐ P2

- **当前状态**：部分落地（`BuildZipDecompressionError` 直接 yield break 终止 + 建议系统命令，非截断分页）
- **缺什么**：设计要求"格式无法识别时用截断分页，诱导 AI 使用分页工具阅读"
- **建议方案**：格式识别失败时，把原始字节前 N KB 截断输出（hex dump 形式），附"用 skip_lines=N 续读"提示
- **验收标准**：ZIP 解压失败时仍输出前 4KB hex dump + 分页提示，不直接终止
- **复杂度**：低

### GAP-038-06 pr checks 独立"警告"档 ⭐ P3

- **当前状态**：部分落地（pr checks 只有 fail/pending/skipping/pass 四档，无独立"警告"档）
- **缺什么**：设计要求"总结 > 错误 > 警告 > 通过"四档，pr checks 缺警告档
- **建议方案**：`CheckSortKey` 增加 `warning=0.5` 档（介于 fail=0 和 pending=1 之间），GitHub check conclusion=`stale`/`neutral` 归入警告档
- **验收标准**：pr checks 输出顺序为 fail → warning → pending → skipping → pass
- **复杂度**：低

### GAP-038-07 run view job 列表"总结"档 ⭐ P3

- **当前状态**：部分落地（job 列表置顶只有"失败/取消/进行中"三档，无独立"总结"档；总结在 baseSummary 头部非 SectionOrder 项）
- **缺什么**：设计要求"总结"作为最高优先级档独立展示
- **建议方案**：`SectionOrder` 增加 `SectionSummary => -1`（比 SectionError=0 更高优先级），把 baseSummary 归入 SectionSummary
- **验收标准**：run view 输出最顶部是总结行，其次是错误，最后是通过
- **复杂度**：低

## 优先级汇总

| 优先级 | 缺口 | 复杂度 |
|--------|------|--------|
| P0 | GAP-038-01 剔除 ANSI 颜色 ✅ | 低 |
| P1 | GAP-038-02 gh 输出加注行号 | 中 |
| P2 | GAP-038-03 阅读模式系统变量 | 低 |
| P2 | GAP-038-04 行号定位跳转 | 中 |
| P2 | GAP-038-05 格式识别失败截断分页 | 低 |
| P3 | GAP-038-06 pr checks 警告档 | 低 |
| P3 | GAP-038-07 run view 总结档 | 低 |

## 关联

- 设计文档：《Agent 工具设计》"首次调用优化"第2条、"行号和分页"整节
- 关联 ADR：[0080](../adr/0080-manual-exe-testing-guide.md)（手动 exe 验收）
- 关联记忆：`gh 日志功能全量缓存策略`、`CI 排查输出按异常优先级置顶`
