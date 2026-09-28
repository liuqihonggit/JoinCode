// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


namespace Structura.Collections
{
    /// <summary>
    /// 无 SIMD 回退位掩码实现，使用 nuint 按字节存储匹配结果，作为 IBitMask 的非向量化后备方案。
    /// </summary>
    internal struct FallbackBitMask : IBitMask<FallbackBitMask>
    {
        // Why use nuint/nint?
        // For 64 bit platform, we could compare 8 buckets at one time,
        // For 32 bit platform, we could compare 4 buckets at one time.
        // And it might be faster to access data for it is aligned, but not sure.
        private readonly nuint _data;

        private static nuint BITMASK_MASK => unchecked((nuint)0x8080_8080_8080_8080);

        private const int BITMASK_SHIFT = 3;

        internal FallbackBitMask(nuint data)
        {
            _data = data;
        }

        /// Returns a new `BitMask` with all bits inverted.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FallbackBitMask Invert()
        {
            return new FallbackBitMask(this._data ^ BITMASK_MASK);
        }

        /// <summary>
        /// 返回当前位掩码是否至少有一个比特位被置位。
        /// </summary>
        /// <returns>若存在置位比特返回 true，否则返回 false。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AnyBitSet()
        {
            return this._data != 0;
        }

        /// <summary>
        /// 返回位掩码中前导零（高位起连续零字节）的数量，按字节粒度归一化。
        /// </summary>
        /// <returns>前导空字节数量。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int LeadingZeros()
        {
#if TARGET_64BIT
            return BitOperations.LeadingZeroCount(this._data) >> BITMASK_SHIFT;
#else
            // maigc number `32`
            // type of `this._data` is `nunit`
            // however, it will be tranfrom to `ulong` implicitly
            // So it is 64 - 32 = 32
            return (BitOperations.LeadingZeroCount(this._data) - 32) >> BITMASK_SHIFT;
#endif
        }

        /// <summary>
        /// 返回位掩码中最低置位比特对应的字节索引；若无任何比特置位则返回 -1。
        /// </summary>
        /// <returns>最低置位字节索引，无置位时返回 -1。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int LowestSetBit()
        {
            if (this._data == 0)
            {
                return -1;
            }
            else
            {
                return this.LowestSetBitNonzero();
            }
        }

        /// <summary>
        /// 返回位掩码中最低置位比特对应的字节索引，调用方须保证掩码非空。
        /// </summary>
        /// <returns>最低置位字节索引。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int LowestSetBitNonzero()
        {
            return this.TrailingZeros();
        }

        /// <summary>
        /// 返回清除最低置位比特后的新位掩码实例。
        /// </summary>
        /// <returns>移除最低置位比特后的新位掩码。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FallbackBitMask RemoveLowestBit()
        {
            return new FallbackBitMask(this._data & (this._data - 1));
        }

        /// <summary>
        /// 返回位掩码中尾部零（低位起连续零字节）的数量，按字节粒度归一化。
        /// </summary>
        /// <returns>尾部空字节数量。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int TrailingZeros()
        {
            return BitOperations.TrailingZeroCount(this._data) >> BITMASK_SHIFT;
        }

        /// <summary>
        /// 返回当前位掩码与指定位掩码按位逻辑与后的新实例。
        /// </summary>
        /// <param name="bitMask">参与按位与的位掩码，须与当前实例同类型。</param>
        /// <returns>按位与结果的新位掩码。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FallbackBitMask And(FallbackBitMask bitMask)
        {
            return new FallbackBitMask(this._data & bitMask._data);
        }
    }

    /// <summary>
    /// 无 SIMD 回退探测组实现，以 nuint 承载 WIDTH 字节的数据，作为 IGroup 的非向量化后备方案。
    /// </summary>
    internal struct FallbackGroup : IGroup<FallbackBitMask, FallbackGroup>
    {
        /// <summary>
        /// 当前探测组承载的字节宽度（等于 nuint 的字节大小）。
        /// </summary>
        public static unsafe int WIDTH => sizeof(nuint);

        /// <summary>
        /// 全部字节初始化为 EMPTY 的空探测组模板，用作空哈希表的初始填充值。
        /// </summary>
        public static readonly byte[] static_empty = InitialStaticEmpty();

        private static byte[] InitialStaticEmpty()
        {
            var res = new byte[WIDTH];
            Array.Fill(res, SwissTableHelper.EMPTY);
            return res;
        }

        /// <summary>
        /// 从给定地址按未对齐方式加载 WIDTH 字节构造探测组。
        /// </summary>
        /// <param name="ptr">起始字节地址。</param>
        /// <returns>加载得到的探测组实例。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe FallbackGroup load(byte* ptr)
        {
            return new FallbackGroup(Unsafe.ReadUnaligned<nuint>(ptr));
        }

        /// <summary>
        /// 从给定地址按对齐方式加载 WIDTH 字节构造探测组，调用方须保证地址按 WIDTH 对齐。
        /// </summary>
        /// <param name="ptr">按 WIDTH 对齐的起始字节地址。</param>
        /// <returns>加载得到的探测组实例。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe FallbackGroup load_aligned(byte* ptr)
        {
            // uint casting is OK, for WIDTH only use low 16 bits now.
            Debug.Assert(((uint)ptr & (WIDTH - 1)) == 0);
            return new FallbackGroup(Unsafe.Read<nuint>(ptr));
        }

        private static nuint repeat(byte b)
        {
            nuint res = 0;
            for (int i = 0; i < WIDTH; i++)
            {
                res <<= 8;
                res &= b;
            }
            return res;
        }

        private readonly nuint _data;

        internal FallbackGroup(nuint data)
        {
            _data = data;
        }

        /// <summary>
        /// 对组内所有字节执行特殊变换：EMPTY 与 DELETED 映射为 EMPTY，FULL 映射为 DELETED。
        /// </summary>
        /// <returns>变换后的新探测组。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FallbackGroup convert_special_to_empty_and_full_to_deleted()
        {
            // Map high_bit = 1 (EMPTY or DELETED) to 1111_1111
            // and high_bit = 0 (FULL) to 1000_0000
            //
            // Here's this logic expanded to concrete values:
            //   let full = 1000_0000 (true) or 0000_0000 (false)
            //   !1000_0000 + 1 = 0111_1111 + 1 = 1000_0000 (no carry)
            //   !0000_0000 + 0 = 1111_1111 + 0 = 1111_1111 (no carry)
            nuint full = ~this._data & unchecked((nuint)0x8080_8080_8080_8080);
            var q = (full >> 7);
            var w = ~full + q;
            return new FallbackGroup(w);
        }

        /// <summary>
        /// 将当前探测组的字节存储到按 WIDTH 对齐的给定地址。
        /// </summary>
        /// <param name="ptr">按 WIDTH 对齐的目标地址。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void StoreAligned(byte* ptr)
        {
            // uint casting is OK, for WIDTH only use low 16 bits now.
            Debug.Assert(((uint)ptr & (WIDTH - 1)) == 0);
            Unsafe.Write(ptr, this._data);
        }

        /// <summary>
        /// 返回标识组内所有等于指定字节位置的位掩码。
        /// </summary>
        /// <param name="b">待匹配的目标字节值。</param>
        /// <returns>匹配结果位掩码。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FallbackBitMask MatchByte(byte b)
        {
            // This algorithm is derived from
            // https://graphics.stanford.edu/~seander/bithacks.html##ValueInWord
            var cmp = this._data ^ repeat(b);
            var res = unchecked((cmp - (nuint)0x0101_0101_0101_0101) & ~cmp & (nuint)0x8080_8080_8080_8080);
            return new FallbackBitMask(res);
        }

        /// <summary>
        /// 返回标识组内所有 EMPTY 字节位置的位掩码。
        /// </summary>
        /// <returns>匹配结果位掩码。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FallbackBitMask MatchEmpty()
        {
            // If the high bit is set, then the byte must be either:
            // 1111_1111 (EMPTY) or 1000_0000 (DELETED).
            // So we can just check if the top two bits are 1 by ANDing them.
            return new FallbackBitMask(this._data & this._data << 1 & unchecked((nuint)0x8080_8080_8080_8080));
        }

        /// <summary>
        /// 返回标识组内所有 EMPTY 或 DELETED 字节位置的位掩码。
        /// </summary>
        /// <returns>匹配结果位掩码。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FallbackBitMask MatchEmptyOrDeleted()
        {
            // A byte is EMPTY or DELETED iff the high bit is set
            return new FallbackBitMask(this._data & unchecked((nuint)0x8080_8080_8080_8080));
        }

        /// <summary>
        /// 返回标识组内所有 FULL 字节位置的位掩码。
        /// </summary>
        /// <returns>匹配结果位掩码。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FallbackBitMask MatchFull()
        {
            return this.MatchEmptyOrDeleted().Invert();
        }

        /// <summary>
        /// 将指定字节广播到组内所有位置构造探测组（当前实现尚未提供）。
        /// </summary>
        /// <param name="b">待广播的字节值。</param>
        /// <returns>广播得到的探测组实例。</returns>
        public static FallbackGroup create(byte b)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// 返回标识组内所有字节与另一探测组对应位置匹配的位掩码（当前实现尚未提供）。
        /// </summary>
        /// <param name="group">待比较的探测组。</param>
        /// <returns>匹配结果位掩码。</returns>
        public FallbackBitMask MatchGroup(FallbackGroup group)
        {
            throw new NotImplementedException();
        }
    }
}
