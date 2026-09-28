// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


#pragma warning disable CA1810 // Initialize reference type static fields inline

namespace Structura.Collections
{
    // Probe sequence based on triangular numbers, which is guaranteed (since our
    // table size is a power of two) to visit every group of elements exactly once.
    //
    // A triangular probe has us jump by 1 more group every time. So first we
    // jump by 1 group (meaning we just continue our linear scan), then 2 groups
    // (skipping over 1 group), then 3 groups (skipping over 2 groups), and so on.
    //
    // The proof is a simple number theory question: i*(i+1)/2 can walk through the complete residue system of 2n
    // to prove this, we could prove when "0 <= i <= j < 2n", "i * (i + 1) / 2 mod 2n == j * (j + 1) / 2" iff "i == j"
    // sufficient: we could have `(i-j)(i+j+1)=4n*k`, k is integer. It is obvious that if i!=j, the left part is odd, but right is always even.
    // So, the the only chance is i==j
    // necessary: obvious
    // Q.E.D.
    internal struct ProbeSeq
    {
        internal int pos;
        private int _stride;
        private readonly int _bucket_mask;

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ProbeSeq(int hash, int bucket_mask)
        {
            this._bucket_mask = bucket_mask;
            this.pos = SwissTableHelper.h1(hash) & bucket_mask;
            this._stride = 0;
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void move_next()
        {
            // We should have found an empty bucket by now and ended the probe.
            Debug.Assert(this._stride <= _bucket_mask, "Went past end of probe sequence");
            this._stride += SwissTableHelper.GROUP_WIDTH;
            this.pos += this._stride;
            this.pos &= _bucket_mask;
        }
    }

    internal static partial class SwissTableHelper
    {
        /// <summary>
        /// 当前平台 SIMD 分组宽度（AVX2 为 32，SSE2 为 16，回退实现为 8）。
        /// </summary>
        public static readonly int GROUP_WIDTH = InitialGroupWidth();

        /// <summary>
        /// 根据当前平台 SIMD 支持情况返回初始分组宽度。
        /// </summary>
        /// <returns>AVX2 支持时返回 32；否则 SSE2 支持时返回 16；否则返回回退实现的宽度 8。</returns>
        public static int InitialGroupWidth()
        {
            if (Avx2.IsSupported)
            {
                return Avx2Group.WIDTH;
            }
            else
            if (Sse2.IsSupported)
            {
                return Sse2Group.WIDTH;
            }
            else
            {
                return FallbackGroup.WIDTH;
            }
        }

        /// Control byte value for an empty bucket.
        public const byte EMPTY = 0b1111_1111;

        /// Control byte value for a deleted bucket.
        public const byte DELETED = 0b1000_0000;

        /// Checks whether a control byte represents a full bucket (top bit is clear).
        public static bool is_full(byte ctrl) => (ctrl & 0x80) == 0;

        /// Checks whether a control byte represents a special value (top bit is set).
        public static bool is_special(byte ctrl) => (ctrl & 0x80) != 0;

        /// Checks whether a special control value is EMPTY (just check 1 bit).
        public static bool special_is_empty(byte ctrl)
        {
            Debug.Assert(is_special(ctrl));
            return (ctrl & 0x01) != 0;
        }

        /// Checks whether a special control value is EMPTY.
        // optimise: return 1 as true, 0 as false
        public static int special_is_empty_with_int_return(byte ctrl)
        {
            Debug.Assert(is_special(ctrl));
            return ctrl & 0x01;
        }

        /// Primary hash function, used to select the initial bucket to probe from.
        public static int h1(int hash)
        {
            return hash;
        }

        /// Secondary hash function, saved in the low 7 bits of the control byte.
        public static byte h2(int hash)
        {
            // Grab the top 7 bits of the hash.
            // cast to uint to use `shr` rahther than `sar`, which makes sure the top bit of returned byte is 0.
            var top7 = (uint)hash >> 25;
            return (byte)top7;
        }

        // DISPATHCH METHODS

        // Generally we do not want to duplicate code, but for performance(use struct and inline), we have to do so.
        // The difference between mirror implmentations should only be `_dummyGroup` except `MoveNext`, in which we use C++ union trick

        // For enumerator, which need record the current state
        [StructLayout(LayoutKind.Explicit)]
        internal struct BitMaskUnion
        {
            [FieldOffset(0)]
            internal Avx2BitMask avx2BitMask;
            [FieldOffset(0)]
            internal Sse2BitMask sse2BitMask;
            [FieldOffset(0)]
            internal FallbackBitMask fallbackBitMask;
        }

        // maybe we should just pass bucket_mask in as parater rather than calculate
        private static int GetBucketMaskFromControlsLength(int controlsLength)
        {
            Debug.Assert(controlsLength >= GROUP_WIDTH);
            if (controlsLength == GROUP_WIDTH)
                return 0;
            else
                return controlsLength - GROUP_WIDTH - 1;
        }

        /// <summary>
        /// 根据当前平台 SIMD 支持情况返回空控制字节数组。
        /// </summary>
        /// <returns>填充 <see cref="EMPTY"/> 的控制字节数组。</returns>
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] DispatchGetEmptyControls()
        {
            if (Avx2.IsSupported)
            {
                return Avx2Group.StaticEmpty;
            }
            else
            if (Sse2.IsSupported)
            {
                return Sse2Group.StaticEmpty;
            }
            else
            {
                return FallbackGroup.StaticEmpty;
            }
        }

        /// <summary>
        /// 根据当前平台 SIMD 支持情况分派加载控制字节组并返回"满桶"匹配位掩码。
        /// </summary>
        /// <param name="controls">控制字节数组。</param>
        /// <param name="index">控制字节起始偏移。</param>
        /// <returns>包含满桶匹配位掩码的 <see cref="BitMaskUnion"/>。</returns>
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BitMaskUnion DispatchGetMatchFullBitMask(byte[] controls, int index)
        {
            if (Avx2.IsSupported)
            {
                return GetMatchFullBitMaskForAvx2(controls, index);
            }
            else
            if (Sse2.IsSupported)
            {
                return GetMatchFullBitMaskForSse2(controls, index);
            }
            else
            {
                return GetMatchFullBitMaskForFallback(controls, index);
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe BitMaskUnion GetMatchFullBitMaskForAvx2(byte[] controls, int index)
        {
            BitMaskUnion result = default;
            fixed (byte* ctrl = &controls[index])
            {
                result.avx2BitMask = Avx2Group.Load(ctrl).MatchFull();
            }
            return result;
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe BitMaskUnion GetMatchFullBitMaskForSse2(byte[] controls, int index)
        {
            BitMaskUnion result = default;
            fixed (byte* ctrl = &controls[index])
            {
                result.sse2BitMask = Sse2Group.Load(ctrl).MatchFull();
            }
            return result;
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe BitMaskUnion GetMatchFullBitMaskForFallback(byte[] controls, int index)
        {
            BitMaskUnion result = default;
            fixed (byte* ctrl = &controls[index])
            {
                result.fallbackBitMask = FallbackGroup.Load(ctrl).MatchFull();
            }
            return result;
        }

        /// <summary>
        /// 根据当前平台 SIMD 支持情况分派推进字典枚举器到下一个有效条目。
        /// </summary>
        /// <typeparam name="TKey">字典键类型。</typeparam>
        /// <typeparam name="TValue">字典值类型。</typeparam>
        /// <param name="version">枚举开始时记录的版本号。</param>
        /// <param name="tolerantVersion">枚举开始时记录的容忍版本号。</param>
        /// <param name="dictionary">目标字典。</param>
        /// <param name="currentCtrlOffset">当前控制字节偏移（引用传递，会被推进）。</param>
        /// <param name="currentBitMask">当前位掩码（引用传递，会被更新）。</param>
        /// <returns>下一个有效条目的引用；若已遍历结束则返回 <see cref="Unsafe.NullRef{T}"/>。</returns>
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref SwissTable<TKey, TValue>.Entry DispatchMoveNextDictionary<TKey, TValue>(
            int version,
            int tolerantVersion,
            in SwissTable<TKey, TValue> dictionary,
            ref int currentCtrlOffset,
            ref BitMaskUnion currentBitMask
            )
            where TKey : notnull
        {
            if (Avx2.IsSupported)
            {
                return ref MoveNextDictionaryForAvx2(version, tolerantVersion, in dictionary, ref currentCtrlOffset, ref currentBitMask);
            }
            else
            if (Sse2.IsSupported)
            {
                return ref MoveNextDictionaryForSse2(version, tolerantVersion, in dictionary, ref currentCtrlOffset, ref currentBitMask);
            }
            else
            {
                return ref MoveNextDictionaryForFallback(version, tolerantVersion, in dictionary, ref currentCtrlOffset, ref currentBitMask);
            }
        }

        /// <summary>
        /// 使用 AVX2 指令推进字典枚举器到下一个有效条目。
        /// </summary>
        /// <typeparam name="TKey">字典键类型。</typeparam>
        /// <typeparam name="TValue">字典值类型。</typeparam>
        /// <param name="version">枚举开始时记录的版本号。</param>
        /// <param name="tolerantVersion">枚举开始时记录的容忍版本号。</param>
        /// <param name="dictionary">目标字典。</param>
        /// <param name="currentCtrlOffset">当前控制字节偏移（引用传递，会被推进）。</param>
        /// <param name="currentBitMask">当前位掩码（引用传递，会被更新）。</param>
        /// <returns>下一个有效条目的引用；若已遍历结束则返回 <see cref="Unsafe.NullRef{T}"/>。</returns>
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref SwissTable<TKey, TValue>.Entry MoveNextDictionaryForAvx2<TKey, TValue>(
            int version,
            int tolerantVersion,
            in SwissTable<TKey, TValue> dictionary,
            ref int currentCtrlOffset,
            ref BitMaskUnion currentBitMask
            )
            where TKey : notnull
        {
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;

            ref var realBitMask = ref currentBitMask.avx2BitMask;

            if (version != dictionary._version)
            {
                ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
            }
            if (tolerantVersion != dictionary._tolerantVersion)
            {
                var newBitMask = GetMatchFullBitMaskForAvx2(controls, currentCtrlOffset).avx2BitMask;
                realBitMask = realBitMask.And(newBitMask);
            }

            while (true)
            {
                var lowest_set_bit = realBitMask.LowestSetBit();
                if (lowest_set_bit >= 0)
                {
                    Debug.Assert(entries != null);
                    realBitMask = realBitMask.RemoveLowestBit();
                    ref var entry = ref entries[currentCtrlOffset + lowest_set_bit];
                    return ref entry;
                }
                currentCtrlOffset += GROUP_WIDTH;
                if (currentCtrlOffset >= dictionary._buckets)
                {
                    return ref Unsafe.NullRef<SwissTable<TKey, TValue>.Entry>();
                }
                realBitMask = GetMatchFullBitMaskForAvx2(controls, currentCtrlOffset).avx2BitMask;
            }
        }

        /// <summary>
        /// 使用 SSE2 指令推进字典枚举器到下一个有效条目。
        /// </summary>
        /// <typeparam name="TKey">字典键类型。</typeparam>
        /// <typeparam name="TValue">字典值类型。</typeparam>
        /// <param name="version">枚举开始时记录的版本号。</param>
        /// <param name="tolerantVersion">枚举开始时记录的容忍版本号。</param>
        /// <param name="dictionary">目标字典。</param>
        /// <param name="currentCtrlOffset">当前控制字节偏移（引用传递，会被推进）。</param>
        /// <param name="currentBitMask">当前位掩码（引用传递，会被更新）。</param>
        /// <returns>下一个有效条目的引用；若已遍历结束则返回 <see cref="Unsafe.NullRef{T}"/>。</returns>
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref SwissTable<TKey, TValue>.Entry MoveNextDictionaryForSse2<TKey, TValue>(
            int version,
            int tolerantVersion,
            in SwissTable<TKey, TValue> dictionary,
            ref int currentCtrlOffset,
            ref BitMaskUnion currentBitMask
            )
            where TKey : notnull
        {
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;

            ref var realBitMask = ref currentBitMask.sse2BitMask;

            if (version != dictionary._version)
            {
                ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
            }
            if (tolerantVersion != dictionary._tolerantVersion)
            {
                var newBitMask = GetMatchFullBitMaskForSse2(controls, currentCtrlOffset).sse2BitMask;
                realBitMask = realBitMask.And(newBitMask);
            }
            while (true)
            {
                var lowest_set_bit = realBitMask.LowestSetBit();
                if (lowest_set_bit >= 0)
                {
                    Debug.Assert(entries != null);
                    realBitMask = realBitMask.RemoveLowestBit();
                    ref var entry = ref entries[currentCtrlOffset + lowest_set_bit];
                    return ref entry;
                }

                currentCtrlOffset += GROUP_WIDTH;
                if (currentCtrlOffset >= dictionary._buckets)
                {
                    return ref Unsafe.NullRef<SwissTable<TKey, TValue>.Entry>();
                }
                realBitMask = GetMatchFullBitMaskForSse2(controls, currentCtrlOffset).sse2BitMask;
            }
        }

        /// <summary>
        /// 使用回退实现（无 SIMD）推进字典枚举器到下一个有效条目。
        /// </summary>
        /// <typeparam name="TKey">字典键类型。</typeparam>
        /// <typeparam name="TValue">字典值类型。</typeparam>
        /// <param name="version">枚举开始时记录的版本号。</param>
        /// <param name="tolerantVersion">枚举开始时记录的容忍版本号。</param>
        /// <param name="dictionary">目标字典。</param>
        /// <param name="currentCtrlOffset">当前控制字节偏移（引用传递，会被推进）。</param>
        /// <param name="currentBitMask">当前位掩码（引用传递，会被更新）。</param>
        /// <returns>下一个有效条目的引用；若已遍历结束则返回 <see cref="Unsafe.NullRef{T}"/>。</returns>
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref SwissTable<TKey, TValue>.Entry MoveNextDictionaryForFallback<TKey, TValue>(
            int version,
            int tolerantVersion,
            in SwissTable<TKey, TValue> dictionary,
            ref int currentCtrlOffset,
            ref BitMaskUnion currentBitMask
            )
            where TKey : notnull
        {
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;

            ref var realBitMask = ref currentBitMask.fallbackBitMask;

            if (version != dictionary._version)
            {
                ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
            }
            if (tolerantVersion != dictionary._tolerantVersion)
            {
                var newBitMask = GetMatchFullBitMaskForFallback(controls, currentCtrlOffset);
                realBitMask = realBitMask.And(newBitMask.fallbackBitMask);
            }
            while (true)
            {
                var lowest_set_bit = realBitMask.LowestSetBit();
                if (lowest_set_bit >= 0)
                {
                    Debug.Assert(entries != null);
                    realBitMask = realBitMask.RemoveLowestBit();
                    ref var entry = ref entries[currentCtrlOffset + lowest_set_bit];
                    return ref entry;
                }

                currentCtrlOffset += GROUP_WIDTH;
                if (currentCtrlOffset >= dictionary._buckets)
                {
                    return ref Unsafe.NullRef<SwissTable<TKey, TValue>.Entry>();
                }
                realBitMask = GetMatchFullBitMaskForFallback(controls, currentCtrlOffset).fallbackBitMask;
            }
        }

        // If we are inside a continuous block of Group::WIDTH full or deleted
        // cells then a probe window may have seen a full block when trying to
        // insert. We therefore need to keep that block non-empty so that
        // lookups will continue searching to the next probe window.
        //
        // Note that in this context `leading_zeros` refers to the bytes at the
        // end of a group, while `trailing_zeros` refers to the bytes at the
        // begining of a group.
        /// <summary>
        /// 根据当前平台 SIMD 支持情况分派判断擦除某桶后是否可安全地将其控制字节置为 EMPTY。
        /// </summary>
        /// <param name="bucketMask">桶位掩码。</param>
        /// <param name="controls">控制字节数组。</param>
        /// <param name="index">待擦除桶的索引。</param>
        /// <returns>若可安全置为 EMPTY 返回 <c>true</c>；否则返回 <c>false</c>。</returns>
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool DispatchIsEraseSafeToSetEmptyControlFlag(int bucketMask, byte[] controls, int index)
        {
            if (Avx2.IsSupported)
            {
                return IsEraseSafeToSetEmptyControlFlagForAvx2(bucketMask, controls, index);
            }
            else
            if (Sse2.IsSupported)
            {
                return IsEraseSafeToSetEmptyControlFlagForSse2(bucketMask, controls, index);
            }
            else
            {
                return IsEraseSafeToSetEmptyControlFlagForFallback(bucketMask, controls, index);
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe bool IsEraseSafeToSetEmptyControlFlagForAvx2(int bucketMask, byte[] controls, int index)
        {
            Debug.Assert(bucketMask == GetBucketMaskFromControlsLength(controls.Length));
            int indexBefore = unchecked((index - GROUP_WIDTH) & bucketMask);
            fixed (byte* ptr_before = &controls[indexBefore])
            fixed (byte* ptr = &controls[index])
            {
                var empty_before = Avx2Group.Load(ptr_before).MatchEmpty();
                var empty_after = Avx2Group.Load(ptr).MatchEmpty();
                return empty_before.LeadingZeros() + empty_after.TrailingZeros() < GROUP_WIDTH;
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe bool IsEraseSafeToSetEmptyControlFlagForSse2(int bucketMask, byte[] controls, int index)
        {
            Debug.Assert(bucketMask == GetBucketMaskFromControlsLength(controls.Length));
            int indexBefore = unchecked((index - GROUP_WIDTH) & bucketMask);
            fixed (byte* ptr_before = &controls[indexBefore])
            fixed (byte* ptr = &controls[index])
            {
                var empty_before = Sse2Group.Load(ptr_before).MatchEmpty();
                var empty_after = Sse2Group.Load(ptr).MatchEmpty();
                return empty_before.LeadingZeros() + empty_after.TrailingZeros() < GROUP_WIDTH;
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe bool IsEraseSafeToSetEmptyControlFlagForFallback(int bucketMask, byte[] controls, int index)
        {
            Debug.Assert(bucketMask == GetBucketMaskFromControlsLength(controls.Length));
            int indexBefore = unchecked((index - GROUP_WIDTH) & bucketMask);
            fixed (byte* ptr_before = &controls[indexBefore])
            fixed (byte* ptr = &controls[index])
            {
                var empty_before = FallbackGroup.Load(ptr_before).MatchEmpty();
                var empty_after = FallbackGroup.Load(ptr).MatchEmpty();
                return empty_before.LeadingZeros() + empty_after.TrailingZeros() < GROUP_WIDTH;
            }
        }

        /// <summary>
        /// 根据当前平台 SIMD 支持情况分派在字典中查找指定键对应的桶条目引用。
        /// </summary>
        /// <typeparam name="TKey">字典键类型。</typeparam>
        /// <typeparam name="TValue">字典值类型。</typeparam>
        /// <param name="dictionary">目标字典。</param>
        /// <param name="key">要查找的键。</param>
        /// <param name="hashOfKey">键的哈希值。</param>
        /// <returns>匹配条目的引用；若未找到则返回 <see cref="Unsafe.NullRef{T}"/>。</returns>
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref SwissTable<TKey, TValue>.Entry DispatchFindBucketOfDictionary<TKey, TValue>(SwissTable<TKey, TValue> dictionary, TKey key, int hashOfKey)
        where TKey : notnull
        {
            if (Avx2.IsSupported)
            {
                return ref FindBucketOfDictionaryForAvx2(dictionary, key, hashOfKey);
            }
            else
            if (Sse2.IsSupported)
            {
                return ref FindBucketOfDictionaryForSse2(dictionary, key, hashOfKey);
            }
            else
            {
                return ref FindBucketOfDictionaryForFallback(dictionary, key, hashOfKey);
            }
        }


        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe ref SwissTable<TKey, TValue>.Entry FindBucketOfDictionaryForAvx2<TKey, TValue>(SwissTable<TKey, TValue> dictionary, TKey key, int hash)
        where TKey : notnull
        {
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;
            var bucketMask = dictionary.rawTable._bucket_mask;

            var hashComparer = dictionary._comparer;

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
        private static unsafe ref SwissTable<TKey, TValue>.Entry FindBucketOfDictionaryForSse2<TKey, TValue>(SwissTable<TKey, TValue> dictionary, TKey key, int hash)
        where TKey : notnull
        {
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;
            var bucketMask = dictionary.rawTable._bucket_mask;

            var hashComparer = dictionary._comparer;

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
                            var group = Sse2Group.Load(ptr + probeSeq.pos);
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
                            var group = Sse2Group.Load(ptr + probeSeq.pos);
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
                        var group = Sse2Group.Load(ptr + probeSeq.pos);
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
        private static unsafe ref SwissTable<TKey, TValue>.Entry FindBucketOfDictionaryForFallback<TKey, TValue>(SwissTable<TKey, TValue> dictionary, TKey key, int hash)
        where TKey : notnull
        {
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;
            var bucketMask = dictionary.rawTable._bucket_mask;

            var hashComparer = dictionary._comparer;

            Debug.Assert(controls != null);

            var h2_hash = h2(hash);
            var targetGroup = FallbackGroup.Create(h2_hash);
            var probeSeq = new ProbeSeq(hash, bucketMask);

            if (hashComparer == null)
            {
                if (typeof(TKey).IsValueType)
                {
                    fixed (byte* ptr = &controls[0])
                    {
                        while (true)
                        {
                            var group = FallbackGroup.Load(ptr + probeSeq.pos);
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
                            var group = FallbackGroup.Load(ptr + probeSeq.pos);
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
                        var group = FallbackGroup.Load(ptr + probeSeq.pos);
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
                        if (group.MatchEmpty().AnyBitSet())
                        {
                            return ref Unsafe.NullRef<SwissTable<TKey, TValue>.Entry>();
                        }
                        probeSeq.move_next();
                    }
                }
            }
        }

        /// <summary>
        /// Find the index of given key, negative means not found.
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="dictionary"></param>
        /// <param name="key"></param>
        /// <returns>
        /// negative return value means not found
        /// </returns>
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int DispatchFindBucketIndexOfDictionary<TKey, TValue>(SwissTable<TKey, TValue> dictionary, TKey key)
    where TKey : notnull
        {
            if (Avx2.IsSupported)
            {
                return FindBucketIndexOfDictionaryForAvx2(dictionary, key);
            }
            else
            if (Sse2.IsSupported)
            {
                return FindBucketIndexOfDictionaryForSse2(dictionary, key);
            }
            else
            {
                return FindBucketIndexOfDictionaryForFallback(dictionary, key);
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe int FindBucketIndexOfDictionaryForAvx2<TKey, TValue>(SwissTable<TKey, TValue> dictionary, TKey key)
           where TKey : notnull
        {
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;
            var bucketMask = dictionary.rawTable._bucket_mask;

            var hashComparer = dictionary._comparer;

            Debug.Assert(controls != null);

            var hash = hashComparer == null ? key.GetHashCode() : hashComparer.GetHashCode(key);
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
                                    return index;
                                }
                            }
                            if (group.MatchEmpty().AnyBitSet())
                            {
                                return -1;
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
                                    return index;
                                }
                            }
                            if (group.MatchEmpty().AnyBitSet())
                            {
                                return -1;
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
                                return index;
                            }
                        }
                        if (group.MatchEmpty().AnyBitSet())
                        {
                            return -1;
                        }
                        probeSeq.move_next();
                    }
                }
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe int FindBucketIndexOfDictionaryForSse2<TKey, TValue>(SwissTable<TKey, TValue> dictionary, TKey key)
           where TKey : notnull
        {
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;
            var bucketMask = dictionary.rawTable._bucket_mask;

            var hashComparer = dictionary._comparer;

            Debug.Assert(controls != null);

            var hash = hashComparer == null ? key.GetHashCode() : hashComparer.GetHashCode(key);
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
                            var group = Sse2Group.Load(ptr + probeSeq.pos);
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
                                    return index;
                                }
                            }
                            if (group.MatchEmpty().AnyBitSet())
                            {
                                return -1;
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
                            var group = Sse2Group.Load(ptr + probeSeq.pos);
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
                                    return index;
                                }
                            }
                            if (group.MatchEmpty().AnyBitSet())
                            {
                                return -1;
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
                        var group = Sse2Group.Load(ptr + probeSeq.pos);
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
                                return index;
                            }
                        }
                        if (group.MatchEmpty().AnyBitSet())
                        {
                            return -1;
                        }
                        probeSeq.move_next();
                    }
                }
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe int FindBucketIndexOfDictionaryForFallback<TKey, TValue>(SwissTable<TKey, TValue> dictionary, TKey key)
           where TKey : notnull
        {
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;
            var bucketMask = dictionary.rawTable._bucket_mask;

            var hashComparer = dictionary._comparer;

            Debug.Assert(controls != null);

            var hash = hashComparer == null ? key.GetHashCode() : hashComparer.GetHashCode(key);
            var h2_hash = h2(hash);
            var targetGroup = FallbackGroup.Create(h2_hash);
            var probeSeq = new ProbeSeq(hash, bucketMask);

            if (hashComparer == null)
            {
                if (typeof(TKey).IsValueType)
                {
                    fixed (byte* ptr = &controls[0])
                    {
                        while (true)
                        {
                            var group = FallbackGroup.Load(ptr + probeSeq.pos);
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
                                    return index;
                                }
                            }
                            if (group.MatchEmpty().AnyBitSet())
                            {
                                return -1;
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
                            var group = FallbackGroup.Load(ptr + probeSeq.pos);
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
                                    return index;
                                }
                            }
                            if (group.MatchEmpty().AnyBitSet())
                            {
                                return -1;
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
                        var group = FallbackGroup.Load(ptr + probeSeq.pos);
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
                                return index;
                            }
                        }
                        if (group.MatchEmpty().AnyBitSet())
                        {
                            return -1;
                        }
                        probeSeq.move_next();
                    }
                }
            }
        }

        /// <summary>
        /// 根据当前平台 SIMD 支持情况分派将字典中所有键值对拷贝到目标数组。
        /// </summary>
        /// <typeparam name="TKey">字典键类型。</typeparam>
        /// <typeparam name="TValue">字典值类型。</typeparam>
        /// <param name="dictionary">源字典。</param>
        /// <param name="destArray">目标数组。</param>
        /// <param name="index">目标数组起始写入偏移。</param>
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DispatchCopyToArrayFromDictionaryWorker<TKey, TValue>(SwissTable<TKey, TValue> dictionary, KeyValuePair<TKey, TValue>[] destArray, int index)
            where TKey : notnull
        {
            if (Avx2.IsSupported)
            {
                CopyToArrayFromDictionaryWorkerForAvx2(dictionary, destArray, index);
            }
            else
            if (Sse2.IsSupported)
            {
                CopyToArrayFromDictionaryWorkerForSse2(dictionary, destArray, index);
            }
            else
            {
                CopyToArrayFromDictionaryWorkerForFallback(dictionary, destArray, index);
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void CopyToArrayFromDictionaryWorkerForAvx2<TKey, TValue>(SwissTable<TKey, TValue> dictionary, KeyValuePair<TKey, TValue>[] destArray, int index)
            where TKey : notnull
        {
            int offset = 0;
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;
            var buckets = entries?.Length ?? 0;

            Debug.Assert(controls != null);

            fixed (byte* ptr = &controls[0])
            {
                var bitMask = Avx2Group.Load(ptr).MatchFull();
                while (true)
                {
                    var lowestSetBit = bitMask.LowestSetBit();
                    if (lowestSetBit >= 0)
                    {
                        Debug.Assert(entries != null);
                        bitMask = bitMask.RemoveLowestBit();
                        ref var entry = ref entries[offset + lowestSetBit];
                        destArray[index++] = new KeyValuePair<TKey, TValue>(entry.Key, entry.Value);
                        continue;
                    }
                    offset += GROUP_WIDTH;
                    if (offset >= buckets)
                    {
                        break;
                    }
                    bitMask = Avx2Group.Load(ptr + offset).MatchFull();
                }
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void CopyToArrayFromDictionaryWorkerForSse2<TKey, TValue>(SwissTable<TKey, TValue> dictionary, KeyValuePair<TKey, TValue>[] destArray, int index)
            where TKey : notnull
        {
            int offset = 0;
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;
            var buckets = entries?.Length ?? 0;

            Debug.Assert(controls != null);

            fixed (byte* ptr = &controls[0])
            {
                var bitMask = Sse2Group.Load(ptr).MatchFull();
                while (true)
                {
                    var lowestSetBit = bitMask.LowestSetBit();
                    if (lowestSetBit >= 0)
                    {
                        Debug.Assert(entries != null);
                        bitMask = bitMask.RemoveLowestBit();
                        ref var entry = ref entries[offset + lowestSetBit];
                        destArray[index++] = new KeyValuePair<TKey, TValue>(entry.Key, entry.Value);
                        continue;
                    }
                    offset += GROUP_WIDTH;
                    if (offset >= buckets)
                    {
                        break;
                    }
                    bitMask = Sse2Group.Load(ptr + offset).MatchFull();
                }
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void CopyToArrayFromDictionaryWorkerForFallback<TKey, TValue>(SwissTable<TKey, TValue> dictionary, KeyValuePair<TKey, TValue>[] destArray, int index)
            where TKey : notnull
        {
            int offset = 0;
            var controls = dictionary.rawTable._controls;
            var entries = dictionary.rawTable._entries;
            var buckets = entries?.Length ?? 0;

            Debug.Assert(controls != null);

            fixed (byte* ptr = &controls[0])
            {
                var bitMask = FallbackGroup.Load(ptr).MatchFull();
                while (true)
                {
                    var lowestSetBit = bitMask.LowestSetBit();
                    if (lowestSetBit >= 0)
                    {
                        Debug.Assert(entries != null);
                        bitMask = bitMask.RemoveLowestBit();
                        ref var entry = ref entries[offset + lowestSetBit];
                        destArray[index++] = new KeyValuePair<TKey, TValue>(entry.Key, entry.Value);
                        continue;
                    }
                    offset += GROUP_WIDTH;
                    if (offset >= buckets)
                    {
                        break;
                    }
                    bitMask = FallbackGroup.Load(ptr + offset).MatchFull();
                }
            }
        }

        /// <summary>
        /// 根据当前平台 SIMD 支持情况分派查找给定哈希值在控制字节数组中可插入的槽位。
        /// </summary>
        /// <param name="hash">键的哈希值。</param>
        /// <param name="controls">控制字节数组。</param>
        /// <param name="bucketMask">桶位掩码。</param>
        /// <returns>可插入槽位的索引。</returns>
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int DispatchFindInsertSlot(int hash, byte[] controls, int bucketMask)
        {
            if (Avx2.IsSupported)
            {
                return FindInsertSlotForAvx2(hash, controls, bucketMask);
            }
            else
            if (Sse2.IsSupported)
            {
                return FindInsertSlotForSse2(hash, controls, bucketMask);
            }
            else
            {
                return FindInsertSlotForFallback(hash, controls, bucketMask);
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe int FindInsertSlotForAvx2(int hash, byte[] controls, int bucketMask)
        {
            Debug.Assert(bucketMask == GetBucketMaskFromControlsLength(controls.Length));
            ProbeSeq probeSeq = new ProbeSeq(hash, bucketMask);
            fixed (byte* ptr = &controls[0])
            {
                while (true)
                {
                    // TODO: maybe we should lock even fix the whole loop.
                    // I am not sure which would be faster.
                    var bit = Avx2Group.Load(ptr + probeSeq.pos)
                        .MatchEmptyOrDeleted()
                        .LowestSetBit();
                    if (bit >= 0)
                    {
                        var result = (probeSeq.pos + bit) & bucketMask;

                        // In tables smaller than the group width, trailing control
                        // bytes outside the range of the table are filled with
                        // EMPTY entries. These will unfortunately trigger a
                        // match, but once masked may point to a full bucket that
                        // is already occupied. We detect this situation here and
                        // perform a second scan starting at the begining of the
                        // table. This second scan is guaranteed to find an empty
                        // slot (due to the load factor) before hitting the trailing
                        // control bytes (containing EMPTY).
                        if (!is_full(*(ptr + result)))
                        {
                            return result;
                        }
                        Debug.Assert(bucketMask < GROUP_WIDTH);
                        Debug.Assert(probeSeq.pos != 0);
                        return Avx2Group.Load(ptr)
                            .MatchEmptyOrDeleted()
                            .LowestSetBitNonzero();
                    }
                    probeSeq.move_next();
                }
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe int FindInsertSlotForSse2(int hash, byte[] controls, int bucketMask)
        {
            Debug.Assert(bucketMask == GetBucketMaskFromControlsLength(controls.Length));
            ProbeSeq probeSeq = new ProbeSeq(hash, bucketMask);
            fixed (byte* ptr = &controls[0])
            {
                while (true)
                {
                    // TODO: maybe we should lock even fix the whole loop.
                    // I am not sure which would be faster.
                    var bit = Sse2Group.Load(ptr + probeSeq.pos)
                        .MatchEmptyOrDeleted()
                        .LowestSetBit();
                    if (bit >= 0)
                    {
                        var result = (probeSeq.pos + bit) & bucketMask;

                        // In tables smaller than the group width, trailing control
                        // bytes outside the range of the table are filled with
                        // EMPTY entries. These will unfortunately trigger a
                        // match, but once masked may point to a full bucket that
                        // is already occupied. We detect this situation here and
                        // perform a second scan starting at the begining of the
                        // table. This second scan is guaranteed to find an empty
                        // slot (due to the load factor) before hitting the trailing
                        // control bytes (containing EMPTY).
                        if (!is_full(*(ptr + result)))
                        {
                            return result;
                        }
                        Debug.Assert(bucketMask < GROUP_WIDTH);
                        Debug.Assert(probeSeq.pos != 0);
                        return Sse2Group.Load(ptr)
                            .MatchEmptyOrDeleted()
                            .LowestSetBitNonzero();
                    }
                    probeSeq.move_next();
                }
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe int FindInsertSlotForFallback(int hash, byte[] controls, int bucketMask)
        {
            Debug.Assert(bucketMask == GetBucketMaskFromControlsLength(controls.Length));
            ProbeSeq probeSeq = new ProbeSeq(hash, bucketMask);
            fixed (byte* ptr = &controls[0])
            {
                while (true)
                {
                    // TODO: maybe we should lock even fix the whole loop.
                    // I am not sure which would be faster.
                    var bit = FallbackGroup.Load(ptr + probeSeq.pos)
                        .MatchEmptyOrDeleted()
                        .LowestSetBit();
                    if (bit >= 0)
                    {
                        var result = (probeSeq.pos + bit) & bucketMask;

                        // In tables smaller than the group width, trailing control
                        // bytes outside the range of the table are filled with
                        // EMPTY entries. These will unfortunately trigger a
                        // match, but once masked may point to a full bucket that
                        // is already occupied. We detect this situation here and
                        // perform a second scan starting at the begining of the
                        // table. This second scan is guaranteed to find an empty
                        // slot (due to the load factor) before hitting the trailing
                        // control bytes (containing EMPTY).
                        if (!is_full(*(ptr + result)))
                        {
                            return result;
                        }
                        Debug.Assert(bucketMask < GROUP_WIDTH);
                        Debug.Assert(probeSeq.pos != 0);
                        return FallbackGroup.Load(ptr)
                            .MatchEmptyOrDeleted()
                            .LowestSetBitNonzero();
                    }
                    probeSeq.move_next();
                }
            }
        }

    }
}
