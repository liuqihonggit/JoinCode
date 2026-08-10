using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

using JoinCode.Gui.Theming;

namespace JoinCode.Gui.Converters;

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
/// 布尔 → 警示前景色：超限用错误色，否则次要文字色。取自身份配色，随主题切换。
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