namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// rg 搜索计算核心 — 无状态纯函数工具类，全部 AggressiveInlining。
/// <para>提取自 RgEngine，统一 ASCII 检测/二进制检测/BOM 检测/行匹配判断，
/// 消除 RgEngine 与 MappedFileReader 的重复逻辑，便于独立单测。</para>
/// </summary>
public static class RgCore {
    private const int BinaryDetectionBufferSize = 8192;

    /// <summary>
    /// 判断数据是否为纯 ASCII（无 UTF-8 多字节序列）。
    /// <para>采样前 8KB：所有字节 &lt; 0x80 则为 ASCII。</para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAscii(ReadOnlySpan<byte> data) {
        var sampleLen = Math.Min(data.Length, BinaryDetectionBufferSize);
        for (var i = 0; i < sampleLen; i++) {
            if (data[i] >= 0x80)
                return false;
        }
        return true;
    }

    /// <summary>
    /// 检测数据是否包含 null 字节（二进制文件标志）。
    /// <para>采样前 8KB：发现 0x00 则为二进制。</para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ContainsNullByte(ReadOnlySpan<byte> data) {
        var sampleLen = Math.Min(data.Length, BinaryDetectionBufferSize);
        for (var i = 0; i < sampleLen; i++) {
            if (data[i] == 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 检测 UTF-8 BOM 并返回其长度（0 或 3）。
    /// <para>BOM（EF BB BF）是字节序标记而非文本内容，需跳过。</para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int DetectBom(ReadOnlySpan<byte> data) {
        return data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;
    }

    /// <summary>
    /// 判断一行是否匹配搜索条件 — 匹配判断核心，零 GC。
    /// <para>fastFixed=true 时用 Span.IndexOf(SIMD 加速)，否则用 Regex.IsMatch(span)。</para>
    /// <para>invertMatch=true 时取反（反向匹配模式）。</para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsLineMatch(
        ReadOnlySpan<char> lineSpan,
        Regex regex,
        ReadOnlySpan<char> patternSpan,
        bool fastFixed,
        bool invertMatch) {
        var isMatch = fastFixed
            ? lineSpan.IndexOf(patternSpan) >= 0
            : regex.IsMatch(lineSpan);
        return isMatch != invertMatch;
    }

    /// <summary>
    /// 判断固定字符串快速路径是否可用 — -F 单独使用(无 -i/-w/-S)时走 Span.IndexOf。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CanUseFastFixedString(bool fixedStrings, bool caseInsensitive, bool wordRegexp, bool smartCase) {
        return fixedStrings && !caseInsensitive && !wordRegexp && !smartCase;
    }
}
