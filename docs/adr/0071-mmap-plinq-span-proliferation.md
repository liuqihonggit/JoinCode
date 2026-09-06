# 0071. mmap + PLINQ + 零 GC Span 技术推广 — 从 RgEngine 到全项目文件遍历

- 状态：accepted
- 日期：2026-09-07
- 决策者：项目架构组

## 背景

ADR 0070 在 `RgEngine` 中实现了 mmap 零拷贝 + PLINQ 并行 + Span 行遍历零 GC 的搜索引擎，实测验证有效。分析全项目发现 40+ 处文件遍历/搜索热点可应用相同技术。

## 决策

### 决策1：MappedFileReader 封装

**选择**：创建 `MappedFileReader`（Abstractions 层）封装 `MemoryMappedFile` + `MemoryMappedViewAccessor`，实现 IDisposable，调用方用 `using var`（NET10 单行）释放，编译器自动展开为 try-finally。

**设计要点**：
- 构造函数直接创建资源，无工厂方法 + try-catch（编译器保证 using 释放）
- `FileInfo.Length` 获取文件大小（只读 metadata），mmap 只打开一次文件
- 空文件不创建 mmap（BCL 限制），mmf/accessor 为 null
- `FileShare.Read` 共享锁（mmap 只读映射）

### 决策2：P0 — PhysicalFileSystem 底层优化（最高杠杆点）

**选择**：在 `PhysicalFileSystem` 的 `ReadAllText` / `ReadAllLines` / 异步版本内部，UTF-8 用 `MappedFileReader` + `LineSpanIndexer`，其他编码走原路径。

**收益**：全项目 62+ 调用方自动受益，无需逐个改造。

### 决策3：P1 — 流式读行改造

**选择**：`SnipLogic` / `FileEditLogic` / `FileEditor` / `FileReader` 直接用 `StreamReader` + `while(ReadLineAsync)` 绕过了 `PhysicalFileSystem`，需单独改造。

**FileReader 特殊处理**：通过 `IFileSystem` 抽象访问文件（可能是 `InMemoryFileSystem`），`MappedFileReader` 只能读真实磁盘文件。解决方案：`FileReader` 改用 `_fs.ReadAllTextAsync` + `LineSpanIndexer`，通过抽象自动获得 mmap（`PhysicalFileSystem` 已 P0 改造），`InMemoryFileSystem` 走内存读取。

### 决策4：P2 — Split 行遍历 Span 化

**选择**：`content.Split('\n')` 改用 `LineSpanIndexer.BuildLineRanges(content.AsSpan())`，避免 `string[]` + 每行 `string` 分配。

**改造点**：
- `ContextCollapseService` 3处 Split
- `ApplyPatchLogic.ParsePatch` 1处 Split

### 决策5：P2-3 ProgressiveDisclosureService 并行化 — 跳过

**选择**：只有3个文件，并行化收益有限，代码复杂度增加不值得。

## 完成的改造

| 改造点 | 技术 | commit |
|--------|------|--------|
| MappedFileReader 封装 | IDisposable + using var | `c62c2864e` |
| P0: PhysicalFileSystem | mmap + LineSpanIndexer | `f882b9b86` |
| P1: SnipLogic | mmap + Span 行遍历 | `d7b785bfe` |
| P1: FileEditLogic | mmap + Span 行遍历 | `f3d59c3f5` |
| P1: FileEditor | mmap + Span 行遍历 | `7e93973c9` |
| P1: FileReader | _fs.ReadAllTextAsync + LineSpanIndexer | `1665cf7af` |
| P2: ContextCollapseService | LineSpanIndexer 替代 Split | `c6311dc71` |
| P2: ApplyPatchLogic | LineSpanIndexer 替代 Split | `edfb44133` |

## 后果

### 正面

- **大文件零拷贝**：mmap 按需分页，不立即分配物理内存
- **零分配行遍历**：LineSpanIndexer 只分配 `List<(int,int)>`，不分配每行 `string`
- **全项目受益**：P0 底层优化覆盖 62+ 调用方

### 负面

- **mmap FileShare.Read**：不允许并发写入，文件被独占锁定时 mmap 失败
- **Span 不能跨 await**：需在同步块内完成处理
- **全量读入内存**：mmap + LineSpanIndexer 把整个文件读入 string，原 StreamReader 逐行读取

## 参考

- ADR 0070 — RgEngine 独立实现（mmap + PLINQ + 零 GC）
- `MappedFileReader` — Abstractions 层 mmap 封装
- `LineSpanIndexer.BuildLineRanges` — 零分配行范围构建
