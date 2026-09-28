// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Structura.Collections
{

    /// <summary>
    /// 探测组接口，定义瑞士表中按组批量匹配字节并产出位掩码的操作契约。
    /// </summary>
    /// <remarks>After C#11, `StaticEmpty`, `Create`, `Load` and `LoadAligned` should become static abstract method</remarks>
    internal interface IGroup<BitMaskImpl, GroupImpl>
        where BitMaskImpl : unmanaged, IBitMask<BitMaskImpl>
        where GroupImpl : unmanaged, IGroup<BitMaskImpl, GroupImpl>
    {
        ///// <summary>
        ///// Returns a full group of empty bytes, suitable for use as the initial
        ///// value for an empty hash table.
        ///// </summary>
        ///// <returns></returns>
        ////byte[] StaticEmpty { get; }

        ///// <summary>
        ///// The bytes that the group data ocupies
        ///// </summary>
        ///// <remarks>
        ///// The implementation should have `readonly` modifier
        ///// </remarks>
        ////int WIDTH { get; }

        ////unsafe GroupImpl Load(byte* ptr);

        ///// <summary>
        ///// Loads a group of bytes starting at the given address, which must be
        ///// aligned to the WIDTH
        ///// </summary>
        ///// <param name="ptr"></param>
        ///// <returns></returns>
        ////unsafe GroupImpl LoadAligned(byte* ptr);

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
