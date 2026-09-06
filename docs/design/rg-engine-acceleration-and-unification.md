# RgEngine 加速方案与全局统一重构

> ADR: [0070](../adr/0070-rg-engine-mmap-plinq.md)
> 日期：2026-09-06

## 一、加速方案

### 1.1 mmap 零拷贝读取大文件

**原理**：`MemoryMappedFile` 把文件映射到进程虚拟内存，按需分页，不立即分配物理内存。

**阈值**：64KB
- 小文件（<64KB）：`File.ReadAllText`（创建 mmap 开销大于直接读）
- 大文件（≥64KB）：`MemoryMappedFile.CreateViewStream` 零拷贝读取

**收益**：
- 大文件不全量读入堆，减少 GC 压力
- Span 行遍历直接操作映射内存，零分配
- 操作系统按需分页，冷数据不占用物理内存

### 1.2 PLINQ 并行每文件

**原理**：`files.AsParallel().WithCancellation(ct).Select(f => SearchFile(...)).ForAll(...)`

**对比 `Task.WhenAll`**：
- `Task.WhenAll`：每个文件一个 Task，批量等待，适合 IO 密集
- PLINQ：内置分区 + 负载均衡，适合 CPU 密集（正则匹配是 CPU 密集）
- 文件数多时 PLINQ 分区更均匀，避免 Task 创建开销

**取消语义**：`WithCancellation(ct)` 在超时时抛 `OperationCanceledException`，调用方捕获返回 ExitCode 2。

### 1.3 零 GC Span 行遍历

**原理**：用 `ReadOnlySpan<char>` 按行遍历，不 `string.Split` 分配行数组。

**实现**：
```
contentSpan.IndexOf('\n') → 找行尾
contentSpan.Slice(start, length) → 取行（零分配）
regex.IsMatch(span) → 匹配
```

**收益**：`string.Split` 分配 `string[]` + 每行新 string，GC 压力大。Span 行遍历零分配。

### 1.4 .NET Regex SIMD 引擎

.NET 10 的 `Regex` 内部用 SIMD 优化匹配，`RegexOptions.Compiled` 编译为 IL，热路径性能接近原生。

### 1.5 性能压测数据

| 实现 | 首次耗时 | 二次耗时 | 匹配文件数 |
|------|----------|----------|-----------|
| jcc rg (Debug) | 303ms | 290ms | 1506 |
| 系统 rg (Rust 原生) | 410ms | 73ms | 1503 |

- 首次 jcc rg 更快（PLINQ 并行度高）
- 二次系统 rg 更快（Rust 原生无 JIT，文件系统缓存命中）
- Release + AOT 构建会显著改善 jcc rg 二次性能（消除 JIT 开销）

---

## 二、代码重复分析

### 2.1 重复实现清单

| 函数 | 重复程度 | 问题描述 |
|------|----------|----------|
| `BinaryExtensions` 集合 | **3 处** | RgEngine/SearchService 完全相同（44个），FileToolHandlers 是超集（80个） |
| `SimpleGlobMatch` | **6 处** | RgEngine 用 `Contains` 匹配，`*.cs` 误匹配 `foo.cs.txt` |
| `IsGitIgnored` | **2 处** | RgEngine 简化版不向上查找、不应用否定模式 |
| `VcsDirectories` 集合 | **4 处** | RgEngine/SearchService 相同，MarkdownWalker 超集 |
| `FileTypeExtensions` 集合 | **2 处** | RgEngine/SearchService 差异（格式和数量不同） |
| `CompileRegex` | **3 处** | RgEngine 最全，SearchService 简化版 |
| `EnumerateFiles` 骨架 | **2 处** | RgEngine/SearchService 相同骨架，不同过滤实现 |
| `lineRanges` 构建 | **2 处** | RgEngine/SearchContent 与 SearchService 完全相同 |

### 2.2 关键 Bug

**`SimpleGlobMatch` 用 `Contains` 匹配，语义不精确**：
- `*.cs` 会匹配 `foo.cs.txt`（`Contains(".cs")` 为真）
- 应替换为已有的公共 `GlobMatcher.IsMatch`

**`IsGitIgnored` 简化版语义不正确**：
- 用 `Contains` 匹配而非真正 glob
- 只读 `root` 目录下的 `.gitignore`，不向上查找父目录
- 跳过 `!` 否定模式而非应用（丢失 unignore 语义）
- 要求 `.git` 目录存在才检查（ripgrep 不要求）

### 2.3 已有公共工具类但未充分复用

| 公共类 | 位置 | 未复用的地方 |
|--------|------|-------------|
| `GlobMatcher` | `infrastructure/Infrastructure/Utils/Text/GlobMatcher.cs` | RgEngine、GlobRulesSection、InMemoryFileSystemWatcher |
| `GitignoreMatcher` | `infrastructure/Infrastructure/IO/Services/FileOps/GitignoreMatcher.cs` | RgEngine |

---

## 三、重构计划

### P0：修复 Bug + 消除最严重重复 ✅ 已完成

| 步骤 | 动作 | 影响文件 | 状态 |
|------|------|----------|------|
| P0-1 | `RgEngine.SimpleGlobMatch` → 委托 `GlobMatcher.IsMatch` | RgEngine.cs | ✅ |
| P0-2 | `RgEngine.IsGitIgnored` → 委托 `GitignoreMatcher`（提取到 Abstractions 层 public） | RgEngine.cs、Abstractions | ✅ |
| P0-3 | 提取 `BinaryFileDetector` 到 `Abstractions/03-hands/Code/` | RgEngine.cs、SearchService.cs、FileToolHandlers.cs | ✅ |
| P0-4a | 提取 `VcsDirectoryExclusions` 到 `Abstractions/03-hands/Code/` | RgEngine.cs、SearchService.cs | ✅ |
| P0-4b | 提取 `LineSpanIndexer` 到 `Abstractions/03-hands/Code/` | RgEngine.cs、SearchService.cs | ✅ |
| P0-4c | 提取 `FileTypeExtensionMap` 到 `Abstractions/03-hands/Code/` | RgEngine.cs、SearchService.cs | ✅ |

### P1：统一正则编译 ✅ 已完成

| 步骤 | 动作 | 影响文件 | 状态 |
|------|------|----------|------|
| P1-1 | 提取 `SearchRegexCompiler` 到 `Abstractions/03-hands/Code/` | RgEngine.cs、SearchService.cs | ✅ |

### P2：路径安全 + 转义修复 ✅ 已完成

| 步骤 | 动作 | 影响文件 | 状态 |
|------|------|----------|------|
| P2-1 | 提取 `PathSafetyValidator` 到 `Abstractions/00-core/core/Utils/Path/` | RgSubCommand.cs | ✅ |
| P2-2 | `FixPowerShellEscaping` 暂不提取（无重复，未来有复用场景时再提取） | — | 不做 |

---

## 四、扩展应用分析

### 4.1 加速全局

| 场景 | 当前实现 | 可优化 | 预期收益 |
|------|----------|--------|----------|
| `SearchService.GrepSearchAsync` | `File.ReadAllText` + `string.Split` | mmap + Span 行遍历 | 大文件 GC 压力降低，性能提升 30-50% |
| `FileReader.IsBinaryFileAsync` | `File.OpenRead` + 手动缓冲 | mmap 零拷贝 | 大文件检测加速 |
| `MarkdownWalker` 遍历 | `Directory.EnumerateFiles` | PLINQ 并行 | 文件数多时加速 |
| 代码索引 `CodeIndexer` | 逐文件串行索引 | PLINQ 并行 | 索引加速 |
| `.gitignore` 解析 | 逐文件读取 | `GitignoreMatcher` 缓存 | 重复解析加速 |

### 4.2 宽容处理扩展

| 场景 | 当前问题 | 可扩展 | 方案 |
|------|----------|--------|------|
| PowerShell 转义 | 仅 `jcc rg` 有 `FixPowerShellEscaping` | 所有接受正则的命令 | 提取到公共工具类 |
| 路径安全检查 | 仅 `jcc rg` 有 `IsRootPath` | 所有文件遍历命令 | 提取 `PathSafetyValidator` |
| 超时硬终止 | 仅 `jcc rg` 有 `--timeout` | 所有长时间命令 | 提取 `CancellationTokenSource.CancelAfter` 封装 |
| 二进制检测 | 3 处不同实现 | 统一 `BinaryFileDetector` | 扩展名超集 + 内容检测 |

### 4.3 技术复用

| 技术 | 当前应用 | 可扩展到 |
|------|----------|----------|
| mmap 零拷贝 | RgEngine 大文件读取 | 文件索引、日志扫描、大文件分析 |
| PLINQ 并行 | RgEngine 文件搜索 | 代码索引、批量文件操作、批量测试 |
| 零 GC Span | RgEngine 行遍历 | 日志解析、CSV 解析、文本处理 |
| `FindLineIndex` 二分查找 | RgEngine 多行匹配 | 任何字符偏移→行号映射场景 |
| `FrozenSet` 查找集 | RgEngine 二进制扩展名 | 所有 O(1) 查找场景（已有规范） |

---

## 五、实施建议

1. **先修 Bug**：P0-1 和 P0-2 是语义不精确的 Bug，应优先修复
2. **渐进式**：每个 P0 步骤独立编译+测试+提交，不一次性重构
3. **影响面控制**：P0 只改 RgEngine.cs，P1 涉及 SearchService.cs 需编译 Core.slnx
4. **性能验证**：每步重构后跑性能压测，确保不回退
5. **ADR 更新**：重构完成后更新 ADR 0070 状态
