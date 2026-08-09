using System.Globalization;

using Avalonia.Media;

using JoinCode.Gui.Converters;
using JoinCode.Gui.Theming;
using JoinCode.Gui.ViewModels;

namespace JoinCode.Gui.Tests.Converters;

/// <summary>
/// UiConverters 单元测试 — 验证 5 个值转换器的输出正确性。
/// 转换器直接驱动 XAML 绑定的颜色/透明度/高亮，错误的转换会导致界面显示异常。
/// </summary>
public sealed class UiConvertersTests
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    private static ISolidColorBrush AsBrush(object? result)
    {
        result.Should().BeAssignableTo<ISolidColorBrush>();
        return (ISolidColorBrush)result!;
    }

    // ── BoolToRoleBrushConverter ──

    [Fact]
    public void BoolToRoleBrush_True_ReturnsRoleUserBrush()
    {
        var conv = new BoolToRoleBrushConverter();
        var scheme = GuiPalette.Current;

        var brush = AsBrush(conv.Convert(true, typeof(IBrush), null, C));

        brush.Color.ToString().Should().Be(GuiPalette.ToBrush(scheme.RoleUser).Color.ToString());
    }

    [Fact]
    public void BoolToRoleBrush_False_ReturnsRoleAssistantBrush()
    {
        var conv = new BoolToRoleBrushConverter();
        var scheme = GuiPalette.Current;

        var brush = AsBrush(conv.Convert(false, typeof(IBrush), null, C));

        brush.Color.ToString().Should().Be(GuiPalette.ToBrush(scheme.RoleAssistant).Color.ToString());
    }

    [Fact]
    public void BoolToRoleBrush_NonBool_ReturnsRoleAssistantBrush()
    {
        var conv = new BoolToRoleBrushConverter();
        var scheme = GuiPalette.Current;

        var brush = AsBrush(conv.Convert("not-a-bool", typeof(IBrush), null, C));

        brush.Color.ToString().Should().Be(GuiPalette.ToBrush(scheme.RoleAssistant).Color.ToString());
    }

    [Fact]
    public void BoolToRoleBrush_ConvertBack_Throws()
    {
        var conv = new BoolToRoleBrushConverter();

        var act = () => conv.ConvertBack(null, typeof(bool), null, C);

        act.Should().Throw<NotSupportedException>();
    }

    // ── StatusToBrushConverter ──

    [Fact]
    public void StatusToBrush_Busy_ReturnsBusyTextBrush()
    {
        var conv = new StatusToBrushConverter();
        var scheme = GuiPalette.Current;

        var brush = AsBrush(conv.Convert(StatusKind.Busy, typeof(IBrush), null, C));

        brush.Color.ToString().Should().Be(GuiPalette.ToBrush(scheme.BusyText).Color.ToString());
    }

    [Fact]
    public void StatusToBrush_Error_ReturnsErrorTextBrush()
    {
        var conv = new StatusToBrushConverter();
        var scheme = GuiPalette.Current;

        var brush = AsBrush(conv.Convert(StatusKind.Error, typeof(IBrush), null, C));

        brush.Color.ToString().Should().Be(GuiPalette.ToBrush(scheme.ErrorText).Color.ToString());
    }

    [Fact]
    public void StatusToBrush_Ready_ReturnsSuccessTextBrush()
    {
        var conv = new StatusToBrushConverter();
        var scheme = GuiPalette.Current;

        var brush = AsBrush(conv.Convert(StatusKind.Ready, typeof(IBrush), null, C));

        brush.Color.ToString().Should().Be(GuiPalette.ToBrush(scheme.SuccessText).Color.ToString());
    }

    [Fact]
    public void StatusToBrush_UnknownValue_ReturnsSuccessTextBrush()
    {
        var conv = new StatusToBrushConverter();
        var scheme = GuiPalette.Current;

        var brush = AsBrush(conv.Convert(999, typeof(IBrush), null, C));

        brush.Color.ToString().Should().Be(GuiPalette.ToBrush(scheme.SuccessText).Color.ToString());
    }

    [Fact]
    public void StatusToBrush_ConvertBack_Throws()
    {
        var conv = new StatusToBrushConverter();

        var act = () => conv.ConvertBack(null, typeof(StatusKind), null, C);

        act.Should().Throw<NotSupportedException>();
    }

    // ── BoolToWarnBrushConverter ──

    [Fact]
    public void BoolToWarnBrush_True_ReturnsErrorTextBrush()
    {
        var conv = new BoolToWarnBrushConverter();
        var scheme = GuiPalette.Current;

        var brush = AsBrush(conv.Convert(true, typeof(IBrush), null, C));

        brush.Color.ToString().Should().Be(GuiPalette.ToBrush(scheme.ErrorText).Color.ToString());
    }

    [Fact]
    public void BoolToWarnBrush_False_ReturnsMutedTextBrush()
    {
        var conv = new BoolToWarnBrushConverter();
        var scheme = GuiPalette.Current;

        var brush = AsBrush(conv.Convert(false, typeof(IBrush), null, C));

        brush.Color.ToString().Should().Be(GuiPalette.ToBrush(scheme.MutedText).Color.ToString());
    }

    [Fact]
    public void BoolToWarnBrush_ConvertBack_Throws()
    {
        var conv = new BoolToWarnBrushConverter();

        var act = () => conv.ConvertBack(null, typeof(bool), null, C);

        act.Should().Throw<NotSupportedException>();
    }

    // ── BoolToThinkingOpacityConverter ──

    [Fact]
    public void BoolToThinkingOpacity_True_Returns082()
    {
        var conv = new BoolToThinkingOpacityConverter();

        var result = conv.Convert(true, typeof(double), null, C);

        result.Should().Be(0.82);
    }

    [Fact]
    public void BoolToThinkingOpacity_False_Returns1()
    {
        var conv = new BoolToThinkingOpacityConverter();

        var result = conv.Convert(false, typeof(double), null, C);

        result.Should().Be(1.0);
    }

    [Fact]
    public void BoolToThinkingOpacity_ConvertBack_Throws()
    {
        var conv = new BoolToThinkingOpacityConverter();

        var act = () => conv.ConvertBack(null, typeof(bool), null, C);

        act.Should().Throw<NotSupportedException>();
    }

    // ── BoolToSessionHighlightConverter ──

    [Fact]
    public void BoolToSessionHighlight_True_ReturnsSessionHighlightBrush()
    {
        var conv = new BoolToSessionHighlightConverter();
        var scheme = GuiPalette.Current;

        var brush = AsBrush(conv.Convert(true, typeof(IBrush), null, C));

        brush.Color.ToString().Should().Be(GuiPalette.ToBrush(scheme.SessionHighlight).Color.ToString());
    }

    [Fact]
    public void BoolToSessionHighlight_False_ReturnsTransparent()
    {
        var conv = new BoolToSessionHighlightConverter();

        var brush = AsBrush(conv.Convert(false, typeof(IBrush), null, C));

        brush.Color.ToString().Should().Be(Brushes.Transparent.Color.ToString());
    }

    [Fact]
    public void BoolToSessionHighlight_ConvertBack_Throws()
    {
        var conv = new BoolToSessionHighlightConverter();

        var act = () => conv.ConvertBack(null, typeof(bool), null, C);

        act.Should().Throw<NotSupportedException>();
    }
}
