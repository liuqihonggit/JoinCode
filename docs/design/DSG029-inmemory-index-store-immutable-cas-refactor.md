# InMemoryIndexStore 不可变+CAS 改造任务

## 目标

将 InMemoryIndexStore 的 ReaderWriterLockSlim 改为 **不可变快照 + CAS** 无锁方案，同时优化检索性能。

## 性能要求

- 3000 个文件检索时间压入 5s 内
- 读多写少：索引构建一次（写），查询频繁（读）
- 无联级锁定：每个方法内只有一个锁 scope，不嵌套，不跨 await

## 方案：IndexSnapshot 不可变快照 + volatile + ImmutableInterlocked.Update

### 核心数据结构

```csharp
internal sealed record IndexSnapshot {
    // 符号索引（O(1) 精确查找）
    ImmutableDictionary<string, SymbolInfo> SymbolsByFqn
    ImmutableDictionary<string, ImmutableList<SymbolInfo>> SymbolsByName
    ImmutableDictionary<string, ImmutableList<SymbolInfo>> SymbolsByFile
    ImmutableDictionary<SymbolKind, ImmutableList<SymbolInfo>> SymbolsByKind

    // 调用图
    ImmutableList<CallEdge> CallEdges
    ImmutableDictionary<string, ImmutableList<CallEdge>> CallsByCaller
    ImmutableDictionary<string, ImmutableList<CallEdge>> CallsByCallee
    ImmutableDictionary<string, ImmutableList<CallEdge>> CallsByFile

    // 依赖图
    ImmutableList<DependencyEdge> DepEdges
    ImmutableDictionary<string, ImmutableList<DependencyEdge>> DepsBySource
    ImmutableDictionary<string, ImmutableList<DependencyEdge>> DepsByTarget
    ImmutableDictionary<string, ImmutableList<DependencyEdge>> DepsByFile

    // 项目依赖
    ImmutableDictionary<string, ProjectInfo> Projects
    ImmutableDictionary<string, ImmutableList<ProjectReferenceEdge>> ProjectRefs
    ImmutableDictionary<string, ImmutableList<NuGetPackageReference>> NuGetRefs

    // 文件追踪
    ImmutableDictionary<string, FileTrackingEntry> FileTracking

    // ── 排序索引（O(log n) 前缀/范围查询）──
    ImmutableList<string> FileTrackingKeysSorted        // 按路径排序 → 目录前缀过滤
    ImmutableList<SymbolInfo> SymbolsSortedByFqn         // 按 FQN 排序 → 前缀搜索
    ImmutableList<SymbolInfo> SymbolsSortedByName        // 按 Name 排序 → 前缀搜索

    // ── 反向索引（O(1) 反向查找）──
    ImmutableDictionary<string, ImmutableList<ProjectReferenceEdge>> ProjectRefsByTarget  // 按目标项目
    ImmutableDictionary<string, ImmutableList<NuGetPackageReference>> NuGetRefsByPackage  // 按包名

    DateTimeOffset LastUpdated
}
```

### InMemoryIndexStore

```csharp
public sealed class InMemoryIndexStore : ServiceEntity, IAsyncDisposable {
    private volatile IndexSnapshot _snapshot = IndexSnapshot.Empty;

    // 读：无锁 volatile 读（快 20-50 倍）
    public IndexSnapshot GetSnapshot() => _snapshot;

    // 写：CAS 原子更新
    public void Update(Func<IndexSnapshot, IndexSnapshot> updater) {
        ImmutableInterlocked.Update(ref _snapshot, updater);
    }
}
```

## 优化瓶颈清单

### P0（最严重，必须修复）

| # | 位置 | 当前 | 优化后 | 方案 |
|---|------|------|--------|------|
| 1 | ProjectDependencyGraph.cs:88 GetAffectedProjectsAsync BFS | O(BFS层×P×R) 每层全量扫描 | O(BFS层×k) | 建 ProjectRefsByTarget 反向索引 |
| 2 | SymbolSearcher.cs:108 FindReferencesAsync | O(E) 遍历全部 CallEdges | O(fqns.Count) | 复用已有 CallsByCallee 索引 |

### P1（前缀匹配，排序+二分）

| # | 位置 | 当前 | 优化后 | 方案 |
|---|------|------|--------|------|
| 3 | IncrementalUpdater.cs:198 GetTrackedFilesInDirectory | O(F) 遍历 FileTracking.Keys | O(log F+k) | FileTrackingKeysSorted 排序+二分 |
| 4 | CodeIndexer.cs:473 GetTrackedFilesInWorkspace | O(F) 同上 | O(log F+k) | 同上 |
| 5 | GraphAnalytics.cs:200 AnalyzeChangeImpactAsync | O(A×P) 双重循环 | O(A×log P) | 项目目录排序+二分前缀 |
| 6 | ProjectDependencyGraph.cs:163 FindOwningProjectAsync | O(P) 遍历 Projects.Values | O(log P) | 项目目录排序+二分前缀 |

### P2（反向索引）

| # | 位置 | 当前 | 优化后 | 方案 |
|---|------|------|--------|------|
| 7 | ProjectDependencyGraph.cs:55 GetProjectDependentsAsync | O(P×R) 展平扫描 | O(k) | ProjectRefsByTarget 反向索引 |
| 8 | ProjectDependencyGraph.cs:131 GetProjectsUsingNuGetPackageAsync | O(P×N) 展平扫描 | O(k) | NuGetRefsByPackage 反向索引 |

### P3（低优先级）

| # | 位置 | 当前 | 优化后 | 方案 |
|---|------|------|--------|------|
| 9 | GraphAnalytics.cs:600 ExplainAsync 同社区过滤 | O(V) | O(1)+Take(20) | 社区标签按 communityId 分桶 |
| 10 | ProjectDependencyGraph.cs:199 ResolveProjectPath | O(P) 后缀匹配 | O(log P) | 反转路径排序+二分（P小，收益有限） |

### B类（无法排序优化，固有全量成本）

- Contains/正则匹配（SearchAsync/SearchByPatternAsync/QueryAsync）→ 需倒排索引，本次不做
- 聚合/图构建/图遍历/全量导出 → 固有 O(V+E)，可考虑缓存，本次不做

## 调用方适配清单

### 写操作（7处，2文件）

| 文件 | 方法 | 改动 |
|------|------|------|
| SymbolIndex.cs:83 | IndexFileWithContentAsync | EnterWriteLock→Update(snap=>snap.IndexFile(...)) |
| SymbolIndex.cs:109 | IndexFilesBatchAsync | EnterWriteLock→Update(snap=>snap.IndexFilesBatch(...)) |
| SymbolIndex.cs:149 | RemoveFileAsync | EnterWriteLock→Update(snap=>snap.RemoveFile(...)) |
| SymbolIndex.cs:160 | ClearAsync | _store.Clear()→Update(snap=>IndexSnapshot.Empty) |
| ProjectIndex.cs:41 | IndexProjectAsync | EnterWriteLock→Update(snap=>snap.IndexProject(...)) |
| ProjectIndex.cs:82 | RemoveProjectAsync | EnterWriteLock→Update(snap=>snap.RemoveProject(...)) |
| ProjectIndex.cs:92 | ClearAsync | EnterWriteLock→Update(snap=>snap.ClearProjects()) |
| ProjectIndex.cs:116 | IndexProjectWithGuidAsync | EnterWriteLock→Update(snap=>snap.IndexProject(...)) |
| GraphPersistence.cs:98 | LoadAsync | EnterWriteLock→Update(snap=>snap.Load(data)) |

### 读操作（39处，7文件）

统一模式：`using var scope = _store.EnterReadLock(); _store.XXX` → `var snap = _store.GetSnapshot(); snap.XXX`

| 文件 | 读锁数 |
|------|--------|
| SymbolSearcher.cs | 5 |
| IncrementalUpdater.cs | 4 |
| CodeIndexer.cs | 2 |
| CallGraph.cs | 4 |
| DependencyGraph.cs | 4 |
| ProjectDependencyGraph.cs | 8 |
| GraphAnalytics.cs | 10 |
| GraphVisualization.cs | 4 |
| GraphPersistence.cs | 1 (SaveAsync) |
| SymbolIndex.cs | 1 (GetStatsAsync) |

### 测试文件适配

| 文件 | 改动 |
|------|------|
| InMemoryIndexStoreTests.cs | EnterWriteLock/EnterReadLock→GetSnapshot/Update |
| GraphPersistenceTests.cs | 同上 |
| GraphAnalyticsTests.cs | 同上 |
| GraphVisualizationWikiTests.cs | 同上 |
| SymbolIndexTests.cs | 同上 |
| SymbolSearcherTests.cs | 同上 |
| ProjectIndexTests.cs | 同上 |
| ProjectDependencyGraphTests.cs | 同上 |
| CallGraphTests.cs | 同上 |
| DependencyGraphTests.cs | 同上 |
| CodeIndexerTests.cs | 同上 |
| CodeIndexServiceTests.cs | 同上 |
| ConcurrencySafetyTests.cs | 同上 |
| ParallelIndexTests.cs | 同上 |
| IncrementalUpdaterTests.cs | 同上 |
| FileWatcherIntegrationTests.cs | 同上 |

## 实现步骤

1. 创建 IndexSnapshot.cs（16字段 + 排序索引 + 反向索引 + 写操作方法）
2. 重写 InMemoryIndexStore.cs（volatile + Update + GetSnapshot）
3. 适配 SymbolIndex.cs（写逻辑移到 IndexSnapshot）
4. 适配 ProjectIndex.cs（写逻辑移到 IndexSnapshot）
5. 适配 GraphPersistence.cs（Load/Save）
6. 适配读调用方（39处 → GetSnapshot）
7. 适配测试文件
8. 移走 LockScope.cs 到 .xxx
9. 全量编译验证
10. git 提交 + PR

## 保留不改

- CommandExecutionAuditor — SemaphoreSlim 异步锁保护文件 I/O（用户确认正确设计）
- DynamicPluginRegistry — 已改为 CAS 无锁
