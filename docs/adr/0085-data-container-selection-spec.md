# 0085. 数据容器选型规范

> 📍 **导航**: [docs/](../README.md) › [adr/](README.md)
> 🔗 **上游索引**: [adr/README.md](README.md) — 修改本文档后须同步更新此索引

- 状态：accepted
- 日期：2026-09-08
- 决策者：项目架构组

## 背景

AOT 编译与 GC 释放效率优先的场景下，不同数据容器的性能特征差异显著。本文档定义各场景下的容器选型规范，禁止用 O(n) 容器做查找集。

## 详细内容

### 数据容器选型规范（AOT编译 + GC释放效率优先）

| 场景 | 选用容器 | 原因 |
|------|----------|------|
| **检索优先（无序）** | `Dictionary<K,V>` / `HashSet<T>` | O(1) 查找，GC释放效率最优 |
| **高频增删查（可变）** | `SwissTable<K,V>` | SIMD 批量探测+连续内存，构建/枚举快，见 ADR 0119 |
| **硬编码有序（如枚举转字典）** | `SortedList<K,V>` | 连续内存，查找 O(log n)，插入少 |
| **高频插入 + 有序** | `SortedDictionary<K,V>` | 红黑树，插入删除 O(log n) |
| **尾追加顺序写入** | `T[]` / `List<T>` | 最后才选择，连续内存 |
| **AOT不可变查找集** | `FrozenSet<T>` / `FrozenDictionary<K,V>` | AOT友好，不可变，O(1) 查找，int key/未命中快 3-18×（见 ADR 0119），但构建慢且不可增删 |

**容器性能对比**：

| 操作 | `SortedDictionary` (红黑树) | `SortedList` (数组) |
|------|----------------------------|---------------------|
| 插入/删除 | O(log n) ✅ | **O(n)** ❌ (要挪动大量元素) |
| 查找/读取 | O(log n) | O(log n) (二分查找) |
| 内存占用 | 大（每个元素存指针） | **小**（连续内存） |

**SwissTable vs FrozenDictionary vs Dictionary 选型**（见 [ADR 0119](0119-swiss-table-high-perf-mutable-hashmap.md) 压测）：

| 场景 | 选用 | 原因 |
|------|------|------|
| 高频增删 + 可变 | `SwissTable<K,V>` | 可变，构建快 3.8-5.1×，枚举快 1.3-1.9× |
| 构建一次 + 海量只读查找（int key / 未命中多） | `FrozenDictionary<K,V>` | int 查找快 3.9×，未命中快 10-18×，但构建慢且不可变 |
| 构建一次 + 海量只读查找（str key 命中） | `FrozenDictionary<K,V>` 或 `Dictionary<K,V>` | str 哈希 O(len) 主导，三者持平 |
| 通用可变字典 | `Dictionary<K,V>` | 成熟稳定，无 SIMD 但够用 |

- **⛔ 禁止 `FrozenDictionary` 做高频增删** — 构建后不可变，每次"改"都要全量重建，性能灾难
- **⛔ 禁止 `SwissTable` 做构建一次后只读查找的 int key 场景** — Frozen 快 3.9×，白丢性能

**禁止行为**：
- **⛔ 禁止 `List<T>` / `T[]` 用作查找集** — `.Contains()` 是 O(n)，高频路径必须用 `HashSet<T>` / `FrozenSet<T>`
- **⛔ 禁止 `static readonly T[]` 用于查找** — 改用 `static readonly FrozenSet<T>`
- **⛔ 禁止内联 `new[] { ... }.Contains()`** — 提取为 `static readonly FrozenSet<T>`

**正确模式**：
```csharp
// 静态查找集 — FrozenSet
private static readonly FrozenSet<string> ValidModes = FrozenSet.Create(
    StringComparer.OrdinalIgnoreCase, "default", "plan", "auto-accept");

// 动态查找集 — HashSet
var scopeSet = new HashSet<string>(scopes, StringComparer.Ordinal);
if (scopeSet.Contains(scope)) ...

// 配置属性懒加载 FrozenSet 缓存
private FrozenSet<string>? _filterSet;
public FrozenSet<string>? FilterSet => _filterSet ??= Filters?.ToFrozenSet();
```

## 替代方案

无。FrozenSet 在 AOT 场景下性能最优，是 .NET 8+ 推荐方案。
