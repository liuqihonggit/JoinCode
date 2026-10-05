# 0119. SwissTable 高性能可变哈希表 — 开放寻址 + SIMD 批量探测

> 📍 **导航**: [docs/](../README.md) › [adr/](README.md)
> 🔗 **上游索引**: [adr/README.md](README.md) — 修改本文档后须同步更新此索引

- 状态：accepted
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

## 压测结果：SwissTable vs FrozenDictionary（2026-10-05 BenchmarkDotNet ShortRunJob）

压测代码：`test/benchmarks/structura.benchmarks/SwissTableVsFrozenBench.cs`，规模 10K/100K，string+int key 双场景。
语义差异：SwissTable 可变（构建后仍可 Add/Remove），FrozenDictionary 构建后不可变（只读）。Add 对比的是构建阶段（Swiss 逐个 Add vs Dictionary 逐个 Add + `ToFrozenDictionary()` 冻结）；读取对比公平。

### 构建（Add）— string key

| Size | SwissTable | FrozenDictionary | 胜者 |
|------|-----------:|----------------:|------|
| 10K | 224 µs / 279 KB | 851 µs / 1097 KB | Swiss 快 3.8×，内存省 3.9× |
| 100K | 3.67 ms / 2.23 MB | 18.6 ms / 11.9 MB | Swiss 快 5.1×，内存省 5.4× |

### 构建（Add）— int key

| Size | SwissTable | FrozenDictionary | 胜者 |
|------|-----------:|----------------:|------|
| 10K | 80 µs / 148 KB | 70 µs / 282 KB | Frozen 快 1.1×，Swiss 内存省 1.9× |
| 100K | 1.06 ms / 1.18 MB | 1.52 ms / 2.97 MB | Swiss 快 1.4×，内存省 2.5× |

### 查找命中 — string key

| Size | SwissTable | FrozenDictionary | 胜者 |
|------|-----------:|----------------:|------|
| 10K | 3.57 ms | 3.25 ms | Frozen 快 1.1× |
| 100K | 8.76 ms | 10.26 ms | Swiss 快 1.2× |

### 查找命中 — int key

| Size | SwissTable | FrozenDictionary | 胜者 |
|------|-----------:|----------------:|------|
| 10K | 642 µs | 169 µs | **Frozen 快 3.8×** |
| 100K | 1.28 ms | 333 µs | **Frozen 快 3.9×** |

### 查找未命中 — string key

| Size | SwissTable | FrozenDictionary | 胜者 |
|------|-----------:|----------------:|------|
| 10K | 144 µs | 13.3 µs | **Frozen 快 10.8×** |
| 100K | 259 µs | 14.0 µs | **Frozen 快 18.5×** |

### 查找未命中 — int key

| Size | SwissTable | FrozenDictionary | 胜者 |
|------|-----------:|----------------:|------|
| 10K | 2.10 ms ⚠️ | 6.8 µs | **Frozen 快 309×**（Swiss 测量不稳定，Error 833µs） |
| 100K | 23.6 µs | 7.5 µs | **Frozen 快 3.1×** |

### ContainsKey — string key

| Size | SwissTable | FrozenDictionary | 胜者 |
|------|-----------:|----------------:|------|
| 10K | 3.58 ms | 3.43 ms | 持平 |
| 100K | 4.46 ms | 5.74 ms | Swiss 快 1.3× |

### 枚举 — string key

| Size | SwissTable | FrozenDictionary | 胜者 |
|------|-----------:|----------------:|------|
| 10K | 15.4 µs | 20.1 µs | Swiss 快 1.3× |
| 100K | 127 µs | 200 µs | Swiss 快 1.6× |

### 枚举 — int key

| Size | SwissTable | FrozenDictionary | 胜者 |
|------|-----------:|----------------:|------|
| 10K | 11.8 µs | 20.4 µs | Swiss 快 1.7× |
| 100K | 110 µs | 204 µs | Swiss 快 1.9× |

### 结论

- **构建阶段 SwissTable 全胜** — 可变字典逐个 Add 是强项，str 快 3.8-5.1× 且内存省 3.9-5.4×；FrozenDictionary 需先建中间 Dictionary 再冻结，构建成本高
- **查找命中 int key FrozenDictionary 大胜（3.8-3.9×）** — FrozenDictionary 对 int key 做了特化布局（`Int32FrozenDictionary`，直接用值作下标），O(1) 无哈希计算
- **查找未命中 FrozenDictionary 大胜（str 10-18×，int 3×）** — FrozenDictionary 的"快速排除"优化极强，未命中路径几乎零成本
- **查找命中 str key 两者持平** — string 哈希计算成本主导，SIMD 探测优势被哈希开销淹没
- **枚举 SwissTable 胜（1.3-1.9×）** — SwissTable 扁平连续内存布局，枚举 cache-friendly；FrozenDictionary 内部布局为查找优化，枚举反而不连续
- **选型建议**：高频增删/构建/枚举 → SwissTable；构建一次后海量只读查找（尤其 int key/未命中多）→ FrozenDictionary
