# DSG036: VECIDX5 分页索引格式 + 跳表增量索引

## 目标

在 VECIDX4 基础上改为分页存储 + 跳表索引表，支持增量索引（只更新变化的页）。

## VECIDX5 文件格式

```
[Magic: 8B "VECIDX5\0"]
[Header: 48B VectorIndexHeaderV5]
[跳表区: 跳表序列化 — 块ID → 页编号+页内索引]
[数据页区: 多个 DataPage]
  每个 DataPage:
    [PageHeader: 48B — 页编号、块数、各段偏移]
    [向量数据: count * dims * 4B]
    [MetaEntryFixed[]: count * 112B]
    [字符串区: 变长]
[SourceText段: 变长]
[图段: 变长]
```

## 结构体定义

### VectorIndexHeaderV5 (48B, Pack=8)
| 字段 | 类型 | 说明 |
|------|------|------|
| PageCapacity | int | 每页块数（默认 256） |
| PageCount | int | 页数 |
| TotalCount | int | 总块数 |
| Dims | int | 向量维度 |
| SkipListOffset | long | 跳表区偏移 |
| PagesOffset | long | 数据页区偏移 |
| SourceOffset | long | SourceText 段偏移 |
| GraphOffset | long | 图段偏移 |

### PageHeader (48B, Pack=8)
| 字段 | 类型 | 说明 |
|------|------|------|
| PageNo | int | 页编号 |
| Count | int | 页内块数 |
| Dims | int | 向量维度 |
| _padding | int | 对齐 |
| VectorOffset | long | 向量数据绝对偏移 |
| MetaOffset | long | MetaEntryFixed[] 绝对偏移 |
| StringOffset | long | 字符串区绝对偏移 |
| StringLen | long | 字符串区长度 |

## 跳表持久化格式

```
[节点数: 4B]
[每个节点: keyLen(4B) + key(keyLen) + pageNo(4B) + pageIndex(4B)]
```

加载时 mmap 映射，重建内存跳表，指向 mmap 数据页（零拷贝）。

## 增量索引流程

1. **加载**：mmap 映射 → 重建跳表 → 跳表指向 mmap 数据页
2. **新增块**：
   a. 找到块所属页（通过跳表或文件路径范围查询）
   b. 页有空间 → 更新页内数据（整页重写）
   c. 页满 → 创建新页（追加到文件末尾）
   d. 更新跳表
3. **修改块**：标记旧块删除 → 写入新页 → 更新跳表
4. **删除块**：从页中删除（整页重写）→ 更新跳表
5. **持久化**：跳表 + 变化的页

## 分页策略

- **固定块数/页**：默认 256 块/页
- 页内向量+Meta 连续存储（MemoryMarshal.Cast 零拷贝）
- 增量更新整页重写（页小，写快）

## 验收标准

- [ ] VECIDX5 分页格式 SaveAsync/LoadAsync
- [ ] 跳表持久化 + mmap 重建
- [ ] 增量索引：只更新变化的页
- [ ] ADR 0080 手动 exe 验收
