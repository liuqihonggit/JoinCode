# 0085. 数据容器选型规范

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
| **硬编码有序（如枚举转字典）** | `SortedList<K,V>` | 连续内存，查找 O(log n)，插入少 |
| **高频插入 + 有序** | `SortedDictionary<K,V>` | 红黑树，插入删除 O(log n) |
| **尾追加顺序写入** | `T[]` / `List<T>` | 最后才选择，连续内存 |
| **AOT不可变查找集** | `FrozenSet<T>` / `FrozenDictionary<K,V>` | AOT友好，不可变，O(1) 查找，GC零分配 |

**容器性能对比**：

| 操作 | `SortedDictionary` (红黑树) | `SortedList` (数组) |
|------|----------------------------|---------------------|
| 插入/删除 | O(log n) ✅ | **O(n)** ❌ (要挪动大量元素) |
| 查找/读取 | O(log n) | O(log n) (二分查找) |
| 内存占用 | 大（每个元素存指针） | **小**（连续内存） |

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
