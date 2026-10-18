namespace Hands.Tests.Handlers;

/// <summary>
/// WebFetchMiddleware.ResolveEncoding charset 解析测试（改动 P1）。
/// <para>ResolveEncoding 是 private static 方法，用反射测试各分支：
/// null/空 → UTF-8；支持的 charset → 对应 Encoding；不支持 → 回退 UTF-8。</para>
/// <para>注：.NET 10 默认仅支持 UTF/ASCII/Latin1，GBK/Shift_JIS 需 CodePagesEncodingProvider。
/// 测试用条件断言：编码可用时验证对应编码，不可用时验证回退 UTF-8。</para>
/// </summary>
public sealed class WebFetchMiddlewareTests {
    private static readonly MethodInfo _resolveEncodingMethod =
        typeof(WebFetchMiddleware).GetMethod("ResolveEncoding", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("未找到 WebFetchMiddleware.ResolveEncoding 私有方法");

    /// <summary>
    /// 调用 private static ResolveEncoding(string?) 方法。
    /// </summary>
    [SuppressMessage("AotSafety", "JCC11003", Justification = "反射 Invoke 返回 object? 需强制转换,边界可空抑制")]
    private static Encoding InvokeResolveEncoding(string? charset) {
        var result = _resolveEncodingMethod.Invoke(null, [charset]);
        return (Encoding)result!;
    }

    /// <summary>
    /// 判断指定 charset 在当前运行时是否可用（不抛 ArgumentException）。
    /// </summary>
    private static bool IsEncodingAvailable(string charset) {
        try { Encoding.GetEncoding(charset); return true; } catch (ArgumentException) { return false; }
    }

    /// <summary>
    /// charset 为 null 时应回退 UTF-8。
    /// </summary>
    [Fact]
    public void ResolveEncoding_null_应返回Utf8() {
        var encoding = InvokeResolveEncoding(null);
        encoding.Should().BeSameAs(Encoding.UTF8);
    }

    /// <summary>
    /// charset 为空字符串时应回退 UTF-8。
    /// </summary>
    [Fact]
    public void ResolveEncoding_空字符串_应返回Utf8() {
        var encoding = InvokeResolveEncoding("");
        encoding.Should().BeSameAs(Encoding.UTF8);
    }

    /// <summary>
    /// charset 为空白字符（空格）时应回退 UTF-8。
    /// </summary>
    [Fact]
    public void ResolveEncoding_空白字符_应返回Utf8() {
        var encoding = InvokeResolveEncoding("   ");
        encoding.Should().BeSameAs(Encoding.UTF8);
    }

    /// <summary>
    /// charset 为 "utf-8" 时应返回 UTF-8 编码。
    /// </summary>
    [Fact]
    public void ResolveEncoding_utf8_应返回Utf8() {
        var encoding = InvokeResolveEncoding("utf-8");
        encoding.WebName.Should().Be("utf-8");
    }

    /// <summary>
    /// charset 为 "UTF-8"（大写）时应返回 UTF-8 编码。
    /// </summary>
    [Fact]
    public void ResolveEncoding_UTF8大写_应返回Utf8() {
        var encoding = InvokeResolveEncoding("UTF-8");
        encoding.WebName.Should().Be("utf-8");
    }

    /// <summary>
    /// charset 为 "ascii" 时应返回 ASCII 编码（.NET 默认支持）。
    /// </summary>
    [Fact]
    public void ResolveEncoding_ascii_应返回Ascii() {
        var encoding = InvokeResolveEncoding("ascii");
        encoding.WebName.Should().Be("us-ascii");
    }

    /// <summary>
    /// charset 为不支持的值时应回退 UTF-8（保守不崩溃）。
    /// </summary>
    [Fact]
    public void ResolveEncoding_不支持的charset_应回退Utf8() {
        var encoding = InvokeResolveEncoding("nonexistent-encoding-xyz");
        encoding.Should().BeSameAs(Encoding.UTF8);
    }

    /// <summary>
    /// charset 为 "gbk" 时：若运行时支持 GBK 则返回 GBK 编码，否则回退 UTF-8。
    /// </summary>
    [Fact]
    public void ResolveEncoding_gbk_支持时返回Gbk_否则回退Utf8() {
        var encoding = InvokeResolveEncoding("gbk");
        if (IsEncodingAvailable("gbk")) {
            encoding.WebName.Should().Be(Encoding.GetEncoding("gbk").WebName);
        } else {
            encoding.Should().BeSameAs(Encoding.UTF8);
        }
    }

    /// <summary>
    /// charset 为 "shift_jis" 时：若运行时支持则返回 Shift_JIS，否则回退 UTF-8。
    /// </summary>
    [Fact]
    public void ResolveEncoding_shiftJis_支持时返回ShiftJis_否则回退Utf8() {
        var encoding = InvokeResolveEncoding("shift_jis");
        if (IsEncodingAvailable("shift_jis")) {
            encoding.WebName.Should().Be(Encoding.GetEncoding("shift_jis").WebName);
        } else {
            encoding.Should().BeSameAs(Encoding.UTF8);
        }
    }

    /// <summary>
    /// charset 为 "iso-8859-1" 时：若运行时支持则返回 ISO-8859-1，否则回退 UTF-8。
    /// </summary>
    [Fact]
    public void ResolveEncoding_iso88591_支持时返回Iso88591_否则回退Utf8() {
        var encoding = InvokeResolveEncoding("iso-8859-1");
        if (IsEncodingAvailable("iso-8859-1")) {
            encoding.WebName.Should().Be("iso-8859-1");
        } else {
            encoding.Should().BeSameAs(Encoding.UTF8);
        }
    }

    /// <summary>
    /// charset 为 "latin1" 别名时：若运行时支持则返回 ISO-8859-1，否则回退 UTF-8。
    /// </summary>
    [Fact]
    public void ResolveEncoding_latin1别名_支持时返回Iso88591_否则回退Utf8() {
        var encoding = InvokeResolveEncoding("latin1");
        if (IsEncodingAvailable("latin1")) {
            encoding.WebName.Should().Be("iso-8859-1");
        } else {
            encoding.Should().BeSameAs(Encoding.UTF8);
        }
    }
}
