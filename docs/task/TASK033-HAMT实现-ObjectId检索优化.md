# TASK033: HAMT 实现 — ObjectId 检索优化

> 状态: 设计中
> 创建: 2026-09-27
> 关联: DSG021 改造总纲、ADR 0117(ObjectId 区间压缩)

## 1. 背景与动机

### 1.1 问题

`ObjectIdManager` 用 `ImmutableDictionary<ObjectId, object>` 做主存储。.NET 的 `ImmutableDictionary` 内部是**平衡二叉搜索树**(按哈希码排序),不是 HAMT:

| 元素数 | 当前层数 O(log₂ N) | HAMT 层数 O(log₃₂ N) |
|--------|-------------------|---------------------|
| 1K     | 10                | 2                   |
| 10K    | 13                | 3                   |
| 100K   | 17                | 4                   |
| 1M     | 20                | 4                   |

### 1.2 目标

自己实现 `ImmutableHamT<TKey, TValue>`,分支因子 32,替换 ObjectIdManager 内部的 ImmutableDictionary,将查找从 O(log₂ N) 降到 O(log₃₂ N)。

### 1.3 为什么不用第三方库

| 库 | 问题 |
|---|------|
| FSharp.HashCollections | 依赖 FSharp.Core,F# API,2年没更新,GitHub 404 |
| ImmutableTrie | 2018年 alpha,7年没更新,已弃维 |
| NuGet/GitHub 搜索 | 纯 C# HAMT 库 **0 结果** |

## 2. 调研:两个参考实现

### 2.1 FSharp.HashCollections (fsprojects/fsharp-hashcollections)

**节点类型**:
```fsharp
HashTrieNode =
    | TrieNode of TrieNodeContent
    | HashCollisionNode of entries: 'tk list

TrieNodeContent = {
    Nodes: CompressedArray<HashTrieNode>   // 子节点
    Entries: CompressedArray<'tk>          // 叶子条目
}
```

**关键设计**:
- **Nodes/Entries 分离**: 一个内部节点同时存子节点和叶子,减少 1 对 1 的中间节点
- **CompressedArray**: `{ BitMap: uint32; Content: 't[] }` — bitmap 标记哪些位置有元素,Content 只存实际元素
- **全满优化**: `BitMap = MaxValue` 时退化为普通数组直接索引(无需 popcount)
- **PopCount**: `BitOperations.PopCount`(硬件指令)
- **压缩索引计算**: `(bitMap &&& (bitMapIndex - 1)) |> popCount` — bitmap 前缀和

### 2.2 ImmutableTrie (stevenguh/ImmutableTrie)

**节点类型**:
```csharp
internal abstract class NodeBase { }
internal sealed class ValueNode : NodeBase { }           // 叶子(key, value)
internal sealed class BitmapIndexedNode : NodeBase { }   // 稀疏: bitmap + 紧凑数组
internal sealed class HashArrayMapNode : NodeBase { }    // 密集: 固定 32 槽数组
internal sealed class HashCollisionNode : NodeBase { }   // 哈希冲突: ValueNode[]
```

**关键设计**:
- **稀疏↔密集自动切换**:
  - `BitmapIndexedNode` count ≥ 16 → 升级为 `HashArrayMapNode`(固定 32 槽,直接索引)
  - `HashArrayMapNode` count < 8 → 降级为 `BitmapIndexedNode`(bitmap + 紧凑数组)
- **PopCount**: 手写 SWAR(2018 年无 BitOperations)
- **Builder 模式**: owner 标记,批量构建时原地修改避免路径复制
- **哈希冲突用数组**: 比 F# list 缓存友好
- **常量**: BITS=5, WIDTH=32, MAX_BITMAP_INDEXED_SIZE=16, MIN_HASH_ARRAY_MAP_SIZE=8

### 2.3 对比

| 特性 | F# | C# | 选用 |
|------|----|----|------|
| Nodes/Entries 分离 | 是 | 否 | 否(增加复杂度,ObjectId 哈希均匀不需要) |
| 稀疏/密集切换 | 全满优化 | Bitmap↔Array 自动切换 | C#(类型清晰) |
| PopCount | BitOperations(硬件) | 手写 SWAR | F#(硬件指令) |
| 哈希冲突 | F# list | 数组 | C#(缓存友好) |
| Builder | ofSeq 可变 | owner 标记 | 不需要(CAS 模式) |
| 删除路径压缩 | SubNodeChange 枚举 | PackNodes | C#(更清晰) |

## 3. 设计

### 3.1 节点类型

```csharp
namespace JoinCode.Abstractions.Collections;

/// <summary>
/// 不可变 HAMT (Hash Array Mapped Trie) — 分支因子 32,查找 O(log₃₂ N)
/// 不可变 + 路径复制,配合 ImmutableInterlocked.Update 实现无锁 CAS
/// </summary>
public sealed class ImmutableHamT<TKey, TValue> {
    internal const int Bits = 5;
    internal const int Width = 1 << Bits;       // 32
    internal const int Mask = Width - 1;        // 0x1f
    internal const int MaxBitmapSize = Width / 2;  // 16: BitmapNode → ArrayNode
    internal const int MinArraySize = Width / 4;   // 8:  ArrayNode → BitmapNode

    private readonly int _count;
    private readonly Node _root;
    private readonly IEqualityComparer<TKey> _comparer;

    // ... API
}

internal abstract class Node { }

/// <summary>叶子节点 — 存储 key-value 对</summary>
internal sealed class LeafNode : Node {
    internal readonly TKey Key;
    internal readonly TValue Value;
}

/// <summary>稀疏内部节点 — bitmap + 紧凑数组(只存非空位置)</summary>
internal sealed class BitmapNode : Node {
    internal readonly int Bitmap;
    internal readonly Node[] Children;  // 长度 = PopCount(Bitmap)
}

/// <summary>密集内部节点 — 固定 32 槽(null 表示空)</summary>
internal sealed class ArrayNode : Node {
    internal readonly int Count;        // 非空槽数
    internal readonly Node?[] Children; // 长度 = 32
}

/// <summary>哈希冲突节点 — 同哈希不同 key</summary>
internal sealed class CollisionNode : Node {
    internal readonly int Hash;
    internal readonly LeafNode[] Entries;
}
```

### 3.2 API (兼容 ImmutableDictionary,便于替换)

```csharp
public static ImmutableHamT<TKey, TValue> Empty { get; }

public int Count { get; }

// 查找
public bool TryGetValue(TKey key, out TValue value);
public TValue this[TKey key] { get; }  // 不存在抛 KeyNotFoundException

// 不可变操作(返回新实例)
public ImmutableHamT<TKey, TValue> Add(TKey key, TValue value);       // key 已存在抛 ArgumentException
public ImmutableHamT<TKey, TValue> SetItem(TKey key, TValue value);   // Add or update
public ImmutableHamT<TKey, TValue> Remove(TKey key);
public ImmutableHamT<TKey, TValue> Clear();

// 批量
public ImmutableHamT<TKey, TValue> AddRange(IEnumerable<KeyValuePair<TKey, TValue>> items);
public ImmutableHamT<TKey, TValue> SetItems(IEnumerable<KeyValuePair<TKey, TValue>> items);

// 遍历
public IEnumerable<KeyValuePair<TKey, TValue>> GetEnumerator();
public IEnumerable<TKey> Keys { get; }
public IEnumerable<TValue> Values { get; }

// 查询
public bool ContainsKey(TKey key);
public bool Contains(KeyValuePair<TKey, TValue> pair);
```

### 3.3 核心算法

#### 3.3.1 TryGet (查找)

```
TryGet(shift, hash, key):
  match node:
    LeafNode(lk, lv) => key == lk ? lv : not found
    BitmapNode(bitmap, children):
      bit = 1 << ((hash >> shift) & Mask)
      if (bitmap & bit) == 0: not found
      idx = PopCount(bitmap & (bit - 1))  // 压缩索引
      return children[idx].TryGet(shift + 5, hash, key)
    ArrayNode(children):
      idx = (hash >> shift) & Mask
      children[idx]?.TryGet(shift + 5, hash, key) ?? not found
    CollisionNode(h, entries):
      if hash != h: not found
      遍历 entries 找 key
```

#### 3.3.2 Add (插入)

```
Add(shift, hash, key, value):
  match node:
    null => new LeafNode(key, value)

    LeafNode(lk, lv):
      if key == lk: return new LeafNode(key, value)  // 替换
      if hash == hash(lk): return new CollisionNode(hash, [old, new])  // 哈希冲突
      return nestTwoLeaves(shift, oldLeaf, newLeaf)  // 创建中间节点

    BitmapNode(bitmap, children):
      bit = 1 << ((hash >> shift) & Mask)
      idx = PopCount(bitmap & (bit - 1))
      if (bitmap & bit) != 0:
        // 位置已有节点,递归
        newChild = children[idx].Add(shift + 5, hash, key, value)
        return new BitmapNode(bitmap, children.Set(idx, newChild))
      else:
        // 位置空,插入叶子
        newChildren = children.Insert(idx, new LeafNode(key, value))
        newBitmap = bitmap | bit
        if PopCount(newBitmap) >= MaxBitmapSize:
          return upgradeToArrayNode(newBitmap, newChildren)  // 升级
        return new BitmapNode(newBitmap, newChildren)

    ArrayNode(count, children):
      idx = (hash >> shift) & Mask
      if children[idx] == null:
        newChildren = children.Set(idx, new LeafNode(key, value))
        return new ArrayNode(count + 1, newChildren)
      else:
        newChild = children[idx].Add(shift + 5, hash, key, value)
        newChildren = children.Set(idx, newChild)
        return new ArrayNode(count, newChildren)

    CollisionNode(h, entries):
      if hash != h: return nestCollisionWithBitmap(shift, ...)
      // 同哈希,加入冲突数组
      查找 entries 中 key 相同的,替换或追加
```

#### 3.3.3 Remove (删除)

```
Remove(shift, hash, key):
  match node:
    LeafNode(lk, lv) => key == lk ? null : this  // 删除返回 null

    BitmapNode(bitmap, children):
      bit = 1 << ((hash >> shift) & Mask)
      if (bitmap & bit) == 0: return this  // 不存在
      idx = PopCount(bitmap & (bit - 1))
      newChild = children[idx].Remove(shift + 5, hash, key)
      if newChild == children[idx]: return this  // 无变化
      if newChild == null:
        // 删除子节点
        newBitmap = bitmap ^ bit
        newChildren = children.RemoveAt(idx)
        if PopCount(newBitmap) == 0: return null
        if PopCount(newBitmap) == 1:
          // 只剩一个子节点,如果是叶子则提升(路径压缩)
          remainingChild = newChildren[0]
          if remainingChild is LeafNode: return remainingChild
        return new BitmapNode(newBitmap, newChildren)
      else:
        return new BitmapNode(bitmap, children.Set(idx, newChild))

    ArrayNode(count, children):
      idx = (hash >> shift) & Mask
      if children[idx] == null: return this
      newChild = children[idx].Remove(shift + 5, hash, key)
      if newChild == children[idx]: return this
      if newChild == null:
        newChildren = children.Set(idx, null)
        newCount = count - 1
        if newCount < MinArraySize:
          return downgradeToBitmapNode(newCount, newChildren)  // 降级
        return new ArrayNode(newCount, newChildren)
      else:
        return new ArrayNode(count, children.Set(idx, newChild))

    CollisionNode(h, entries):
      if hash != h: return this
      从 entries 移除 key
      if entries.Length == 1: return entries[0]  // 退化回叶子
      if entries.Length == 0: return null
      return new CollisionNode(h, newEntries)
```

#### 3.3.4 路径压缩

删除后如果 BitmapNode 只剩一个子节点且是 LeafNode,提升 LeafNode 到父级,减少中间节点。

#### 3.3.5 稀疏↔密集切换

- **升级** (BitmapNode → ArrayNode): BitmapNode 的 count >= MaxBitmapSize(16) 时,展开为固定 32 槽数组
- **降级** (ArrayNode → BitmapNode): ArrayNode 的 count < MinArraySize(8) 时,压缩为 bitmap + 紧凑数组

### 3.4 PopCount

```csharp
// .NET 8+ 硬件指令,比手写 SWAR 快
using System.Numerics;
int popcount = BitOperations.PopCount((uint)bitmap);
```

### 3.5 哈希

- 32 位哈希(`int`),每层取 5 bit,最多 7 层(35 bit > 32 bit)
- `shift` 从 0 开始,每层 +5,最大 30
- 超过 30 仍有哈希冲突 → CollisionNode

### 3.6 不可变 + CAS

所有修改返回新实例(路径复制),配合:
```csharp
ImmutableInterlocked.Update(ref _field, d => d.Add(key, value));
var snapshot = Volatile.Read(ref _field);
snapshot.TryGetValue(key, out var value);
```

## 4. 实现计划

### 4.1 文件结构

```
lib/abstractions/abs_core/core_utils/core/hamt/
  ImmutableHamT.cs              — 公开 API + 常量
  ImmutableHamT.Node.cs        — 节点类型(Leaf/Bitmap/Array/Collision)
  ImmutableHamT.Operations.cs  — Add/Remove/TryGet/遍历
  ImmutableHamT.Builder.cs     — 批量构建(可选,后续添加)
```

### 4.2 步骤

| 步骤 | 内容 | 验证 |
|------|------|------|
| S1 | 节点类型定义 + 常量 | 编译通过 |
| S2 | TryGet 查找 | 单元测试:空表/单元素/多元素/哈希冲突 |
| S3 | Add 插入(含冲突处理+升级) | 单元测试:插入/替换/冲突/升级阈值 |
| S4 | Remove 删除(含路径压缩+降级) | 单元测试:删除/不存在/压缩/降级阈值 |
| S5 | 遍历 GetEnumerator | 单元测试:遍历顺序/数量正确 |
| S6 | 批量 API AddRange/SetItems | 单元测试 |
| S7 | 替换 ObjectIdManager 内部存储 | 编译+现有测试通过 |
| S8 | Benchmark 对比 ImmutableDictionary | 性能验证 |

### 4.3 测试策略

- **正确性**: 与 ImmutableDictionary 交叉验证 — 随机操作序列(Add/Remove/TryGet),两个实现结果一致
- **边界**: 空表、单元素、全满(32个同前缀)、哈希冲突(同哈希不同key)、深层嵌套(7层)
- **性能**: BenchmarkDotNet 对比 ImmutableDictionary,验证 Contains/Add/Remove 快约一倍

### 4.4 ObjectIdManager 改造

```csharp
// 改造前
private static ImmutableDictionary<ObjectId, object> _objects = ImmutableDictionary<ObjectId, object>.Empty;

// 改造后
private static ImmutableHamT<ObjectId, object> _objects = ImmutableHamT<ObjectId, object>.Empty;
```

API 兼容,只需改类型声明 + `ImmutableInterlocked.Update` 的 lambda 参数类型。

## 5. 风险与缓解

| 风险 | 缓解 |
|------|------|
| 删除路径压缩逻辑复杂 | 参考 C# ImmutableTrie 的 PackNodes,4 种情况穷举 |
| 稀疏/密集切换阈值不准 | 先用 16/8(参考值),benchmark 后调优 |
| 哈希冲突处理遗漏 | 交叉验证测试(随机操作 vs ImmutableDictionary) |
| NativeAOT 兼容 | 纯 C# 无反射 emit,天然兼容 |
| 替换后现有测试失败 | S7 只改类型声明,API 兼容,应无破坏 |

## 6. 验收标准

- [ ] 编译通过(含 NativeAOT)
- [ ] 交叉验证测试通过(1000 次随机操作 vs ImmutableDictionary)
- [ ] ObjectIdManager 现有测试全部通过
- [ ] Benchmark 显示 Contains 快 ≥1.5x,Add 快 ≥1.5x(vs ImmutableDictionary)
- [ ] 代码无注释(遵循项目风格)

## 7. 决策记录

<!-- 🤖 Auto Decision: 2026-09-27 -->
<!-- 决策: 自己实现 HAMT,融合 F#(BitOperations.PopCount) + C#(稀疏/密集切换+数组冲突) 优点 -->
<!-- 原因: .NET 生态无成熟纯 C# HAMT 库,F# 库依赖 FSharp.Core 有 AOT 风险,ImmutableTrie 已弃维 -->
<!-- 替代方案: 分桶+数组(O(1)但只适用连续key)、引 F# 库(AOT 风险)、保持 ImmutableDictionary(不改进) -->
<!-- 验证: 调研完成,设计文档已写,待 CI 通过后实现 -->
