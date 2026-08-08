using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;

using JoinCode.Gui.Theming;

namespace JoinCode.Gui.Converters;

/// <summary>
/// 布尔 → 横向对齐：User 居右（End），Assistant 居左（Start）。
/// </summary>
public sealed class BoolToHorizontalAlignmentConverter : IValueConverter
{
    /// <summary>IsUser=true → End（右侧）</summary>
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is HorizontalAlignment.Right;
}

/// <summary>
/// 布尔 → 气泡背景色：User 用户蓝，Assistant 深灰。颜色取自身份配色，随主题切换。
/// </summary>
public sealed class BoolToBubbleBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var s = GuiPalette.Current;
        return value is true
            ? GuiPalette.ToBrush(s.BubbleUser)
            : GuiPalette.ToBrush(s.BubbleText);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 布尔 → 角色标签色：User 蓝色，Assistant 淡青。颜色取自身份配色，随主题切换。
/// </summary>
public sealed class BoolToRoleBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var s = GuiPalette.Current;
        return value is true
            ? GuiPalette.ToBrush(s.RoleUser)
            : GuiPalette.ToBrush(s.RoleAssistant);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 会话状态 → 指示器颜色：就绪绿 / 思考黄 / 错误红。取自身份配色，随主题切换。
/// </summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var s = GuiPalette.Current;
        return value switch
        {
            ViewModels.StatusKind.Busy => GuiPalette.ToBrush(s.BusyText),
            ViewModels.StatusKind.Error => GuiPalette.ToBrush(s.ErrorText),
            _ => GuiPalette.ToBrush(s.SuccessText)
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 布尔 → 警示前景色：超限用错误色，否则次要文字色。取自身份配色，随主题切换。
/// </summary>
public sealed class BoolToWarnBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var s = GuiPalette.Current;
        return value is true
            ? GuiPalette.ToBrush(s.ErrorText)
            : GuiPalette.ToBrush(s.MutedText);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 消息类型 → 气泡底色：User / 思考 / 工具 / 工具结果 / 正文。取自身份配色，随主题切换。
/// </summary>
public sealed class KindToBubbleBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not ViewModels.ChatUiMessage m)
            return Brushes.Transparent;
        var s = GuiPalette.Current;
        return m.IsUser
            ? GuiPalette.ToBrush(s.BubbleUser)
            : m.IsThinking
                ? GuiPalette.ToBrush(s.BubbleThinking)
                : m.Kind switch
                {
                    ViewModels.ChatUiMessageKind.ToolCall => GuiPalette.ToBrush(s.BubbleToolCall),
                    ViewModels.ChatUiMessageKind.ToolResult => GuiPalette.ToBrush(s.BubbleToolResult),
                    _ => GuiPalette.ToBrush(s.BubbleText)
                };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 布尔(是否思考消息) → 不透明度：思考内容轻微半透明呈现淡出感，正文不透明。
/// </summary>
public sealed class BoolToThinkingOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? 0.82 : 1.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 布尔 → 会话条目高亮底色：选中时高亮色，未选中透明。取自身份配色，随主题切换。
/// </summary>
public sealed class BoolToSessionHighlightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        return value is true
            ? GuiPalette.ToBrush(GuiPalette.Current.SessionHighlight)
            : Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}