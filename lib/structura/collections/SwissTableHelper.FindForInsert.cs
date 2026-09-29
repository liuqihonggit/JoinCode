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
            => ref ProbeCoreFindForInsert<Avx2Group, Avx2BitMask, TKey, TValue>(dictionary, key, hash, out insertSlot, out insertOldCtrl);

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe ref SwissTable<TKey, TValue>.Entry FindForInsertForSse2<TKey, TValue>(SwissTable<TKey, TValue> dictionary, TKey key, int hash,
            out int insertSlot, out byte insertOldCtrl)
            where TKey : notnull
            => ref ProbeCoreFindForInsert<Sse2Group, Sse2BitMask, TKey, TValue>(dictionary, key, hash, out insertSlot, out insertOldCtrl);

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe ref SwissTable<TKey, TValue>.Entry FindForInsertForFallback<TKey, TValue>(SwissTable<TKey, TValue> dictionary, TKey key, int hash,
            out int insertSlot, out byte insertOldCtrl)
            where TKey : notnull
            => ref ProbeCoreFindForInsert<FallbackGroup, FallbackBitMask, TKey, TValue>(dictionary, key, hash, out insertSlot, out insertOldCtrl);

        /// <summary>
        /// 探测核心泛型实现:在字典中查找指定键,若未找到则记录可插入槽位。
        /// 通过 <see cref="IGroup{BitMaskImpl, GroupImpl}"/> 的 static abstract 成员实现 SIMD 去虚化,
        /// JIT 为每个 <typeparamref name="TGroup"/> 特化生成专门代码,零虚调用开销。
        /// </summary>
        /// <typeparam name="TGroup">探测组实现类型(Avx2Group/Sse2Group/FallbackGroup)。</typeparam>
        /// <typeparam name="TBitMask">位掩码实现类型。</typeparam>
        /// <typeparam name="TKey">字典键类型。</typeparam>
        /// <typeparam name="TValue">字典值类型。</typeparam>
        /// <param name="dictionary">目标字典。</param>
        /// <param name="key">要查找的键。</param>
        /// <param name="hash">键的哈希值。</param>
        /// <param name="insertSlot">若键不存在,输出可插入槽位索引;否则输出 -1。</param>
        /// <param name="insertOldCtrl">若键不存在,输出插入槽位的原控制字节;否则输出默认值。</param>
        /// <returns>匹配条目的引用;若未找到则返回 <see cref="Unsafe.NullRef{T}"/>。</returns>
        [SkipLocalsInit]
        internal static unsafe ref SwissTable<TKey, TValue>.Entry ProbeCoreFindForInsert<TGroup, TBitMask, TKey, TValue>(
            SwissTable<TKey, TValue> dictionary, TKey key, int hash,
            out int insertSlot, out byte insertOldCtrl)
            where TGroup : unmanaged, IGroup<TBitMask, TGroup>
            where TBitMask : unmanaged, IBitMask<TBitMask>
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
            var targetGroup = TGroup.Create(h2_hash);
            var probeSeq = new ProbeSeq(hash, bucketMask);

            fixed (byte* ptr = &controls[0])
            {
                if (hashComparer == null)
                {
                    if (typeof(TKey).IsValueType)
                    {
                        while (true)
                        {
                            var group = TGroup.Load(ptr + probeSeq.pos);
                            var bitmask = group.MatchGroup(targetGroup);
                            while (bitmask.AnyBitSet())
                            {
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
                    else
                    {
                        EqualityComparer<TKey> defaultComparer = EqualityComparer<TKey>.Default;
                        while (true)
                        {
                            var group = TGroup.Load(ptr + probeSeq.pos);
                            var bitmask = group.MatchGroup(targetGroup);
                            while (bitmask.AnyBitSet())
                            {
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
                else
                {
                    while (true)
                    {
                        var group = TGroup.Load(ptr + probeSeq.pos);
                        var bitmask = group.MatchGroup(targetGroup);
                        while (bitmask.AnyBitSet())
                        {
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
