// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Structura.Collections
{
    internal static partial class SwissTableHelper
    {
        /// <summary>
        /// 根据当前平台 SIMD 支持情况分派在字典中查找指定键，若未找到则返回可插入槽位信息。
        /// </summary>
        /// <typeparam name="TKey">字典键类型。</typeparam>
        /// <typeparam name="TValue">字典值类型。</typeparam>
        /// <param name="dictionary">目标字典。</param>
        /// <param name="key">要查找的键。</param>
        /// <param name="hashOfKey">键的哈希值。</param>
        /// <param name="insertSlot">若键不存在，输出可插入槽位索引；否则输出 -1。</param>
        /// <param name="insertOldCtrl">若键不存在，输出插入槽位的原控制字节；否则输出默认值。</param>
        /// <returns>匹配条目的引用；若未找到则返回 <see cref="Unsafe.NullRef{T}"/>。</returns>
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref SwissTable<TKey, TValue>.Entry DispatchFindForInsert<TKey, TValue>(SwissTable<TKey, TValue> dictionary, TKey key, int hashOfKey, out int insertSlot, out byte insertOldCtrl)
        where TKey : notnull
        {
            if (Avx2.IsSupported)
            {
                return ref FindForInsertForAvx2(dictionary, key, hashOfKey, out insertSlot, out insertOldCtrl);
            }
            else
            if (Sse2.IsSupported)
            {
                return ref FindForInsertForSse2(dictionary, key, hashOfKey, out insertSlot, out insertOldCtrl);
            }
            else
            {
                return ref FindForInsertForFallback(dictionary, key, hashOfKey, out insertSlot, out insertOldCtrl);
            }
        }



        [SkipLocalsInit]
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static unsafe ref SwissTable<TKey, TValue>.Entry FindForInsertForAvx2<TKey, TValue>(SwissTable<TKey, TValue> dictionary, TKey key, int hash,
                    out int insertSlot, out byte insertOldCtrl)
                where TKey : notnull
                {
                    var controls = dictionary.rawTable._controls;
                    var entries = dictionary.rawTable._entries;
                    var bucketMask = dictionary.rawTable._bucket_mask;

                    var hashComparer = dictionary._comparer;

                    insertSlot = -1;
                    insertOldCtrl = default;
                    Debug.Assert(controls != null);

                    var h2_hash = h2(hash);
                    var targetGroup = Avx2Group.Create(h2_hash);
                    var probeSeq = new ProbeSeq(hash, bucketMask);

                    if (hashComparer == null)
                    {
                        if (typeof(TKey).IsValueType)
                        {
                            fixed (byte* ptr = &controls[0])
                            {
                                while (true)
                                {
                                    var group = Avx2Group.Load(ptr + probeSeq.pos);
                                    var bitmask = group.MatchGroup(targetGroup);
                                    // TODO: Iterator and performance, if not influence, iterator would be clearer.
                                    while (bitmask.AnyBitSet())
                                    {
                                        // there must be set bit
                                        Debug.Assert(entries != null);
                                        var bit = bitmask.LowestSetBitNonzero();
                                        bitmask = bitmask.RemoveLowestBit();
                                        var index = (probeSeq.pos + bit) & bucketMask;
                                        ref var entry = ref entries[index];
                                        if (EqualityComparer<TKey>.Default.Equals(key, entry.Key))
                                        {
                                            return ref entry;
                                        }
                                    }
                                    if (insertSlot < 0)
                                    {
                                        var emptyBit = group.MatchEmptyOrDeleted().LowestSetBit();
                                        if (emptyBit >= 0)
                                        {
                                            insertSlot = (probeSeq.pos + emptyBit) & bucketMask;
                                            insertOldCtrl = controls[insertSlot];
                                        }
                                    }
                                    if (group.MatchEmpty().AnyBitSet())
                                    {
                                        return ref Unsafe.NullRef<SwissTable<TKey, TValue>.Entry>();
                                    }
                                    probeSeq.move_next();
                                }
                            }
                        }
                        else
                        {
                            EqualityComparer<TKey> defaultComparer = EqualityComparer<TKey>.Default;
                            fixed (byte* ptr = &controls[0])
                            {
                                while (true)
                                {
                                    var group = Avx2Group.Load(ptr + probeSeq.pos);
                                    var bitmask = group.MatchGroup(targetGroup);
                                    // TODO: Iterator and performance, if not influence, iterator would be clearer.
                                    while (bitmask.AnyBitSet())
                                    {
                                        // there must be set bit
                                        Debug.Assert(entries != null);
                                        var bit = bitmask.LowestSetBitNonzero();
                                        bitmask = bitmask.RemoveLowestBit();
                                        var index = (probeSeq.pos + bit) & bucketMask;
                                        ref var entry = ref entries[index];
                                        if (defaultComparer.Equals(key, entry.Key))
                                        {
                                            return ref entry;
                                        }
                                    }
                                    if (insertSlot < 0)
                                    {
                                        var emptyBit = group.MatchEmptyOrDeleted().LowestSetBit();
                                        if (emptyBit >= 0)
                                        {
                                            insertSlot = (probeSeq.pos + emptyBit) & bucketMask;
                                            insertOldCtrl = controls[insertSlot];
                                        }
                                    }
                                    if (group.MatchEmpty().AnyBitSet())
                                    {
                                        return ref Unsafe.NullRef<SwissTable<TKey, TValue>.Entry>();
                                    }
                                    probeSeq.move_next();
                                }
                            }
                        }
                    }
                    else
                    {
                        fixed (byte* ptr = &controls[0])
                        {
                            while (true)
                            {
                                var group = Avx2Group.Load(ptr + probeSeq.pos);
                                var bitmask = group.MatchGroup(targetGroup);
                                // TODO: Iterator and performance, if not influence, iterator would be clearer.
                                while (bitmask.AnyBitSet())
                                {
                                    // there must be set bit
                                    Debug.Assert(entries != null);
                                    var bit = bitmask.LowestSetBitNonzero();
                                    bitmask = bitmask.RemoveLowestBit();
                                    var index = (probeSeq.pos + bit) & bucketMask;
                                    ref var entry = ref entries[index];
                                    if (hashComparer.Equals(key, entry.Key))
                                    {
                                        return ref entry;
                                    }
                                }
                                if (insertSlot < 0)
                                    {
                                        var emptyBit = group.MatchEmptyOrDeleted().LowestSetBit();
                                        if (emptyBit >= 0)
                                        {
                                            insertSlot = (probeSeq.pos + emptyBit) & bucketMask;
                                            insertOldCtrl = controls[insertSlot];
                                        }
                                    }
                                    if (group.MatchEmpty().AnyBitSet())
                                {
                                    return ref Unsafe.NullRef<SwissTable<TKey, TValue>.Entry>();
                                }
                                probeSeq.move_next();
                            }
                        }
                    }
                }


        [SkipLocalsInit]
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static unsafe ref SwissTable<TKey, TValue>.Entry FindForInsertForSse2<TKey, TValue>(SwissTable<TKey, TValue> dictionary, TKey key, int hash,
                    out int insertSlot, out byte insertOldCtrl)
                where TKey : notnull
                {
                    var controls = dictionary.rawTable._controls;
                    var entries = dictionary.rawTable._entries;
                    var bucketMask = dictionary.rawTable._bucket_mask;

                    var hashComparer = dictionary._comparer;

                    insertSlot = -1;
                    insertOldCtrl = default;
                    Debug.Assert(controls != null);

                    var h2_hash = h2(hash);
                    var targetGroup = Sse2Group.Create(h2_hash);
                    var probeSeq = new ProbeSeq(hash, bucketMask);

                    if (hashComparer == null)
                    {
                        if (typeof(TKey).IsValueType)
                        {
                            fixed (byte* ptr = &controls[0])
                            {
                                while (true)
                                {
                                    var group = Sse2Group.load(ptr + probeSeq.pos);
                                    var bitmask = group.MatchGroup(targetGroup);
                                    // TODO: Iterator and performance, if not influence, iterator would be clearer.
                                    while (bitmask.AnyBitSet())
                                    {
                                        // there must be set bit
                                        Debug.Assert(entries != null);
                                        var bit = bitmask.LowestSetBitNonzero();
                                        bitmask = bitmask.RemoveLowestBit();
                                        var index = (probeSeq.pos + bit) & bucketMask;
                                        ref var entry = ref entries[index];
                                        if (EqualityComparer<TKey>.Default.Equals(key, entry.Key))
                                        {
                                            return ref entry;
                                        }
                                    }
                                    if (insertSlot < 0)
                                    {
                                        var emptyBit = group.MatchEmptyOrDeleted().LowestSetBit();
                                        if (emptyBit >= 0)
                                        {
                                            insertSlot = (probeSeq.pos + emptyBit) & bucketMask;
                                            insertOldCtrl = controls[insertSlot];
                                        }
                                    }
                                    if (group.MatchEmpty().AnyBitSet())
                                    {
                                        return ref Unsafe.NullRef<SwissTable<TKey, TValue>.Entry>();
                                    }
                                    probeSeq.move_next();
                                }
                            }
                        }
                        else
                        {
                            EqualityComparer<TKey> defaultComparer = EqualityComparer<TKey>.Default;
                            fixed (byte* ptr = &controls[0])
                            {
                                while (true)
                                {
                                    var group = Sse2Group.load(ptr + probeSeq.pos);
                                    var bitmask = group.MatchGroup(targetGroup);
                                    // TODO: Iterator and performance, if not influence, iterator would be clearer.
                                    while (bitmask.AnyBitSet())
                                    {
                                        // there must be set bit
                                        Debug.Assert(entries != null);
                                        var bit = bitmask.LowestSetBitNonzero();
                                        bitmask = bitmask.RemoveLowestBit();
                                        var index = (probeSeq.pos + bit) & bucketMask;
                                        ref var entry = ref entries[index];
                                        if (defaultComparer.Equals(key, entry.Key))
                                        {
                                            return ref entry;
                                        }
                                    }
                                    if (insertSlot < 0)
                                    {
                                        var emptyBit = group.MatchEmptyOrDeleted().LowestSetBit();
                                        if (emptyBit >= 0)
                                        {
                                            insertSlot = (probeSeq.pos + emptyBit) & bucketMask;
                                            insertOldCtrl = controls[insertSlot];
                                        }
                                    }
                                    if (group.MatchEmpty().AnyBitSet())
                                    {
                                        return ref Unsafe.NullRef<SwissTable<TKey, TValue>.Entry>();
                                    }
                                    probeSeq.move_next();
                                }
                            }
                        }
                    }
                    else
                    {
                        fixed (byte* ptr = &controls[0])
                        {
                            while (true)
                            {
                                var group = Sse2Group.load(ptr + probeSeq.pos);
                                var bitmask = group.MatchGroup(targetGroup);
                                // TODO: Iterator and performance, if not influence, iterator would be clearer.
                                while (bitmask.AnyBitSet())
                                {
                                    // there must be set bit
                                    Debug.Assert(entries != null);
                                    var bit = bitmask.LowestSetBitNonzero();
                                    bitmask = bitmask.RemoveLowestBit();
                                    var index = (probeSeq.pos + bit) & bucketMask;
                                    ref var entry = ref entries[index];
                                    if (hashComparer.Equals(key, entry.Key))
                                    {
                                        return ref entry;
                                    }
                                }
                                if (insertSlot < 0)
                                    {
                                        var emptyBit = group.MatchEmptyOrDeleted().LowestSetBit();
                                        if (emptyBit >= 0)
                                        {
                                            insertSlot = (probeSeq.pos + emptyBit) & bucketMask;
                                            insertOldCtrl = controls[insertSlot];
                                        }
                                    }
                                    if (group.MatchEmpty().AnyBitSet())
                                {
                                    return ref Unsafe.NullRef<SwissTable<TKey, TValue>.Entry>();
                                }
                                probeSeq.move_next();
                            }
                        }
                    }
                }


        [SkipLocalsInit]
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static unsafe ref SwissTable<TKey, TValue>.Entry FindForInsertForFallback<TKey, TValue>(SwissTable<TKey, TValue> dictionary, TKey key, int hash,
                    out int insertSlot, out byte insertOldCtrl)
                where TKey : notnull
                {
                    var controls = dictionary.rawTable._controls;
                    var entries = dictionary.rawTable._entries;
                    var bucketMask = dictionary.rawTable._bucket_mask;

                    var hashComparer = dictionary._comparer;

                    insertSlot = -1;
                    insertOldCtrl = default;
                    Debug.Assert(controls != null);

                    var h2_hash = h2(hash);
                    var targetGroup = FallbackGroup.create(h2_hash);
                    var probeSeq = new ProbeSeq(hash, bucketMask);

                    if (hashComparer == null)
                    {
                        if (typeof(TKey).IsValueType)
                        {
                            fixed (byte* ptr = &controls[0])
                            {
                                while (true)
                                {
                                    var group = FallbackGroup.load(ptr + probeSeq.pos);
                                    var bitmask = group.MatchGroup(targetGroup);
                                    // TODO: Iterator and performance, if not influence, iterator would be clearer.
                                    while (bitmask.AnyBitSet())
                                    {
                                        // there must be set bit
                                        Debug.Assert(entries != null);
                                        var bit = bitmask.LowestSetBitNonzero();
                                        bitmask = bitmask.RemoveLowestBit();
                                        var index = (probeSeq.pos + bit) & bucketMask;
                                        ref var entry = ref entries[index];
                                        if (EqualityComparer<TKey>.Default.Equals(key, entry.Key))
                                        {
                                            return ref entry;
                                        }
                                    }
                                    if (insertSlot < 0)
                                    {
                                        var emptyBit = group.MatchEmptyOrDeleted().LowestSetBit();
                                        if (emptyBit >= 0)
                                        {
                                            insertSlot = (probeSeq.pos + emptyBit) & bucketMask;
                                            insertOldCtrl = controls[insertSlot];
                                        }
                                    }
                                    if (group.MatchEmpty().AnyBitSet())
                                    {
                                        return ref Unsafe.NullRef<SwissTable<TKey, TValue>.Entry>();
                                    }
                                    probeSeq.move_next();
                                }
                            }
                        }
                        else
                        {
                            EqualityComparer<TKey> defaultComparer = EqualityComparer<TKey>.Default;
                            fixed (byte* ptr = &controls[0])
                            {
                                while (true)
                                {
                                    var group = FallbackGroup.load(ptr + probeSeq.pos);
                                    var bitmask = group.MatchGroup(targetGroup);
                                    // TODO: Iterator and performance, if not influence, iterator would be clearer.
                                    while (bitmask.AnyBitSet())
                                    {
                                        // there must be set bit
                                        Debug.Assert(entries != null);
                                        var bit = bitmask.LowestSetBitNonzero();
                                        bitmask = bitmask.RemoveLowestBit();
                                        var index = (probeSeq.pos + bit) & bucketMask;
                                        ref var entry = ref entries[index];
                                        if (defaultComparer.Equals(key, entry.Key))
                                        {
                                            return ref entry;
                                        }
                                    }
                                    if (insertSlot < 0)
                                    {
                                        var emptyBit = group.MatchEmptyOrDeleted().LowestSetBit();
                                        if (emptyBit >= 0)
                                        {
                                            insertSlot = (probeSeq.pos + emptyBit) & bucketMask;
                                            insertOldCtrl = controls[insertSlot];
                                        }
                                    }
                                    if (group.MatchEmpty().AnyBitSet())
                                    {
                                        return ref Unsafe.NullRef<SwissTable<TKey, TValue>.Entry>();
                                    }
                                    probeSeq.move_next();
                                }
                            }
                        }
                    }
                    else
                    {
                        fixed (byte* ptr = &controls[0])
                        {
                            while (true)
                            {
                                var group = FallbackGroup.load(ptr + probeSeq.pos);
                                var bitmask = group.MatchGroup(targetGroup);
                                // TODO: Iterator and performance, if not influence, iterator would be clearer.
                                while (bitmask.AnyBitSet())
                                {
                                    // there must be set bit
                                    Debug.Assert(entries != null);
                                    var bit = bitmask.LowestSetBitNonzero();
                                    bitmask = bitmask.RemoveLowestBit();
                                    var index = (probeSeq.pos + bit) & bucketMask;
                                    ref var entry = ref entries[index];
                                    if (hashComparer.Equals(key, entry.Key))
                                    {
                                        return ref entry;
                                    }
                                }
                                if (insertSlot < 0)
                                    {
                                        var emptyBit = group.MatchEmptyOrDeleted().LowestSetBit();
                                        if (emptyBit >= 0)
                                        {
                                            insertSlot = (probeSeq.pos + emptyBit) & bucketMask;
                                            insertOldCtrl = controls[insertSlot];
                                        }
                                    }
                                    if (group.MatchEmpty().AnyBitSet())
                                {
                                    return ref Unsafe.NullRef<SwissTable<TKey, TValue>.Entry>();
                                }
                                probeSeq.move_next();
                            }
                        }
                    }
                }

    }
}
