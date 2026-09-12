namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 位掩码工具 — 提供直观的位运算 API，用于固定集合的 O(1) 成员判断。
/// 替代 FrozenSet&lt;T&gt;.Contains，无哈希查找、无堆分配。
/// </summary>
public static class BitMask
{
    /// <summary>
    /// 构建包含指定枚举值的 32 位掩码。用于静态初始化，一次性 params 数组分配。
    /// </summary>
    public static int Of<TEnum>(params TEnum[] values) where TEnum : struct, Enum
    {
        var mask = 0;
        for (var i = 0; i < values.Length; i++)
            mask |= 1 << Unsafe.As<TEnum, int>(ref values[i]);
        return mask;
    }

    /// <summary>
    /// 判断 32 位掩码是否包含指定枚举值。O(1) 位运算，无哈希查找。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Contains<TEnum>(int mask, TEnum value) where TEnum : struct, Enum
        => ((mask >> Unsafe.As<TEnum, int>(ref value)) & 1) != 0;

    /// <summary>
    /// 判断 32 位掩码是否包含指定索引。O(1) 位运算。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Contains(int mask, int index)
        => ((mask >> index) & 1) != 0;

    /// <summary>
    /// 判断 64 位掩码是否包含指定索引。O(1) 位运算。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Contains64(ulong mask, int index)
        => ((mask >> index) & 1UL) != 0;
}
