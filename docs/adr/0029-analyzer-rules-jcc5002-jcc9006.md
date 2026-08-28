# 0029. 分析器铁律 JCC5002/JCC9006

- 状态：accepted
- 日期：2026-08-29
- 决策者：项目架构组

## 背景

项目有自定义 Roslyn 分析器，用于在编译期捕获特定性能和安全问题。需要明确这些分析器的规则和豁免范围。

## 决策

**两条分析器铁律**：

### JCC5002 — 循环内禁止 `+=` 拼字符串
- **规则**：循环内禁止用 `+=` 拼接字符串，流式追加用 `StringBuilder`
- **原因**：循环内 `+=` 每次创建新字符串对象，O(n²) 时间复杂度，GC 压力大
- **正确做法**：用 `StringBuilder.Append()` 或 `string.Concat()`

### JCC9006 — FileStream 必须用 FileShare.ReadWrite
- **规则**：`FileStream` 构造必须用 `FileShare.ReadWrite`（避免跨进程读写冲突）
- **豁免**：`PhysicalFileSystem`/`SafeFileIO` 已豁免
- **原因**：跨进程并发读写时 `FileShare.None` 会锁死，`FileShare.ReadWrite` 允许并发读写

## 替代方案

1. **不强制（仅建议）**：放弃。性能问题在运行时才暴露，编译期捕获成本更低。
2. **用 Roslynator 等第三方分析器**：放弃。不满足项目特定需求，且 AOT 兼容性未知。
3. **仅 JCC5002 不 JCC9006**：放弃。跨进程文件冲突同样重要。

## 后果

- 正面：编译期捕获性能和安全问题；代码质量高
- 负面：开发者需熟悉规则；豁免需显式标注
- 中性：分析器位于 `generators/AotSafety.Generator`（ADR 0001 第一层）
