namespace JoinCode.Gui.Converters;

/// <summary>
/// (IsUser, Kind) → 消息卡片色条/角色名颜色：工具消息用工具色、思考用紫色、其余按角色蓝/淡青。
/// 取自身份配色，随主题切换。
/// </summary>
public sealed class MsgBarBrushConverter : IMultiValueConverter {
    /// <summary>转换值</summary>
    public object Convert(IList<object?> values, Type targetType, object? parameter, System.Globalization.CultureInfo culture) {
        var s = GuiPalette.Current;
        if (values.Count >= 2 && values[1] is ViewModels.ChatUiMessageKind kind) {
            if (kind is ViewModels.ChatUiMessageKind.ToolCall or ViewModels.ChatUiMessageKind.ToolResult)
                return GuiPalette.ToBrush(s.ToolLabel);
            if (kind == ViewModels.ChatUiMessageKind.Thinking)
                return GuiPalette.ToBrush(s.ThinkingLabel);
        }
        return values.Count >= 1 && values[0] is true
            ? GuiPalette.ToBrush(s.RoleUser)
            : GuiPalette.ToBrush(s.RoleAssistant);
    }
}

/// <summary>
/// 布尔 → 角色标签色：User 蓝色，Assistant 淡青。颜色取自身份配色，随主题切换。
/// </summary>
public sealed class BoolToRoleBrushConverter : IValueConverter {
    /// <summary>转换值</summary>
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) {
        var s = GuiPalette.Current;
        return value is true
            ? GuiPalette.ToBrush(s.RoleUser)
            : GuiPalette.ToBrush(s.RoleAssistant);
    }

    /// <summary>转换回原值</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 会话状态 → 指示器颜色：就绪绿 / 思考黄 / 错误红。取自身份配色，随主题切换。
/// </summary>
public sealed class StatusToBrushConverter : IValueConverter {
    /// <summary>单例实例 — 供 XAML 静态绑定</summary>
    public static readonly StatusToBrushConverter Instance = new();

    /// <summary>转换值</summary>
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) {
        var s = GuiPalette.Current;
        return value switch {
            ViewModels.StatusKind.Busy => GuiPalette.ToBrush(s.BusyText),
            ViewModels.StatusKind.Error => GuiPalette.ToBrush(s.ErrorText),
            _ => GuiPalette.ToBrush(s.SuccessText)
        };
    }

    /// <summary>转换回原值</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 布尔 → 警示前景色：超限用错误色，否则次要文字色。取自身份配色，随主题切换。
/// </summary>
public sealed class BoolToWarnBrushConverter : IValueConverter {
    /// <summary>转换值</summary>
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) {
        var s = GuiPalette.Current;
        return value is true
            ? GuiPalette.ToBrush(s.ErrorText)
            : GuiPalette.ToBrush(s.MutedText);
    }

    /// <summary>转换回原值</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 布尔 → 警示前景色：超限用错误色，否则次要文字色。取自身份配色，随主题切换。
/// </summary>
public sealed class BoolToThinkingOpacityConverter : IValueConverter {
    /// <summary>转换值</summary>
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? 0.82 : 1.0;

    /// <summary>转换回原值</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 布尔 → 会话条目高亮底色：选中时高亮色，未选中透明。取自身份配色，随主题切换。
/// </summary>
public sealed class BoolToSessionHighlightConverter : IValueConverter {
    /// <summary>转换值</summary>
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) {
        return value is true
            ? GuiPalette.ToBrush(GuiPalette.Current.SessionHighlight)
            : Brushes.Transparent;
    }

    /// <summary>转换回原值</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 布尔 → 展开图标：true 显 ▼（已展开），false 显 ▶（已收起）。
/// </summary>
public sealed class ExpandGlyphConverter : IValueConverter {
    /// <summary>单例实例 — 供 XAML 静态绑定</summary>
    public static readonly ExpandGlyphConverter Instance = new();

    /// <summary>转换值</summary>
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? "▼" : "▶";

    /// <summary>转换回原值</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 字符串相等判断 → bool：value 等于 parameter(OrdinalIgnoreCase) 返回 true。
/// 供 RadioButton/ToggleButton IsChecked 绑定到字符串属性(如 SelectedEffort)。
/// ConvertBack 时返回 parameter(选中时写回档位值)。
/// </summary>
public sealed class StringEqualsConverter : IValueConverter {
    /// <summary>单例实例 — 供 XAML 静态绑定</summary>
    public static readonly StringEqualsConverter Instance = new();

    /// <summary>转换值</summary>
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is string s && parameter is string p
            && string.Equals(s, p, StringComparison.OrdinalIgnoreCase);

    /// <summary>转换回原值 — 选中时写回 parameter(档位值),未选中返回 Null 不改变源</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true && parameter is string p ? p : Avalonia.Data.BindingNotification.Null;
}

/// <summary>
/// 推理档位字符串 ↔ Slider 索引(double) — "low"=0 "medium"=1 "high"=2 "max"=3 "auto"=4。
/// 供 Slider Value 双向绑定到 SelectedEffort(string)。
/// </summary>
public sealed class EffortLevelToDoubleConverter : IValueConverter {
    /// <summary>单例实例 — 供 XAML 静态绑定</summary>
    public static readonly EffortLevelToDoubleConverter Instance = new();

    /// <summary>档位 → 索引</summary>
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is string s ? s.ToLowerInvariant() switch {
            "low" => 0.0,
            "medium" => 1.0,
            "high" => 2.0,
            "max" => 3.0,
            "auto" => 4.0,
            _ => 4.0
        } : 4.0;

    /// <summary>索引 → 档位</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is double d ? Math.Round(d) switch {
            0 => "low",
            1 => "medium",
            2 => "high",
            3 => "max",
            _ => "auto"
        } : "auto";
}

/// <summary>
/// 推理档位索引(double) → 品牌色画刷 — Codex 风格五色。
/// 0=绿(#10A37F) 1=蓝(#3B82F6) 2=橙(#F97316) 3=紫(#8B5CF6) 4=灰(#6B7280)
/// </summary>
public sealed class EffortIndexToBrushConverter : IValueConverter {
    /// <summary>单例实例 — 供 XAML 静态绑定</summary>
    public static readonly EffortIndexToBrushConverter Instance = new();

    /// <summary>索引或档位名 → 画刷(支持 double 索引和 string 档位名两种输入)</summary>
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) {
        var idx = value switch {
            double d => (int)Math.Round(d),
            string s => s.ToLowerInvariant() switch { "low" => 0, "medium" => 1, "high" => 2, "max" => 3, _ => 4 },
            _ => 4
        };
        var hex = idx switch {
            0 => "#10A37F",
            1 => "#3B82F6",
            2 => "#F97316",
            3 => "#8B5CF6",
            _ => "#6B7280"
        };
        return new SolidColorBrush(Color.Parse(hex));
    }

    /// <summary>转换回原值 — 不支持</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 推理档位索引(double) → 档位显示名 — 0="low" 1="medium" 2="high" 3="max" 4="auto"
/// </summary>
public sealed class EffortIndexToLabelConverter : IValueConverter {
    /// <summary>单例实例 — 供 XAML 静态绑定</summary>
    public static readonly EffortIndexToLabelConverter Instance = new();

    /// <summary>索引 → 标签</summary>
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) {
        var idx = value is double d ? (int)Math.Round(d) : 4;
        return idx switch {
            0 => "low",
            1 => "medium",
            2 => "high",
            3 => "max",
            _ => "auto"
        };
    }

    /// <summary>转换回原值 — 不支持</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 轮次编号 → 互补色画刷：偶数轮 TurnColorA，奇数轮 TurnColorB。
/// 取自身份配色，随主题切换。驱动轮次色条区分对话轮次（任务4）。
/// </summary>
public sealed class TurnIndexToBrushConverter : IValueConverter {
    /// <summary>单例实例 — 供 XAML 静态绑定</summary>
    public static readonly TurnIndexToBrushConverter Instance = new();

    /// <summary>转换值</summary>
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) {
        var s = GuiPalette.Current;
        return value is int turn && turn % 2 != 0
            ? GuiPalette.ToBrush(s.TurnColorB)
            : GuiPalette.ToBrush(s.TurnColorA);
    }

    /// <summary>转换回原值</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}