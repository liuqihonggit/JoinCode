namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 状态指示器符号枚举
/// </summary>
public enum StatusSymbol
{
    [EnumValue("\u2713")] Tick,
    [EnumValue("\u2717")] Cross,
    [EnumValue("\u26A0")] Warning,
    [EnumValue("\u2139")] Info,
    [EnumValue("\u25CB")] Circle,
    [EnumValue("\u21BB")] Refresh,
    [EnumValue("\u25B6")] Play,
    [EnumValue("\u2026")] Ellipsis,
    [EnumValue("\u2298")] Prohibited,
    [EnumValue("\u25A0")] Stop,
    [EnumValue("\u21E5")] Skip,
}

/// <summary>
/// 对象类型图标符号枚举
/// </summary>
public enum ObjectSymbol
{
    [EnumValue("\u2500")] File,
    [EnumValue("\u25B8")] Directory,
    [EnumValue("\u25C6")] DiamondFilled,
    [EnumValue("\u2630")] List,
    [EnumValue("\u2B21")] Struct,
    [EnumValue("\u2192")] ArrowRight,
    [EnumValue("\u21AF")] Lightning,
    [EnumValue("\u270E")] Pencil,
    [EnumValue("\u25C7")] DiamondOpen,
    [EnumValue("\u25C8")] Color,
    [EnumValue("\u2315")] Search,
    [EnumValue("\u25C9")] Agent,
    [EnumValue("\u2699")] Gear,
    [EnumValue("\u2715")] Clean,
    [EnumValue("\u2191")] ArrowUp,
    [EnumValue("\u2605")] Star,
    [EnumValue("\u271A")] Health,
    [EnumValue("\u2295")] Operator,
    [EnumValue("\u25A3")] Indexer,
    [EnumValue("\u2716")] Destructor,
    [EnumValue("\u25AB")] LocalFunction,
}

/// <summary>
/// 优先级符号枚举
/// </summary>
public enum PrioritySymbol
{
    [EnumValue("\u25CF")] Critical,
    [EnumValue("\u25D0")] High,
    [EnumValue("\u25D4")] Medium,
    [EnumValue("\u25CB")] Low,
}

/// <summary>
/// 结构/导航符号枚举
/// </summary>
public enum StructureSymbol
{
    [EnumValue("\u2022")] Bullet,
    [EnumValue("\u276F")] Pointer,
    [EnumValue("\u251C")] Branch,
    [EnumValue("\u2514")] Leaf,
    [EnumValue("\u2502")] Vertical,
    [EnumValue("\u2500")] Horizontal,
    [EnumValue("\u258E")] Blockquote,
    [EnumValue("\u2501")] HeavyHorizontal,
}
