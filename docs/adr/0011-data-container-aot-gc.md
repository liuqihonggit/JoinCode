# 0011. 数据容器 AOT+GC 选型

- 状态：accepted
- 日期：2026-08-29
- 决策者：项目架构组

## 背景

项目强制 NativeAOT（见 ADR 0002），数据容器选型需兼顾 AOT 友好性和 GC 释放效率。不同场景（检索优先、有序、高频插入、AOT 不可变查找）需不同容器，错误选型会导致性能问题或 AOT 不兼容。

## 决策

按场景选型：

| 场景 | 选用容器 | 原因 |
|------|----------|------|
| 检索优先（无序） | `Dictionary<K,V>` / `HashSet<T>` | O(1) 查找，GC 释放效率最优 |
| 硬编码有序（如枚举转字典） | `SortedList<K,V>` | 连续内存，查找 O(log n)，插入少 |
| 高频插入 + 有序 | `SortedDictionary<K,V>` | 红黑树，插入删除 O(log n) |
| 尾追加顺序写入 | `T[]` / `List<T>` | 最后才选择，连续内存 |
| AOT 不可变查找集 | `FrozenSet<T>` / `FrozenDictionary<K,V>` | AOT 友好，不可变，O(1) 查找，GC 零分配 |

**禁止行为**：
- ⛔ 禁止 `List<T>` / `T[]` 用作查找集 — `.Contains()` 是 O(n)
- ⛔ 禁止 `static readonly T[]` 用于查找 — 改用 `static readonly FrozenSet<T>`
- ⛔ 禁止内联 `new[] { ... }.Contains()` — 提取为 `static readonly FrozenSet<T>`

**正确模式**：
```csharp
private static readonly FrozenSet<string> ValidModes = FrozenSet.Create(
    StringComparer.OrdinalIgnoreCase, "default", "plan", "auto-accept");
```

## 替代方案

1. **统一用 `Dictionary<K,V>`**：放弃。有序场景需 `SortedList`/`SortedDictionary`，统一会损失有序性或性能。
2. **统一用 `FrozenDictionary`**：放弃。动态场景需可变容器，Frozen 只适合不可变查找集。
3. **用 `ImmutableDictionary`**：放弃。每次修改返回新实例，GC 压力大；Frozen 在 AOT 下更优。

## 后果

- 正面：按场景选型性能最优；AOT 兼容；GC 释放效率可控
- 负面：选型规则需开发者熟悉，新人可能误用 `List<T>.Contains()`
- 中性：配置属性懒加载 FrozenSet 缓存模式（`_filterSet ??= Filters?.ToFrozenSet()`）
