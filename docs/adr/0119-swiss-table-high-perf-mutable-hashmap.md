# 0119. SwissTable 高性能可变哈希表 — 开放寻址 + SIMD 批量探测

> 📍 **导航**: [docs/](../README.md) › [adr/](README.md)
> 🔗 **上游索引**: [adr/README.md](README.md) — 修改本文档后须同步更新此索引

- 状态：proposed
- 日期：2026-09-28
- 决策者：用户（liuqihonggit）+ AI

## 背景

项目已有三种哈希表方案：
- **HAMT**（`ImmutableHamT`）— 不可变持久化，CAS 无锁场景最优
- **FrozenDictionary** — 构造一次后只读，完美 hash O(1) 无碰撞
- **BCL Dictionary** — 可变哈希表，分离 entries/buckets 数组，有额外间接

### 问题

BCL `Dictionary<TKey, TValue>` 在高频增删查场景存在性能瓶颈：
1. **内存不连续** — entries 和 buckets 分离，cache miss 多
2. **无 SIMD 探测** — 逐个比较，未利用硬件向量指令
3. **碰撞链** — 链式解决碰撞，指针追逐 cache 不友好

### SwissTable 方案

Google Abseil `flat_hash_map` 启发的开放寻址哈希表：
1. **扁平化存储** — 元数据表（1 byte/槽）+ 数据表（Entry/槽）连续布局，cache-friendly
2. **SIMD 批量探测** — `Vector128<byte>` 一次比较 16 个元数据字节，快速定位匹配/空槽
3. **元数据编码** — 每槽 1 byte：0x00=空，0x7F=删除标记，1-126=哈希低 7 位
4. **高质量哈希** — `System.HashCode.Combine` 获取哈希，低 7 位存元数据，剩余位用于探测
5. **开放寻址** — 平方探测（quadratic probing）减少聚集

## 决策

### 决策1：实现 `IDictionary<TKey, TValue>` 接口

方便直接替换 `Dictionary<TKey, TValue>`，消费方代码无需改动。

### 决策2：单线程版本

并发场景由消费方加锁或用 `ConcurrentDictionary`。SwissTable 内部不加锁，SIMD 优化无顾虑。

### 决策3：放在 Structura 项目

与 HAMT 同项目，命名空间 `Structura.Collections`。

### 决策4：SIMD 策略

用 `System.Runtime.Intrinsics.Vector128<byte>` 一次比较 16 个元数据字节。.NET 10 硬件加速自动启用，不支持 SIMD 时回退到逐字节比较。

### 决策5：扩容策略

负载因子超过 0.75 时扩容（2 倍），扩容时清理 tombstone。

## 替代方案

| 方案 | 优点 | 缺点 | 放弃原因 |
|------|------|------|---------|
| BCL Dictionary | 成熟稳定 | 无 SIMD，内存不连续 | 性能不够 |
| ConcurrentDictionary | 并发安全 | 分段锁开销，非热点 | 单线程场景过重 |
| HAMT | 不可变 + CAS | 路径复制开销 | 可变场景不合适 |
| FrozenDictionary | 完美 hash O(1) | 构造后不可变 | 不能增删 |

## 验证

- 编译通过（0 警告 0 错误，NativeAOT 兼容）
- 单元测试覆盖 Add/Remove/TryGetValue/ContainsKey/扩容/碰撞
- 基准测试对比 BCL Dictionary（预期查找快 1.5-3x，增删快 1.2-2x）
