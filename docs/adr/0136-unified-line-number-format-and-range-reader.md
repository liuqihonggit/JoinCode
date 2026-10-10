# ADR 0136: 统一行号格式化与偏移读取到 LineNumberFormatter + LineRangeReader

## 状态

proposed

## 上下文

项目有 4 个场景使用行号前缀（FileRead、gh 日志、rg/grep、BuildOutput），行号格式和偏移读取逻辑分散在各工具中：

**行号格式问题**（已修复，commit `0395ea254`）：

| 场景 | 修复前格式 | 修复后 |
|------|-----------|--------|
| FileRead | `1\tcontent` / `     1→content` | ✅ `LineNumberFormatter` |
| gh 日志（4 处） | `42: content`（冒号+空格） | ✅ `LineNumberFormatter` |
| `SkipAndTruncate` | `1\tcontent`（硬编码 `\t`） | ❌ 未用 `LineNumberFormatter` |
| rg/grep | `path:42:content`（rg 原生） | ✅ 无需改 |

**偏移读取逻辑重复**：

| 函数 | 位置 | 职责 | 行号格式化 | 续读提示 |
|------|------|------|-----------|---------|
| `SkipAndTruncate` | `GitHubRunLogFilter.cs:79` | 截断+格式化+提示 | ❌ 硬编码 `\t` | ✅ |
| `ApplyLimit<T>` | `SearchService.cs:603` | 纯截断列表 | 无 | 无 |
| FileRead offset | `FileToolHandlers.Read.cs` | FileReader 读取范围 | ✅ | 无 |
| gh `StreamAndFilter` | `GitHubRunLogFilterRunner.cs` | 流式截断+格式化 | ✅ | ✅ |

**根因**：行号格式化和偏移读取是横切关注点，但散落在 4 个工具中各自实现，违反 AGENTS.md 第 10 条"提取重复为 LINQ 链式"和第 11 条"硬编码变委托"。

## 决策

采用 **两层工具类** 方案：

### 1. `LineNumberFormatter`（已实现，commit `0395ea254`）

- `Format(lineNumber, content, compact)` — 单行格式化（流式日志场景）
- `FormatMultiLine(content, startLine, compact)` — 多行格式化（FileRead 场景）
- 紧凑模式 `行号\t内容` / 箭头模式 `行号→内容`（padStart 6）
- 配置开关：`FileOperationConfig.CompactLinePrefix`

### 2. `LineRangeReader`（待实现）

- `Slice(lines, skip, max) → (range, hasMore, nextSkip)` — 纯截断，不格式化
- `Read(lines, skip, max, compact) → LineRangeResult` — 截断 + 行号格式化 + 续读提示
- 统一续读提示格式：`... [共 N 行，显示第 X-Y 行。用 skip_lines=Z 续读后续行]`

### 委托关系

```
SkipAndTruncate → LineRangeReader.Read（修复硬编码 \t）
ApplyLimit      → LineRangeReader.Slice（统一截断逻辑）
gh StreamAndFilter 截断片段 → LineRangeReader.Read
FileRead offset+limit → LineRangeReader.Slice（截断后由 AddLineNumbers 格式化）
```

## 替代方案（考虑过但放弃）

| 方案 | 优点 | 缺点 | 放弃原因 |
|------|------|------|---------|
| B: 仅修复 SkipAndTruncate 硬编码 \t | 最小改动 | 不统一偏移读取逻辑，重复仍在 | 治标不治本 |
| C: 各工具独立修复 | 不引入新抽象 | 4 处重复维护、格式不一致风险 | 违反 DRY |
| D: 泛型管道中间件 | 最灵活 | 过度工程化、行号格式化不是管道职责 | 杀鸡用牛刀 |

## 实施过程

| 步骤 | 内容 | commit |
|------|------|--------|
| Phase 1 | 创建 `LineNumberFormatter` + FileRead/gh 日志委托 | `0395ea254` |
| Phase 2 | 创建 `LineRangeReader` + `SkipAndTruncate` 委托 | 待实现 |
| Phase 3 | `ApplyLimit` 委托给 `LineRangeReader.Slice` | 待实现 |
| Phase 4 | gh `StreamAndFilter` 截断片段统一 | 待实现 |

## 后续

- `LineRangeReader` 实现后，ADR 状态改为 `accepted`
- AGENTS.md"好代码一键清单"中新增 `LineRangeReader.Read` 作为偏移读取推荐模式
