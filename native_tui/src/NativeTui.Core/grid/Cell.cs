namespace NativeTui.Core.Grid;

/// <summary>
/// 字符终端宽度（对齐基准 ink/screen.ts 的 CellWidth）。
/// </summary>
public enum CellWidth : byte
{
    /// <summary>常规单格字符。</summary>
    Default = 0,

    /// <summary>宽字符头部（CJK/emoji，占两格）。</summary>
    Wide = 1,

    /// <summary>宽字符尾部占位格 — 复制/序列化时跳过，不单独输出。</summary>
    SpacerTail = 2,
}

/// <summary>
/// 单个终端 cell — rune + 样式 id + 宽度标记。
/// 使用 <see cref="Rune"/>（UTF-32 scalar）存储以支持补充平面 emoji。
/// </summary>
public readonly record struct Cell
{
    /// <summary>字符（UTF-32 scalar）。</summary>
    public Rune Value { get; init; }

    /// <summary>样式 intern id（StylePool）。</summary>
    public int StyleId { get; init; }

    /// <summary>宽度标记。</summary>
    public CellWidth Width { get; init; }

    /// <summary>构造 cell。</summary>
    public Cell(Rune value, int styleId, CellWidth width)
    {
        Value = value;
        StyleId = styleId;
        Width = width;
    }

    /// <summary>默认空白 cell（空格 + 默认样式）。</summary>
    public static Cell Blank { get; } = new(new Rune(' '), 0, CellWidth.Default);
}
