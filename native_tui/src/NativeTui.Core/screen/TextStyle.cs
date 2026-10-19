namespace NativeTui.Core.Screen;

/// <summary>颜色种类。</summary>
public enum ColorKind : byte
{
    /// <summary>终端默认色（SGR 39/49）。</summary>
    Default = 0,

    /// <summary>真彩 rgb。</summary>
    Rgb = 1,

    /// <summary>ANSI 256 色索引。</summary>
    Ansi256 = 2,
}

/// <summary>
/// 终端颜色值 — Default/Rgb/Ansi256 三态。
/// Default 与 rgb(0,0,0) 语义不同，靠 <see cref="ColorKind"/> 区分。
/// </summary>
public readonly record struct Color
{
    /// <summary>颜色种类。</summary>
    public ColorKind Kind { get; }

    /// <summary>红分量（Rgb 时有效）。</summary>
    public byte R { get; }

    /// <summary>绿分量（Rgb 时有效）。</summary>
    public byte G { get; }

    /// <summary>蓝分量（Rgb 时有效）。</summary>
    public byte B { get; }

    /// <summary>256 色索引（Ansi256 时有效）。</summary>
    public byte Index { get; }

    private Color(ColorKind kind, byte r, byte g, byte b, byte index)
    {
        Kind = kind;
        R = r;
        G = g;
        B = b;
        Index = index;
    }

    /// <summary>终端默认色。</summary>
    public static Color Default => default;

    /// <summary>真彩色。</summary>
    public static Color FromRgb(int r, int g, int b) =>
        new(ColorKind.Rgb, (byte)r, (byte)g, (byte)b, 0);

    /// <summary>ANSI 256 色索引。</summary>
    public static Color FromAnsi256(int index) =>
        new(ColorKind.Ansi256, 0, 0, 0, (byte)index);
}

/// <summary>
/// 文本样式 — 前景/背景色 + 六个修饰开关。
/// 对齐基准 ink/styles.ts 的 TextStyle（不含 wrap，wrap 属于布局层）。
/// </summary>
public readonly record struct TextStyle
{
    /// <summary>前景色。</summary>
    public Color Foreground { get; init; }

    /// <summary>背景色。</summary>
    public Color Background { get; init; }

    /// <summary>粗体。</summary>
    public bool Bold { get; init; }

    /// <summary>斜体。</summary>
    public bool Italic { get; init; }

    /// <summary>下划线。</summary>
    public bool Underline { get; init; }

    /// <summary>暗淡。</summary>
    public bool Dim { get; init; }

    /// <summary>反色。</summary>
    public bool Inverse { get; init; }

    /// <summary>删除线。</summary>
    public bool Strikethrough { get; init; }

    /// <summary>无任何修饰的默认样式。</summary>
    public static TextStyle Default { get; } = new();
}
