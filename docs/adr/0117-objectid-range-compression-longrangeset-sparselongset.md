# 0117. ObjectId 区间压缩 — LongRangeSet + SparseLongSet + 按类型独立计数

> 📍 **导航**: [docs/](../README.md) › [adr/](README.md)
> 🔗 **上游索引**: [adr/README.md](README.md) — 修改本文档后须同步更新此索引

- 状态：proposed
- 日期：2026-09-26
- 决策者：项目架构组

## 背景

`ObjectIdManager._typeIndex`（`Type → ObjectId` 列表，`ObjectIdManager.cs:10`）和 `PluginManager._pluginResourceIds`（插件名 → ObjectId 列表，`PluginManager.cs:28`）使用 `ImmutableList<ObjectId>` 平铺存储。每个 `ObjectId` 是 readonly struct，含 `Type`(枚举) + `SequenceId`(long) + `UniqueId`(string) + `DisplayName`(string)，实际占 100+ bytes（两个字符串对象是大头）。作为反向索引，只需能反查 `_objects` 即可，不需要重复存字符串。

`ObjectId.SequenceId` 当前是全局原子自增 long（`ObjectId.cs:11` 的 `_globalSequence`），同类型 ObjectId 的 SequenceId 不连续（被其他类型穿插），无法用 `[start~end]` 连续区间表示。

## 决策

1. **引入两个 long 集合压缩数据结构**（放 `lib/abstractions/abs_core/core_utils/core/ranges/`）：
   - `LongRangeSet`：排序不相交区间 `[(start, end)]`，连续段合并为 `[start~end]` 只存 2 个 long。`Add` 合并相邻区间，`Remove` 拆分区间，`Contains` 二分查找。适用于同类型连续 SequenceId。
   - `SparseLongSet`：排序 long + varint delta 编码。相邻 long 差值小则 1 byte，大则 5 bytes。适用于稀疏 long 集合。

2. **`ObjectId.SequenceId` 改为按 ObjectType 独立计数**（`long[]` 21 槽，`Interlocked.Increment` 按类型索引）。同类型 SequenceId 严格连续递增，使 `[start~end]` 区间天然成立。ObjectId 全局唯一性由 `(Type, SequenceId)` 联合保证，不受影响。

3. **`ObjectId` 加内部 lookup 构造函数**（不分配字符串，`UniqueId`/`DisplayName = string.Empty`），用于从 `(ObjectType, SequenceId)` 反查 `_objects` 字典。`ObjectId.Equals`/`GetHashCode` 只看 `Type+SequenceId`，lookup 键与原键 hash/equals 一致。

4. **`ObjectIdManager._typeIndex` 改为 `ImmutableDictionary<Type, LongRangeSet>`**，只存 SequenceId。`GetAll<T>` 枚举区间 SequenceId → 构造 lookup ObjectId → 查 `_objects`。

5. **`PluginManager._pluginResourceIds` 改为按 ObjectType 二级分组 + LongRangeSet**，反查时 `(ObjectType, SequenceId)` 齐全。

## 替代方案

- **保留全局计数 + 稀疏区间**：同类型 SequenceId 稀疏，区间要么包含空洞（反查需过滤）要么拆成单点区间，压缩率低甚至更费内存。放弃。
- **ObjectIdRangeSet（区间端点带 ObjectId）**：端点仍带字符串引用，节省有限。放弃。
- **_typeIndex 只存 long 不用区间**：省字符串但未压缩连续段，N 个 long 仍 8N bytes。区间结构进一步压到 16 bytes/段。采用区间结构更优。

## 后果

- 正面：同类型连续注册时单个区间 `[1~N]` 从 ~100N bytes 压到 16 bytes；字符串只在 `_objects` 存一次，消除反向索引的字符串重复。
- 负面：`EntityObjectIdTests` 跨类型递增断言改为同类型递增断言；`SessionIdFactory` 注释更新（功能不变，单类型使用）。
- 中性：`LongRangeSet` 和 `SparseLongSet` 作为通用工具入库，未来其他 long 集合场景可复用。
