// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


namespace Structura.Collections
{
    internal struct Sse2BitMask : IBitMask<Sse2BitMask>
    {
        private const ushort BITMASK_MASK = 0xffff;

        // 128 / 8 = 16, so choose ushort
        internal readonly ushort _data;

        internal Sse2BitMask(ushort data)
        {
            _data = data;
        }

        /// <summary>
        /// 返回当前位掩码的按位反转结果。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sse2BitMask Invert()
        {
            return new Sse2BitMask((ushort)(this._data ^ BITMASK_MASK));
        }

        /// <summary>
        /// 判断当前位掩码是否有任何位被设置。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AnyBitSet()
        {
            return this._data != 0;
        }

        /// <summary>
        /// 返回当前位掩码的前导零数量。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int LeadingZeros()
        {
            // maigc number `16`
            // type of `this._data` is `short`
            // however, it will be tranfrom to `uint` implicitly
            // Delete the additional length
            return BitOperations.LeadingZeroCount(this._data) - 16;
        }

        /// <summary>
        /// 返回最低设置位的位置；若无任何位被设置则返回 -1。
        /// </summary>
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
        /// 返回最低设置位的位置（调用方需保证至少有一个位被设置）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int LowestSetBitNonzero()
        {
            return this.TrailingZeros();
        }

        /// <summary>
        /// 返回清除最低设置位后的位掩码。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sse2BitMask RemoveLowestBit()
        {
            return new Sse2BitMask((ushort)(this._data & (this._data - 1)));
        }

        /// <summary>
        /// 返回当前位掩码的尾随零数量。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int TrailingZeros()
        {
            return BitOperations.TrailingZeroCount(this._data);
        }

        /// <summary>
        /// 返回当前位掩码与指定位掩码按位与的结果。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sse2BitMask And(Sse2BitMask bitMask)
        {
            return new Sse2BitMask((ushort)(this._data & bitMask._data));
        }
    }

    // TODO: suppress default initialization.
    internal struct Sse2Group : IGroup<Sse2BitMask, Sse2Group>
    {
        // 128 bits(_data length) / 8 (byte bits) = 16 bytes
        /// <summary>
        /// 获取组的宽度（字节数），固定为 16 字节（128 位 / 8 位每字节）。
        /// </summary>
        public static int WIDTH => 128 / 8;

        private readonly Vector128<byte> _data;

        internal Sse2Group(Vector128<byte> data)
        {
            _data = data;
        }

        /// <summary>
        /// 静态空字节数组，所有元素初始化为 EMPTY 标记，用于初始化空组。
        /// </summary>
        public static readonly byte[] static_empty = InitialStaticEmpty();

        private static byte[] InitialStaticEmpty()
        {
            var res = new byte[WIDTH];
            Array.Fill(res, SwissTableHelper.EMPTY);
            return res;
        }

        /// <summary>
        /// 从非对齐字节指针加载 16 字节数据构造 SSE2 组。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe Sse2Group load(byte* ptr)
        {
            return new Sse2Group(Sse2.LoadVector128(ptr));
        }

        /// <summary>
        /// 从 16 字节对齐的字节指针加载数据构造 SSE2 组。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe Sse2Group load_aligned(byte* ptr)
        {
            // `uint` casting is OK, WIDTH is 16, so checking lowest 4 bits for address align
            Debug.Assert(((uint)ptr & (WIDTH - 1)) == 0);
            return new Sse2Group(Sse2.LoadAlignedVector128(ptr));
        }

        /// <summary>
        /// 将组数据存储到 16 字节对齐的字节指针位置。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void StoreAligned(byte* ptr)
        {
            // `uint` casting is OK, WIDTH is 16, so checking lowest 4 bits for address align
            Debug.Assert(((uint)ptr & (WIDTH - 1)) == 0);
            Sse2.StoreAligned(ptr, this._data);
        }

        /// <summary>
        /// 在组中逐字节查找与指定字节相等的位置，返回匹配位掩码。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sse2BitMask MatchByte(byte b)
        {
            // TODO: Check how compiler create this, which command it uses. This might incluence performance dramatically.
            var compareValue = Vector128.Create(b);
            var cmp = Sse2.CompareEqual(this._data, compareValue);
            return new Sse2BitMask((ushort)Sse2.MoveMask(cmp));
        }

        private static readonly Sse2Group EmptyGroup = Create(SwissTableHelper.EMPTY);

        /// <summary>
        /// 在组中查找空槽位，返回匹配位掩码。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sse2BitMask MatchEmpty()
        {
            return this.MatchGroup(EmptyGroup);
            //return this.MatchByte(SwissTableHelper.EMPTY);
        }

        /// <summary>
        /// 在组中查找空或已删除的槽位（高位为 1 的字节），返回匹配位掩码。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sse2BitMask MatchEmptyOrDeleted()
        {
            // A byte is EMPTY or DELETED iff the high bit is set
            return new Sse2BitMask((ushort)Sse2.MoveMask(this._data));
        }

        /// <summary>
        /// 在组中查找已占用的满槽位，返回匹配位掩码。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sse2BitMask MatchFull()
        {
            return this.MatchEmptyOrDeleted().Invert();
        }

        /// <summary>
        /// 将特殊字节（空或已删除）转换为空标记，将满字节转换为已删除标记，用于调整组内字节状态。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sse2Group convert_special_to_empty_and_full_to_deleted()
        {
            // Map high_bit = 1 (EMPTY or DELETED) to 1111_1111
            // and high_bit = 0 (FULL) to 1000_0000
            //
            // Here's this logic expanded to concrete values:
            //   let special = 0 > byte = 1111_1111 (true) or 0000_0000 (false)
            //   1111_1111 | 1000_0000 = 1111_1111
            //   0000_0000 | 1000_0000 = 1000_0000
            // byte: 0x80_u8 as i8
            var zero = Vector128<sbyte>.Zero;
            zero.As<sbyte, byte>();
            // TODO: check whether asXXXX could be removed.
            var special = Sse2.CompareGreaterThan(zero, this._data.AsSByte()).AsByte();
            return new Sse2Group(Sse2.Or(special, Vector128.Create((byte)0x80)));
        }

        /// <summary>
        /// 创建所有字节都初始化为指定值的 SSE2 组。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Sse2Group Create(byte b)
        {
            return new Sse2Group(Vector128.Create(b));
        }

        /// <summary>
        /// 在当前组中查找与指定组对应字节全部相等的位置，返回匹配位掩码。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Sse2BitMask MatchGroup(Sse2Group group)
        {
            var cmp = Sse2.CompareEqual(this._data, group._data);
            return new Sse2BitMask((ushort)Sse2.MoveMask(cmp));
        }
    }
}
