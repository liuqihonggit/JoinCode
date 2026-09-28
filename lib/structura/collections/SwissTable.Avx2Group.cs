// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


namespace Structura.Collections
{
    internal struct Avx2BitMask : IBitMask<Avx2BitMask>
    {
        private const uint BITMASK_MASK = 0xffff_ffff;

        // 256 / 8 = 32, so choose uint
        internal readonly uint _data;

        internal Avx2BitMask(uint data)
        {
            _data = data;
        }

        /// <summary>
        /// 返回当前位掩码的按位反转结果。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Avx2BitMask Invert()
        {
            return new Avx2BitMask((this._data ^ BITMASK_MASK));
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
            return BitOperations.LeadingZeroCount(this._data);
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
        public Avx2BitMask RemoveLowestBit()
        {
            return new Avx2BitMask(this._data & (this._data - 1));
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
        public Avx2BitMask And(Avx2BitMask bitMask)
        {
            return new Avx2BitMask((this._data & bitMask._data));
        }
    }

    // TODO: suppress default initialization.
    internal struct Avx2Group : IGroup<Avx2BitMask, Avx2Group>
    {
        // 256 bits(_data length) / 8 (byte bits) = 32 bytes
        /// <summary>
        /// 获取组的宽度（字节数），固定为 32 字节（256 位 / 8 位每字节）。
        /// </summary>
        public static int WIDTH => 256 / 8;

        private readonly Vector256<byte> _data;

        internal Avx2Group(Vector256<byte> data)
        {
            _data = data;
        }

        /// <summary>
        /// 静态空字节数组，所有元素初始化为 EMPTY 标记，用于初始化空组。
        /// </summary>
        public static readonly byte[] StaticEmpty = InitialStaticEmpty();

        private static byte[] InitialStaticEmpty()
        {
            var res = new byte[WIDTH];
            Array.Fill(res, SwissTableHelper.EMPTY);
            return res;
        }

        /// <summary>
        /// 从非对齐字节指针加载 32 字节数据构造 AVX2 组。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe Avx2Group Load(byte* ptr)
        {
            return new Avx2Group(Avx2.LoadVector256(ptr));
        }

        /// <summary>
        /// 从 32 字节对齐的字节指针加载数据构造 AVX2 组。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe Avx2Group LoadAligned(byte* ptr)
        {
            // `uint` casting is OK, WIDTH is 32, so checking lowest 5 bits for address align
            Debug.Assert(((uint)ptr & (WIDTH - 1)) == 0);
            return new Avx2Group(Avx2.LoadAlignedVector256(ptr));
        }

        /// <summary>
        /// 将组数据存储到 32 字节对齐的字节指针位置。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void StoreAligned(byte* ptr)
        {
            // `uint` casting is OK, WIDTH is 32, so checking lowest 5 bits for address align
            Debug.Assert(((uint)ptr & (WIDTH - 1)) == 0);
            Avx2.StoreAligned(ptr, this._data);
        }

        /// <summary>
        /// 在组中逐字节查找与指定字节相等的位置，返回匹配位掩码。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Avx2BitMask MatchByte(byte b)
        {
            // TODO: Check how compiler create this, which command it uses. This might incluence performance dramatically.
            var compareValue = Vector256.Create(b);
            var cmp = Avx2.CompareEqual(this._data, compareValue);
            return new Avx2BitMask((uint)Avx2.MoveMask(cmp));
        }

        private static readonly Avx2Group EmptyGroup = Create(SwissTableHelper.EMPTY);

        /// <summary>
        /// 在组中查找空槽位，返回匹配位掩码。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Avx2BitMask MatchEmpty()
        {
            return this.MatchGroup(EmptyGroup);
            // return this.match_byte(SwissTableHelper.EMPTY);
        }

        /// <summary>
        /// 在组中查找空或已删除的槽位（高位为 1 的字节），返回匹配位掩码。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Avx2BitMask MatchEmptyOrDeleted()
        {
            // A byte is EMPTY or DELETED iff the high bit is set
            return new Avx2BitMask((uint)Avx2.MoveMask(this._data));
        }

        /// <summary>
        /// 在组中查找已占用的满槽位，返回匹配位掩码。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Avx2BitMask MatchFull()
        {
            return this.MatchEmptyOrDeleted().Invert();
        }

        /// <summary>
        /// 将特殊字节（空或已删除）转换为空标记，将满字节转换为已删除标记，用于调整组内字节状态。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Avx2Group convert_special_to_empty_and_full_to_deleted()
        {
            // Map high_bit = 1 (EMPTY or DELETED) to 1111_1111
            // and high_bit = 0 (FULL) to 1000_0000
            //
            // Here's this logic expanded to concrete values:
            //   let special = 0 > byte = 1111_1111 (true) or 0000_0000 (false)
            //   1111_1111 | 1000_0000 = 1111_1111
            //   0000_0000 | 1000_0000 = 1000_0000
            // byte: 0x80_u8 as i8
            var zero = Vector256<sbyte>.Zero;
            zero.As<sbyte, byte>();
            // TODO: check whether asXXXX could be removed.
            var special = Avx2.CompareGreaterThan(zero, this._data.AsSByte()).AsByte();
            return new Avx2Group(Avx2.Or(special, Vector256.Create((byte)0x80)));
        }

        /// <summary>
        /// 创建所有字节都初始化为指定值的 AVX2 组。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Avx2Group Create(byte b)
        {
            return new Avx2Group(Vector256.Create(b));
        }

        /// <summary>
        /// 在当前组中查找与指定组对应字节全部相等的位置，返回匹配位掩码。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Avx2BitMask MatchGroup(Avx2Group group)
        {
            var cmp = Avx2.CompareEqual(this._data, group._data);
            return new Avx2BitMask((uint)Avx2.MoveMask(cmp));
        }
    }
}
