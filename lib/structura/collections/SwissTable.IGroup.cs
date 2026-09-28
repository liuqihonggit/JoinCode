// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Structura.Collections
{

    /// <summary>
    /// 探测组接口，定义瑞士表中按组批量匹配字节并产出位掩码的操作契约。
    /// </summary>
    /// <remarks>
    /// 使用 C# 11+ static abstract 成员声明 WIDTH/Create/Load/LoadAligned 为接口契约，
    /// 具体实现由各 SIMD struct（Avx2Group/Sse2Group/FallbackGroup）提供。
    /// 实例方法（MatchByte/MatchGroup/MatchEmpty 等）不使用默认实现，避免 struct 装箱。
    /// </remarks>
    internal interface IGroup<BitMaskImpl, GroupImpl>
        where BitMaskImpl : unmanaged, IBitMask<BitMaskImpl>
        where GroupImpl : unmanaged, IGroup<BitMaskImpl, GroupImpl>
    {
        /// <summary>
        /// 获取探测组的字节宽度（实现需为 readonly static）。
        /// </summary>
        static abstract int WIDTH { get; }

        /// <summary>
        /// 创建所有字节都初始化为指定值的探测组（字节广播）。
        /// </summary>
        /// <param name="b">待广播的字节值。</param>
        /// <returns>广播得到的探测组实例。</returns>
        static abstract GroupImpl Create(byte b);

        /// <summary>
        /// 从非对齐字节指针加载 WIDTH 字节数据构造探测组。
        /// </summary>
        /// <param name="ptr">起始字节地址。</param>
        /// <returns>加载得到的探测组实例。</returns>
        static abstract unsafe GroupImpl Load(byte* ptr);

        /// <summary>
        /// 从 WIDTH 字节对齐的指针加载数据构造探测组，调用方须保证地址对齐。
        /// </summary>
        /// <param name="ptr">按 WIDTH 对齐的起始字节地址。</param>
        /// <returns>加载得到的探测组实例。</returns>
        static abstract unsafe GroupImpl LoadAligned(byte* ptr);

        /// <summary>
        /// Performs the following transformation on all bytes in the group:
        /// - `EMPTY => EMPTY`
        /// - `DELETED => EMPTY`
        /// - `FULL => DELETED`
        /// </summary>
        /// <returns></returns>
        GroupImpl ConvertSpecialToEmptyAndFullToDeleted();

        /// <summary>
        /// Stores the group of bytes to the given address, which must be
        /// aligned to WIDTH
        /// </summary>
        /// <param name="ptr"></param>
        unsafe void StoreAligned(byte* ptr);

        /// <summary>
        /// Returns a `BitMask` indicating all bytes in the group which have
        /// the given value.
        /// </summary>
        /// <param name="b"></param>
        /// <returns></returns>
        BitMaskImpl MatchByte(byte b);

        // <summary>
        // Returns a `GroupImpl` with given byte brodcast.
        // </summary>
        // <param name="group"></param>
        // <returns></returns>
        // match_byte is good enough, however, we do not have readonly parameter now,
        // so we need add this as an optimsation.
        // GroupImpl Create(byte b);

        // match_byte is good enough, however, we do not have readonly parameter now,
        // so we need add this as an optimsation.
        /// <summary>
        /// Returns a `BitMask` indicating all bytes in the group is matched with another group
        /// </summary>
        /// <param name="group"></param>
        /// <returns></returns>
        BitMaskImpl MatchGroup(GroupImpl group);

        /// <summary>
        /// Returns a `BitMask` indicating all bytes in the group which are
        /// `EMPTY`.
        /// </summary>
        /// <returns></returns>
        BitMaskImpl MatchEmpty();

        /// <summary>
        /// Returns a `BitMask` indicating all bytes in the group which are
        /// `EMPTY` or `DELETED`.
        /// </summary>
        /// <returns></returns>
        BitMaskImpl MatchEmptyOrDeleted();


        /// <summary>
        /// Returns a `BitMask` indicating all bytes in the group which are full.
        /// </summary>
        /// <returns></returns>
        BitMaskImpl MatchFull();
    }
}
