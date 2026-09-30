# DSG030 — 语言无关向量代码索引设计

> 📍 **导航**: [docs/design/](README.md) › DSG030
> 🔗 **相关**: DSG029（InMemoryIndexStore 不可变快照）、ADR 0098（万物皆插件）
> 📌 **状态**: accepted（嵌入模型选型已确定：API + simhash 混合）

## 1. 目标

为通用 Coding Agent 构建**语言无关的混合代码索引**，融合两条查询路径：

| 路径 | 能力 | 已有 |
|------|------|------|
| **符号图**（精确） | 调用链、影响范围、依赖关系 — 确定性图遍历 | ✅ `CallGraph` / `DependencyGraph` |
| **向量搜索**（语义） | "找类似代码"、"解释这段逻辑" — 语义相似度 | ❌ **本设计新增** |

参考 Cursor 的向量切割方案：tree-sitter 按函数/类切块 → 块内容哈希做缓存键 → 嵌入向量 → ANN 搜索。**客户端本地完成**，不需要 turbopuffer 等云端向量库。

## 1.1 与 Cursor 技术栈对比（场景差异）

| 技术 | Cursor 用它因为 | 本地工具场景 | 本方案 |
|------|----------------|------------|--------|
| **turbopuffer** | 百万级云端代码库，多租户，serverless 弹性 | 单项目几千文件，内存够用 | 本地内存 + 暴力搜索 SIMD（引入 turbopuffer = 过度设计） |
| **Merkle 树** | 客户端→服务器增量同步，只传哈希分歧分支 | 本地单进程，不需要跨端同步 | `ContentHash` 更简单 |
| **simhash 索引复用** | 组织内代码库 92% 相似，复用队友索引 | 多项目共享代码 | **借鉴** — 索引复用/去重辅助 |
| **tree-sitter 切块** | ✅ | ✅ | 已采用 |
| **块内容哈希缓存** | ✅ | ✅ | 已采用（ContentHash） |

**结论**：Cursor 是云端 SaaS 架构，本方案是客户端本地架构。跳过 turbopuffer 和 Merkle 树是对的，simhash 借鉴做索引复用。

## 2. 整体架构

```
┌──────────────────────────────────────────────────────────┐
│                    Agent 查询层                           │
│  "谁调用了 Foo?" / "找类似这段的代码" / "解释这个函数"    │
└─────────────────────────┬────────────────────────────────┘
                          │
┌─────────────────────────▼────────────────────────────────┐
│              混合查询路由 (HybridQueryRouter)             │
│  ┌──────────┐  ┌───────────┐  ┌───────────────────┐     │
│  │ 符号查询  │  │ 向量查询   │  │ 文本查询(grep)    │     │
│  │ (精确)    │  │ (语义)    │  │ (降级兜底)        │     │
│  └──────────┘  └───────────┘  └───────────────────┘     │
└─────────────────────────┬────────────────────────────────┘
                          │
┌─────────────────────────▼────────────────────────────────┐
│                   索引层                                  │
│  ┌────────────────────────┐  ┌────────────────────────┐ │
│  │ 符号图 (已有)           │  │ 向量索引 (新增)         │ │
│  │ SymbolIndex+CallGraph  │  │ EmbeddingIndex+ANN     │ │
│  │ +DependencyGraph       │  │ +IEmbeddingModel       │ │
│  └────────────────────────┘  └────────────────────────┘ │
└─────────────────────────┬────────────────────────────────┘
                          │
┌─────────────────────────▼────────────────────────────────┐
│          切块层 (已有 ILanguagePlugin，语言无关)          │
│  C# / Python / Rust / Go / TS / ... 各一个插件           │
│  TreeSitter AST → SymbolInfo[] + ChunkInfo[]             │
│  每个 Symbol = 一个 chunk（含源码片段供嵌入）             │
└─────────────────────────┬────────────────────────────────┘
                          │
┌─────────────────────────▼────────────────────────────────┐
│        增量层 (已有 IncrementalUpdater + FileWatcher)     │
│        块哈希不变 → 跳过重新嵌入                          │
└──────────────────────────────────────────────────────────┘
```

## 3. 三条流水线

### 3.1 索引构建（写入）

```
工作区目录
  │
  ├─1─ 扫描文件 → 按扩展名路由到 ILanguagePlugin
  │     .cs→CSharp  .py→Python  .rs→Rust  .go→Go  .ts→TypeScript ...
  │
  ├─2─ 并行读文件 + ContentHash → 哈希不变则跳过整文件
  │
  ├─3─ TreeSitter 解析 → AST → 切块（一次遍历同时提取符号+块）
  │     ILanguagePlugin.ExtractAll(sourceCode, filePath)
  │     → ExtractionResult { Symbols, Calls, Dependencies, Chunks }
  │
  │     每个 Chunk:
  │       chunkId   = hash(filePath + fqn + contentHash)
  │       sourceText = 源码片段（方法体/类体/函数体）
  │       meta      = { fqn, kind, file, lineRange, language, contentHash }
  │
  ├─4─ 符号图写入（已有）：InsertSymbols / InsertCallEdges / InsertDependencyEdges
  │
  ├─5─ 向量嵌入（新增）— 只对变更块
  │     for each chunk in Chunks:
  │       if chunk.contentHash == storedHash: 复用旧向量（跳过嵌入）
  │       else:
  │         vector = await EmbedModel.EmbedAsync(chunk.sourceText)
  │         VectorStore.Upsert(chunk.chunkId, vector, chunk.meta)
  │
  │     批量优化：首次索引用 EmbedBatchAsync 批量嵌入
  │
  └─6─ 原子提交：新索引构建成功 → 原子替换引用（last-good 原则）
       构建期间旧索引继续服务查询
```

### 3.2 查询（读取）

```
自然语言查询
  │
  ├─1─ 查询分类 (QueryClassifier)
  │     符号型("谁调用Foo"、"调用链A→B")  → 符号图
  │     语义型("找类似代码"、"解释这段")   → 向量搜索
  │     混合型                              → 两路并行 + 融合
  │
  ├─2a 符号图（已有）：
  │     CallGraph.GetCallersAsync("Foo")
  │     CallGraph.GetCallChainAsync("A", "B")
  │     CallGraph.GetImpactScopeAsync("Foo")
  │
  ├─2b 向量搜索（新增）：
  │     queryVec = await EmbedModel.EmbedAsync(query)
  │     results  = ANN.Search(queryVec, topK=10)
  │     → [{ chunkId, file, lineRange, fqn, score }]
  │
  ├─3─ 结果融合 + 去重 + TokenBudget 截断（已有 TruncateByTokenBudget）
  │     符号结果（精确，高置信）+ 向量结果（语义，按 score 排序）
  │
  └─4─ 返回代码块元数据 → Agent 本地读取源码 → 上传给 LLM
```

### 3.3 增量更新

```
FileWatcher 触发文件变更
  │
  ├─1─ 文件内容哈希不变 → return（整文件未变）
  ├─2─ 重新解析变更文件 → 新块列表 (ExtractAll)
  ├─3─ Diff 新旧块：
  │     added   = newChunks - oldChunks    → 需要嵌入
  │     removed = oldChunks - newChunks    → 从向量库删除
  │     unchanged = newChunks ∩ oldChunks  → 跳过（复用旧向量）
  ├─4─ 只对 added 块嵌入 + 写入向量库
  ├─5─ 从向量库删除 removed 块
  └─6─ 更新符号图（已有 IncrementalUpdater）
```

## 4. 数据结构（新增）

### 4.1 ChunkInfo — 向量嵌入的输入单元

```csharp
// 位置：lib/abstractions/abs_perception/code_index/chunk/ChunkInfo.cs
namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 代码块 — 向量嵌入的输入单元，与 SymbolInfo 分离
/// <para>SymbolInfo 只含元数据（符号图用，内存友好）</para>
/// <para>ChunkInfo 额外持有源码片段（嵌入用，嵌入后可释放）</para>
/// </summary>
public sealed record ChunkInfo {
    /// <summary>块唯一标识 = hash(filePath + fqn + contentHash)</summary>
    public required string ChunkId { get; init; }
    /// <summary>关联符号的完全限定名</summary>
    public required string SymbolFqn { get; init; }
    /// <summary>符号类型（Method/Class/Function/...）</summary>
    public required SymbolKind Kind { get; init; }
    /// <summary>文件路径</summary>
    public required string FilePath { get; init; }
    /// <summary>起始行号</summary>
    public required int StartLine { get; init; }
    /// <summary>结束行号</summary>
    public required int EndLine { get; init; }
    /// <summary>语言标识（c-sharp/python/rust/go/...）</summary>
    public required string LanguageId { get; init; }
    /// <summary>块内容哈希 — 增量缓存键，哈希不变则跳过嵌入</summary>
    public required string ContentHash { get; init; }
    /// <summary>块源码文本 — 嵌入输入，嵌入完成后可置 null 释放内存</summary>
    public string? SourceText { get; init; }
}
```

### 4.2 ExtractionResult 扩展

```csharp
// 位置：lib/abstractions/abs_perception/code_index/disclosure/ExtractionResult.cs
// 修改：新增 Chunks 字段（默认空，向后兼容）
public sealed class ExtractionResult {
    public required IReadOnlyList<SymbolInfo> Symbols { get; init; }
    public required IReadOnlyList<CallEdge> Calls { get; init; }
    public required IReadOnlyList<DependencyEdge> Dependencies { get; init; }
    /// <summary>代码块列表 — 向量嵌入用，默认空（符号图不需要）</summary>
    public IReadOnlyList<ChunkInfo> Chunks { get; init; } = [];
}
```

### 4.3 嵌入模型抽象（语言无关）

```csharp
// 位置：lib/abstractions/abs_perception/code_index/embedding/IEmbeddingModel.cs
namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 嵌入模型抽象 — 对任何语言的代码块一视同仁
/// </summary>
public interface IEmbeddingModel {
    /// <summary>向量维度</summary>
    int Dimensions { get; }
    /// <summary>模型标识（用于缓存键）</summary>
    string ModelId { get; }

    /// <summary>嵌入单个文本 → 向量</summary>
    Task<float[]> EmbedAsync(string text, CancellationToken ct);

    /// <summary>批量嵌入 — 首次索引提速，减少 API 往返/模型加载开销</summary>
    Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct);
}
```

### 4.4 ANN 搜索抽象

```csharp
// 位置：lib/abstractions/abs_perception/code_index/embedding/IAnnSearch.cs
namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 近似最近邻搜索抽象 — 语言无关、嵌入模型无关
/// </summary>
public interface IAnnSearch {
    /// <summary>添加向量</summary>
    void Add(string id, float[] vector);
    /// <summary>批量添加</summary>
    void AddRange(IReadOnlyList<(string Id, float[] Vector)> items);
    /// <summary>删除向量</summary>
    void Remove(string id);
    /// <summary>搜索 top-K 最相似向量（余弦相似度）</summary>
    IReadOnlyList<(string Id, float Score)> Search(float[] query, int topK, CancellationToken ct);
    /// <summary>向量数量</summary>
    int Count { get; }
}
```

### 4.5 EmbeddingIndex — 串联嵌入+存储+ANN

```csharp
// 位置：server/code_index/embedding/EmbeddingIndex.cs
namespace JoinCode.CodeIndex;

/// <summary>
/// 向量索引 — 串联嵌入模型、向量存储、ANN 搜索
/// 维护块哈希缓存：块没变就不重新嵌入（省 API 调用/计算）
/// </summary>
public sealed class EmbeddingIndex : IAsyncDisposable {
    private readonly IEmbeddingModel _embedModel;
    private readonly IAnnSearch _ann;
    private readonly Dictionary<string, ChunkMetadata> _metadata;  // chunkId → 元数据
    private readonly Dictionary<string, string> _chunkHashes;      // chunkId → contentHash（增量缓存）

    /// <summary>索引就绪状态 — 供 Agent 层判断查询路径</summary>
    public IndexStatus Status { get; private set; }

    /// <summary>批量索引代码块 — 跳过哈希未变的块</summary>
    public async Task IndexChunksAsync(IReadOnlyList<ChunkInfo> chunks, CancellationToken ct);

    /// <summary>语义搜索 — 查询文本 → 嵌入 → ANN → 元数据</summary>
    public async Task<IReadOnlyList<ChunkSearchResult>> SearchAsync(string query, int topK, CancellationToken ct);

    /// <summary>删除文件关联的所有块</summary>
    public Task RemoveFileAsync(string filePath, CancellationToken ct);
}

public enum IndexStatus { NotReady, Ready, Partial, Error }

public sealed record ChunkSearchResult(
    string ChunkId, string FilePath, string SymbolFqn,
    int StartLine, int EndLine, float Score);
```

## 5. 代码组织方案

### 5.1 新增目录

```
server/code_index/
  embedding/                    ← 新增
    EmbeddingIndex.cs           — 串联层（嵌入+存储+ANN）
    EmbeddingIndexStatus.cs     — 索引就绪状态
  vector/                       ← 新增
    BruteForceAnn.cs            — 暴力搜索 + SIMD（起步方案）
    VectorMath.cs               — SIMD 余弦相似度（复用项目 Sse2/Avx2 经验）

lib/abstractions/abs_perception/code_index/
  chunk/                        ← 新增
    ChunkInfo.cs                — 块数据模型
  embedding/                    ← 新增
    IEmbeddingModel.cs          — 嵌入模型抽象
    IAnnSearch.cs               — ANN 搜索抽象
    IndexStatus.cs              — 状态枚举
    ChunkSearchResult.cs        — 搜索结果
```

### 5.2 修改现有文件（最小改动）

| 文件 | 改动 | 说明 |
|------|------|------|
| `ExtractionResult.cs` | 新增 `Chunks` 字段（默认空） | 向后兼容，现有插件不填则跳过向量层 |
| `ILanguagePlugin.cs` | 无需改动 | `ExtractAll` 已返回 `ExtractionResult`，Chunks 随之返回 |
| `CSharpSymbolExtractor.cs` | `ExtractAll` 补充 Chunks 生成 | 从 AST 节点提取源码片段 + 算块哈希 |
| `CodeIndexer.cs` | 新增 `EmbeddingIndex` 字段 + 在 `BuildIndexAsync` 加嵌入步骤 | 组合向量层 |
| `InMemoryIndexStore.cs` | 可选：加 `EmbeddingIndex` 引用 | 或独立持有，不进 IndexSnapshot |

### 5.3 不需要移动的代码

现有 `server/code_index/` 结构保持不变：
- `graph/` — 符号图查询（已有，不动）
- `incremental/` — 增量更新（已有，不动）
- `parsing/` — TreeSitter 解析（已有，扩展 Chunks 生成）
- `indexing/` — 索引编排（已有，加向量步骤）
- `analytics/` — 图分析（已有，不动）

**结论：不需要大规模移动代码。主要是新增 `embedding/` + `vector/` 两个目录，并在 `CodeIndexer` 里组合。**

## 6. 多语言扩展（语言无关）

### 6.1 插件架构

`ILanguagePlugin` 已是语言无关接口。每种语言只需实现一个插件：

```
ILanguagePlugin (已有接口)
  ├─ CSharpSymbolExtractor   (已有, TreeSitter c-sharp)
  ├─ BashAstParser           (已有, TreeSitter bash)
  ├─ PythonSymbolExtractor   (新增, TreeSitter python)
  ├─ RustSymbolExtractor     (新增, TreeSitter rust)
  ├─ GoSymbolExtractor       (新增, TreeSitter go)
  ├─ TypeScriptSymbolExtractor (新增, TreeSitter typescript)
  ├─ JavaScriptSymbolExtractor (新增, TreeSitter javascript)
  ├─ JavaSymbolExtractor     (新增, TreeSitter java)
  └─ ...
```

### 6.2 每个插件只需实现

- `ExtractSymbols`: TreeSitter AST → `SymbolInfo[]`
- `ExtractCalls`: AST → `CallEdge[]`
- `ExtractDependencies`: AST → `DependencyEdge[]`
- `ExtractChunks`: AST → `ChunkInfo[]`（新增，从节点提取源码片段 + 算块哈希）

### 6.3 切块策略按语言特性调整

| 语言 | 切块粒度 | TreeSitter 语言 |
|------|---------|----------------|
| C# | method/class/property/constructor | c-sharp |
| Python | function/class | python |
| Rust | fn/impl/trait/mod | rust |
| Go | func/struct/interface | go |
| TypeScript | function/class/arrow function | typescript |
| JavaScript | function/class/arrow function | javascript |
| Java | method/class/interface | java |
| C/C++ | function/class/struct | c, cpp |

### 6.4 向量层完全语言无关

- 嵌入模型对任何语言的代码块一视同仁（代码语义跨语言通用）
- `VectorStore` 不关心语言，只存 `vector + metadata`
- `ANN` 搜索纯数学运算，语言无关

### 6.5 父文档检索（Parent Document Retriever）

**决策**：采用父文档检索而非重叠块+合并去重。理由：代码符号天然有父子层级（method⊂class⊂file），父文档检索天然契合，无需复杂的重叠区域识别和文本拼接去重。

**机制**：

```
向量库存小块（方法/属性）          父文档存储存大块（类/文件原文）
┌────────────────────┐           ┌──────────────────────────┐
│ chunkId: m1         │           │ chunkId: class-001        │
│ fqn: N.Foo.Bar      │──Parent──▶│ fqn: N.Foo                │
│ ParentChunkId: ─────│──Id──────▶│ SourceText: class Foo {  │
│ SourceText: void..  │           │   void Bar() { }          │
└────────────────────┘           │   void Baz() { } }        │
                                  └──────────────────────────┘
召回 m1 → 查 ParentChunkId → 取 class-001 原文 → 喂 LLM
```

**数据结构**：

| 类型 | 字段 | 说明 |
|------|------|------|
| `ChunkInfo` | `ParentChunkId` (string?) | 指向父文档 ChunkId，null=自身是父文档或未启用 |
| `ChunkSearchResult` | `ParentDocumentText` (string?) | 召回时填充的父文档原文 |
| `ChunkSearchResult` | `ParentStartLine/ParentEndLine` (int?) | 父文档行范围 |
| `ChunkSearchResult` | `ParentSymbolFqn` (string?) | 父文档符号 FQN |
| `ParentDocument` | `ChunkId/FilePath/SymbolFqn/StartLine/EndLine/SourceText` | 父文档原文存储单元 |
| `ExtractionResult` | `ParentDocuments` (IReadOnlyList\<ParentDocument\>) | 提取时生成的父文档列表 |

**父子层级建立规则**（`CollectChunksWithParents`）：

1. 类级别符号（Class/Struct/Interface/Record/RecordStruct/Enum/Delegate）→ 既是 ChunkInfo（被嵌入）也是 ParentDocument（存原文）
2. 子符号（Method/Property/Field/Event/Constructor/Operator/Indexer）→ `ParentChunkId` 指向所属类（通过 FQN 层级推断：`N.C.M` 的父是 `N.C`）
3. 顶级子符号（无父类，如顶级方法）→ `ParentChunkId` 指向文件级父文档（整个文件原文，不嵌入）

**存储**：`InMemoryParentDocumentStore`（纯内存，`ReaderWriterLockSlim` 线程安全，进程退出释放）

**集成**：
- `EmbeddingIndex` 构造时可选注入 `IParentDocumentStore`，`SearchAsync` 召回后自动填充父文档原文
- `CodeIndexer.SetParentDocumentStore` 注入，`BuildIndexAsync` Phase E3 批量填充，Phase F 同步删除

**为什么不选重叠块+合并去重**：
- 当前按符号切，语义完整，不切断语义（重叠块解决的核心问题不存在）
- 符号边界清晰，重叠价值有限
- 合并去重逻辑复杂（识别重叠区+边界对齐+文本拼接）
- 父文档检索天然利用符号父子层级，零额外推断成本

## 7. 向量搜索层选型

### 7.1 起步方案：暴力搜索 + SIMD

```csharp
// server/code_index/vector/BruteForceAnn.cs
public sealed class BruteForceAnn : IAnnSearch {
    private readonly Dictionary<string, float[]> _vectors = new();
    private readonly int _dimensions;

    public IReadOnlyList<(string Id, float Score)> Search(float[] query, int topK, CancellationToken ct) {
        // SIMD 余弦相似度计算（复用项目 SwissTable 的 Sse2/Avx2 经验）
        var scores = _vectors
            .Select(kvp => (kvp.Key, Score: VectorMath.CosineSimilarity(query, kvp.Value)))
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .ToList();
        return scores;
    }
}
```

- **优点**：零依赖、AOT 友好、简单可靠、项目已有 SIMD 经验
- **适用**：<10K 块的中小代码库
- **后续升级**：向量量大时换 HNSW（O(log n) 查询）

### 7.2 SIMD 余弦相似度

```csharp
// server/code_index/vector/VectorMath.cs
// 复用项目 lib/structura/collections/ 的 Sse2/Avx2 经验
public static float CosineSimilarity(float[] a, float[] b) {
    // Vector<float> SIMD 点积 + 归一化
    // 比标量快 4-8x
}
```

## 8. 嵌入模型选型（已确定）

### 8.1 选型过程与排除

| 方案 | 结论 | 原因 |
|------|------|------|
| **本地 ONNX（直接引用）** | ❌ 排除 | AGENTS.md 禁止微软 AI 包直接引用（NativeAOT 不兼容） |
| **本地 ONNX（卫星项目/独立 exe）** | ✅ 可选 | 跨进程通讯绕过 AOT 限制，主程序 AOT，ONNX 在独立进程跑 |
| **调 LLM API** | ✅ 可选 | HTTP 调用 AOT 友好，项目已有 LLM 基础设施，高质量嵌入 |
| **simhash** | ✅ 采用（辅助） | 纯位运算零依赖，AOT 完全兼容，用于索引复用/去重（借鉴 Cursor） |

### 8.2 嵌入方案对比（按 10s 索引要求）

| 方案 | 10s 索引（1000文件） | 离线 | 质量 | 复杂度 |
|------|---------------------|------|------|--------|
| **调 LLM API** | ⚠️ 网络延迟 5-10s | ❌ | ✅ 高 | 低 |
| **本地 ONNX（独立 exe）** | ✅ 批量推理 2-3s | ✅ | ⚠️ 中（通用模型） | 中（卫星项目） |
| **混合（ONNX 主 + API 精）** | ✅ | ✅ | ✅ | 高 |

### 8.3 最终方案：ONNX 独立 exe（主路径）+ simhash（辅助）

**主路径 — ONNX 独立 exe（极速离线）**：

```
主程序（AOT）
  │
  ├─ EmbeddingClient（IPC 客户端，AOT 友好）
  │     ↓ named pipe / stdin-stdout
  │
  └─ 启动 onnx_embedding_exe.exe（独立进程，非 AOT）
        │
        ├─ Microsoft.ML.OnnxRuntime（NuGet，仅卫星项目引用）
        ├─ model_quantized.onnx（~22MB INT8 量化模型，实测 5000块 7.0s）
        ├─ tokenizer.json
        └─ 循环：读 stdin → tokenize → ONNX 推理 → stdout 返回向量
```

**跨进程通讯协议**（二进制，极速）：
- 请求：`4字节长度(LE) + UTF8 JSON {"id":1, "texts":["code1","code2"]}`
- 响应：`4字节长度(LE) + float[] 二进制（连续 4×n×d 字节）`
- 批量嵌入：一次请求多个文本，减少 IPC 往返

**模型文件管理**：
- `model_quantized.onnx`（~22MB INT8 量化，384维）— 从 Xenova/all-MiniLM-L6-v2 下载
- `tokenizer.json` — 同上
- 存放：`%AppData%/jcc/embedding/`
- 下载源：hf-mirror.com（国内镜像）
- **实测**：5000块索引 7.0s（seq=32, batch=32），达标

**卫星项目结构**：
```
tool/onnx_embedding/           ← 新增卫星项目（非 AOT）
  OnnxEmbeddingExe.csproj      — 引用 Microsoft.ML.OnnxRuntime
  Program.cs                   — stdin/stdout 循环
  OnnxEmbedder.cs              — 模型加载 + 推理
```

**辅助 — simhash（索引复用/去重）**：
- 借鉴 Cursor 的 simhash 索引复用
- 多项目共享代码时，simhash 检测相似块，复用已有向量
- 纯位运算，极快，零依赖

**降级链**：ONNX exe 启动失败/推理失败 → simhash 粗筛 → 符号搜索 → ripgrep

### 8.3 OpenAI 现代嵌入设计调研

**模型规格**：

| 模型 | 维度 | 上下文 | 编码 | 价格 | 推荐 |
|------|------|--------|------|------|------|
| text-embedding-3-small | 1536（可截断） | 8191 tokens | cl100k_base | $0.02/1M tokens | ✅ 性价比最高 |
| text-embedding-3-large | 3072（可截断） | 8191 tokens | cl100k_base | $0.13/1M tokens | 需要更高精度时 |

**Matryoshka Representation Learning (MRL) — text-embedding-3 核心创新**：
- API 调用时可指定 `dimensions` 参数，返回截断后的向量
- 1536 维 → 可截断到 256/512/1024，性能损失很小（256 维保留 ~90% 性能）
- **对本方案的意义**：
  - 用 **256 维**而非 1536 维：内存降 6x，暴力搜索速度提 6x
  - 可先用 256 维粗排，再用 1536 维精排（两阶段检索）

**批量嵌入**：
- 一次请求最多 2048 个输入
- 首次索引必须批量嵌入（减少 HTTP 往返）
- `EmbedBatchAsync` 接口已在设计第 4.3 节预留

**长文本处理**（OpenAI cookbook 方案）：
- 超过 8191 tokens 的块需要二次分块
- 分块后可加权平均 + 归一化成单个向量
- **本方案**：tree-sitter 按函数/类切分，单块通常 < 8191 tokens，一般不需要再分
- 兜底：超大块（如 2000 行的 God Class）截断到 8191 tokens

**代码嵌入最佳实践**：
- OpenAI 无专门代码嵌入模型，text-embedding-3 对代码效果不错
- 嵌入时加语言前缀：`"c# code: " + chunkText`，提示模型这是代码
- 可选：对大块先用 LLM 生成摘要，再嵌入摘要（更语义化，但多一次 LLM 调用）

**现代向量搜索趋势**（可选优化）：
- MRL 降维：256 维粗排 → 1536 维精排
- 量化：float32 → int8，内存降 4x（SIMD int8 点积更快）
- 混合搜索：向量 + 符号图 + BM25（本方案已有符号图）
- 重排：向量粗排 → cross-encoder 精排（可选，增加延迟）

## 9. 纵深防御（4层）

| 层 | 策略 | 触发条件 | 实现 |
|----|------|---------|------|
| **L1** | **last-good**：构建期间旧索引服务，失败不删旧 | 索引构建失败 | `EmbeddingIndex` 原子替换引用，构建失败保留旧引用 |
| **L2** | **降级链**：向量→符号搜索→ripgrep | 向量未就绪/嵌入失败 | `HybridQueryRouter` 检查 `IndexStatus`，未就绪走 `SymbolSearcher`，再降级走 ripgrep |
| **L3** | **超时保护**：ANN 设 topK+timeout，BFS 设 maxDepth+maxNodes | 大代码库/环依赖 | `SearchAsync` 接受 `CancellationToken`，topK 上限 |
| **L4** | **状态显式**：`IndexStatus { NotReady, Ready, Partial, Error }` | Agent 选择查询路径 | `EmbeddingIndex.Status` 属性暴露，Agent 层据此路由 |

### 降级链详解

```
查询请求
  │
  ├─ EmbeddingIndex.Status == Ready?
  │   ├─ Yes → 向量搜索（语义）
  │   └─ No  → 降级
  │
  ├─ SymbolSearcher 可用?
  │   ├─ Yes → 符号名搜索（精确）
  │   └─ No  → 降级
  │
  └─ ripgrep 文本搜索（兜底，总有结果）
```

## 10. 实现路径（分阶段）

### Phase 1：抽象层 + 数据模型（无外部依赖）

1. 新增 `ChunkInfo` / `IEmbeddingModel` / `IAnnSearch` / `IndexStatus` / `ChunkSearchResult`
2. 扩展 `ExtractionResult` 加 `Chunks` 字段
3. 编译验证 + 单元测试（模型构造、默认值）

### Phase 2：向量搜索层（零依赖）

1. 实现 `BruteForceAnn`（暴力搜索）
2. 实现 `VectorMath.CosineSimilarity`（SIMD 加速）
3. 单元测试：添加/删除/搜索、SIMD vs 标量一致性

### Phase 3：嵌入层（依赖嵌入模型选型）

1. 实现 `IEmbeddingModel` 的具体实现（API / ONNX / simhash，取决于选型）
2. 实现 `EmbeddingIndex`（串联嵌入+存储+ANN+增量缓存）
3. 单元测试：嵌入、增量跳过、状态管理

### Phase 4：切块集成

1. `CSharpSymbolExtractor.ExtractAll` 补充 `Chunks` 生成
2. `CodeIndexer.BuildIndexAsync` 加向量嵌入步骤
3. 集成测试：端到端索引 → 查询

### Phase 5：混合查询路由

1. 实现 `HybridQueryRouter`（查询分类 + 双路并行 + 融合）
2. 实现 `QueryClassifier`（符号型 vs 语义型）
3. 集成测试：混合查询、降级链

### Phase 6：多语言插件（按需扩展）

1. Python / Rust / Go / TypeScript 插件（各一个 `ILanguagePlugin` 实现）
2. 每个插件实现 `ExtractChunks`（从 AST 提取源码片段 + 块哈希）

## 11. 性能预期

**硬性要求：10s 内完成整个工程索引**（用户要求）

**卫星项目实测数据**（2026-09-30 验证）：

| 组件 | 实测 | 目标 | 合格 |
|------|------|:----:|:----:|
| SIMD 暴力搜索（10K×256维） | **0.969 ms** | <5ms | ✅ |
| ONNX 量化模型加载 | 87 ms | <1s | ✅ |
| ONNX 量化推理 5000块（seq=32） | **7.0 s** | <10s | ✅ |
| ONNX 原版推理 5000块（seq=32） | 11.4 s | <10s | ≈ |
| DirectML GPU | 不可用 | — | ❌ 此机器不兼容 |

**最佳配置：量化模型（22MB INT8）+ seq=32 + batch=32 → 5000块 7.0s ✅**

**关键发现**：
- 量化模型（22MB）比原版（86MB）快 1.6x，模型小 4x，质量损失可接受
- DirectML 不可用（此机器 GPU 不兼容），CPU 推理已达标
- CPU 多线程优化无明显帮助（ONNX 内部已并行）
- seq_len 是性能关键：seq=32 比 seq=64 快 ~2.5x

**达标策略**：
1. 使用量化模型 `model_quantized.onnx`（22MB，从 Xenova/all-MiniLM-L6-v2 下载）
2. 切块时限制 max_tokens=32，长函数截断
3. batch=32，ONNX 内部已并行无需增大

| 操作 | 预期延迟 | 说明 |
|------|---------|------|
| 首次索引（≤2000块） | **<10s** | TreeSitter ~3s + ONNX 嵌入 ~5s + 写入 ~1s |
| 首次索引（5000块） | ~11s | 需 seq=32 或多进程优化 |
| 增量更新（单文件） | <100ms | 块哈希跳过 + TreeSitter 增量 |
| 向量搜索（10K×256维） | **0.97ms** | SIMD 暴力搜索（实测） |
| 符号查询 | <1ms | 已有，Dictionary O(1) |

## 11.1 纯内存策略（无持久化）

- 不需要向量数据库（turbopuffer/SQLite/LiteDB 等）
- 进程退出释放全部内存，下次启动重建索引
- 增量更新靠 `FileWatcher` + `ContentHash`，不靠持久化
- 重建速度 <10s（可接受，不需要持久化开销）
- 符号图已有 `GraphPersistence`（可选），向量层不需要

## 12. 与现有系统的关系

| 现有组件 | 关系 |
|---------|------|
| `InMemoryIndexStore` + `IndexSnapshot` | 符号图存储，不动。向量层独立存储 |
| `CallGraph` / `DependencyGraph` | 符号图查询，不动。向量层并行查询 |
| `IncrementalUpdater` + `FileWatcher` | 增量框架，复用。向量层在增量回调中触发 |
| `SymbolSearcher` | 符号搜索，复用做降级 |
| `ContentHash` | 内容哈希，复用做块哈希 |
| `TruncateByTokenBudget` | token 截断，复用做结果融合 |
| `ILanguagePlugin` | 语言插件接口，扩展 Chunks 字段 |
| `CSharpSymbolExtractor` | C# 提取器，扩展 Chunks 生成 |

---

<!-- 🤖 Auto Decision: 2026-09-30 -->
<!-- 决策: 向量层独立于符号图，不修改 IndexSnapshot，新增 EmbeddingIndex 独立存储 -->
<!-- 原因: 符号图用不可变快照(CAS无锁)，向量层用可变Dictionary(写少读多+ANN内部状态)，两者并发模型不同 -->
<!-- 替代方案: 向量数据放进 IndexSnapshot（被否决：向量量大时每次 CAS 复制整批向量，O(n) 内存压力） -->
<!-- 验证: 待 Phase 1 编译验证 -->

## 13. 实现进展与优化记录（2026-10-01）

### 13.1 切块策略：固定 500 行切块 + 预嵌入 AST 符号 FQN

**决策**：向量索引用固定 500 行/块（非 AST 符号切块），每块记录覆盖的 AST 符号 FQN（`ContainedSymbolFqns`）。

**原因**：AST 符号切块产生 ~32000 块（太多，ONNX 推理 60s+），固定 500 行切块产生 ~5970 块（10x 少）。块内预嵌入 FQN，搜索时直接查 CallGraph/DependencyGraph 组成知识图谱。

**实现**：`LineBasedChunkExtractor`（Span 优化，`ReadOnlySpan.Slice` 替代 `Split+Join`）

### 13.2 SIMD 加速

| 优化点 | 改动 | 收益 |
|--------|------|------|
| **Mean pooling** | `Vector<float>` 批量累加+归一化，提取 `VectorAddInPlace`/`VectorScaleInPlace` + `[AggressiveInlining]` | 73M 浮点加法 SIMD 化 |
| **持久化读写** | `MemoryMarshal.AsBytes` 批量读写，替代逐 float `bw.Write`/`br.ReadSingle` | 2.3M 次函数调用 → 5970 次批量 |
| **NormalizeInPlace** | `Vector<float>` 批量乘法 | 搜索时归一化加速 |
| **回退逻辑** | `Vector.IsHardwareAccelerated` 显式回退到标量 | 不支持 SIMD 的硬件健壮性 |

**结论**：SIMD 收益有限（~0.5s），真正瓶颈是 ONNX 推理（10.9s）和 Roslyn 解析（5.4s），这些是原生库内部已用 SIMD。

### 13.3 mmap 大文件零拷贝读取

**决策**：>1MB 文件用 `MemoryMappedFile` + `ArrayPool<byte>` 替代 `ReadAllTextAsync`，避免 byte[] 中间分配。

**实现**：`HashUtility.ReadFileAndComputeHashMappedAsync`（`await using` + `ReadExactlyAsync`）

### 13.4 知识图谱关联（核心功能）

**问题**：`ContainedSymbolFqns` 被填充+持久化，但 `SearchSemanticAsync` 从未读取它查图谱 — 设计与实现有差距。

**修复**：`SearchSemanticAsync` 加 `include_graph` 参数（默认 true），对每个搜索结果的 `ContainedSymbolFqns` 查 `CallGraph.GetCallersAsync`/`GetCalleesAsync`，拼进返回文本。每块最多 5 个 FQN，每类最多 2 条边，跨结果去重。

**效果**：AI 在语义搜索结果中直接看到调用方/被调用方关系，不需要额外调用 `code_index_get_callers` 等工具。

### 13.5 全项目实测数据

```
[code-index] 扫描: 5809 文件 (439ms)
[code-index] 符号索引: 5809 文件 (5383ms)
[code-index] 向量嵌入: 5970 块 (10952ms)
[code-index] 父文档: 13867 文档 (31ms)
[code-index] 持久化: 680ms
[code-index] 总计: 17486ms
```

| 阶段 | 耗时 | 说明 |
|------|------|------|
| 扫描 | 439ms | 5809 文件 |
| 符号索引 | 5383ms | Roslyn AST 解析 + 符号写入 |
| 向量嵌入 | 10952ms | ONNX 推理（全 CPU）+ SIMD mean pooling |
| 父文档 | 31ms | 13867 文档 |
| 持久化 | 680ms | 三索引批量写入 |
| **总计** | **17.5s** | 全 CPU + BelowNormal 优先级 |

**配置**：量化模型 22MB INT8 + seq=32 + batch=128 + 全 CPU + BelowNormal + 固定 500 行切块

**瓶颈分析**：ONNX 推理 10.9s（原生库内部已 SIMD）+ Roslyn 解析 5.4s（无法 SIMD），C# 层优化空间有限。Phase E+E2 并行反而更慢（18.8s，CPU 争抢）已回退。

### 13.6 修复: tokenizer considerPreTokenization=false 导致向量退化（2026-10-01）

**现象**：搜索 Score 全 1.0000，所有块向量完全相同。

**根因**：`BertTokenizer.EncodeToIds` 传 `considerPreTokenization: false`，跳过 BERT 预分词，所有词被当作 `[UNK]` (id=100) 处理。不同文本编码为相同的 `[CLS][UNK][SEP]` (101,100,102)，ONNX 推理产生完全相同的向量。

**诊断过程**：
1. 写诊断程序嵌入 6 段不同代码，发现所有非空文本向量完全相同
2. 打印 token IDs，发现全部编码为 `[101, 100, 102]`
3. 对比 `considerPreTokenization` true/false，确认 false 导致退化

**修复方案**：提取 `GuardedBertTokenizer` 装饰类（`tool/onnx_embedding/GuardedBertTokenizer.cs`），包装 `BertTokenizer`：
- `EncodeToIdsSafe(text)` — 强制 `considerPreTokenization=true`，调用方无需接触危险参数
- `EncodeToIds(...)` — 守卫拦截 `considerPreTokenization=false`，抛出带 4 要素诱导报错（①为什么拒绝 ②触发参数 ③拦截守卫 ④正确做法）
- 守卫元数据：`Name=PreTokenizationGuard`，`Priority=100`（tokenizer 层）

**验证**：
- 3 个回归测试通过（`OnnxEmbedderRegressionTest`：不同文本向量不同 / 相同文本向量相同 / 非空文本向量非零）
- jcc 重建索引后搜索 Score=0.22~0.42（非全 1.0）
- 向量嵌入耗时 15s（tokenize 更多 token，之前所有文本只产生 3 个 token）

**设计理由**：用装饰类而非直接改参数，防止未来误改回 false 导致语义搜索静默失效。守卫报错响亮且带诱导方式，AI 调用时能理解错误含义而非换命令绕过。

### 13.7 搜索质量优化：重排序 + 图谱加权 + L2 归一化 + 符号前缀（2026-10-01）

**现象**：13.6 修复向量退化后，搜索能返回不同 Score，但目标代码 `VectorMath.cs`（含 `CosineSimilarity` 方法）未排在前列。搜 "cosine similarity calculation SIMD" 时 `VectorMathTests.cs` 排第 1 位而实现文件 `VectorMath.cs` 排不上。

**根因分析**（三个叠加问题）：

1. **maxSeqLen=32 截断核心语义**：BERT 只取前 32 token，代码块前 32 token 多为 `namespace`+`class` 声明，核心方法名被截断
2. **BERT tokenizer 不拆分 PascalCase**：`CosineSimilarity` 作为一个整体 token，与查询 `cosine similarity`（两个词）不匹配
3. **缺少重排序和图谱加权**：仅靠向量余弦单信号排序，无法利用符号名、关键词、调用关系等结构化信号

**修复方案**（四项协同）：

1. **L2 归一化**（`OnnxEmbedder.MeanPoolSimd/Scalar`）：mean pooling 后对向量做 L2 归一化。对余弦相似度无影响（余弦本身归一化无关），但为未来切换到点积搜索做准备，且提高数值稳定性

2. **符号短名 PascalCase 拆分小写前缀**（`EmbeddingIndex.BuildEmbedText`）：嵌入文本 = `拆分小写的符号短名 + "\n" + SourceText`。例：`CosineSimilarity` → `cosine similarity`，确保核心语义在前 32 token 内被 BERT 编码

3. **SemanticSearchReranker 多信号重排序**（`kit/mcp_tool_dispatch/code_tools/SemanticSearchReranker.cs`）：
   - oversample topK×3 召回候选
   - 多信号加权：向量余弦 0.5 + 关键词重叠 0.3 + 符号名匹配 0.2
   - 图谱加权（上限 0.5，防止淹没向量主信号）：调用关系 +0.05 + 同文件 +0.03 + 同命名空间 +0.02
   - 取 topK 返回

4. **SearchSemanticAsync 集成**（`CodeIndexToolHandlers`）：改为 oversample → RerankAsync → 取 topK 流水线

**验证**：
- 753 单元 + 344 单元 + 3 E2E 回归全通过
- 搜 "cosine similarity calculation SIMD" → `VectorMathTests.cs` 第 1 位（1.0451），`VectorMath.cs` 第 2 位（0.9345）— 实现文件成功上榜

**设计决策**：
- oversample 倍数选 3 而非 5：平衡召回率与延迟（3× 候选集重排序开销可控）
- 图谱加权上限 0.5：防止结构化信号淹没向量相似度主信号（向量是语义搜索的根基）
- 信号权重 0.5/0.3/0.2：向量为主、关键词为辅、符号名补充，符合"语义为主、精确为辅"原则

<!-- 🤖 Auto Decision: 2026-10-01 -->
<!-- 决策: oversample 倍数选 3 而非 5 -->
<!-- 原因: 平衡召回率与延迟,3x 候选集重排序开销可控,5x 在大库上延迟明显 -->
<!-- 替代方案: 动态倍数(根据 topK 调整),但增加复杂度暂不采用 -->
<!-- 验证: 编译通过,1100 测试全通过,搜索 VectorMath.cs 排第 2 位 ✅ -->
