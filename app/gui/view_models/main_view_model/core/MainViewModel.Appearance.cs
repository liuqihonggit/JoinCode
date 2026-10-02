namespace JoinCode.Gui.ViewModels;

/// <summary>GUI 外观选择与引擎主题解耦，选项来自统一配置文件。</summary>
public sealed partial class MainViewModel {
    private readonly AppearanceCatalog _appearance = AppearanceCatalog.Load();
    private bool _hasGuiThemePreference;

    /// <summary>配置驱动的基础主题选项。</summary>
    public IReadOnlyList<ThemeOption> ThemeOptions => _appearance.Themes;
    /// <summary>配置驱动的强调色选项。</summary>
    public IReadOnlyList<AccentOption> AccentOptions => _appearance.Accents;

    /// <summary>当前强调色；null 选择不改变已有设置。</summary>
    public AccentOption? SelectedAccent {
        get => AccentOptions.FirstOrDefault(a => a.Id == AccentId) ?? AccentOptions.FirstOrDefault();
        set { if (value is not null) AccentId = value.Id; }
    }

    /// <summary>当前主题选项，直接驱动原有 CurrentTheme 属性。</summary>
    public ThemeOption? SelectedThemeOption {
        get => ThemeOptions.FirstOrDefault(t => t.Id == CurrentTheme.ToString());
        set {
            if (value is not null && Enum.TryParse<GuiPalette.GuiThemeVariant>(value.Id, out var theme))
                CurrentTheme = theme;
        }
    }

    /// <summary>当前强调色配置标识。</summary>
    [ObservableProperty]
    private string _accentId = "ocean";

    /// <summary>交互动效开关。</summary>
    [ObservableProperty]
    private bool _animationsEnabled = true;

    /// <summary>设置抽屉的目标宽度，关闭时不占位。</summary>
    public double SettingsPanelWidth => IsSettingsPanelOpen ? 312 : 0;

    partial void OnIsSettingsPanelOpenChanged(bool value) => OnPropertyChanged(nameof(SettingsPanelWidth));

    partial void OnAccentIdChanged(string value) => OnPropertyChanged(nameof(SelectedAccent));

    /// <summary>恢复 GUI 外观；无效配置回退至目录默认值。</summary>
    private void LoadAppearance(Persistence.GuiPreferences prefs) {
        AccentId = AccentOptions.Any(a => a.Id == prefs.AccentId) ? prefs.AccentId : AccentOptions[0].Id;
        AnimationsEnabled = prefs.AnimationsEnabled;
        if (Enum.TryParse<GuiPalette.GuiThemeVariant>(prefs.GuiTheme, out var theme) && Enum.IsDefined(theme)) {
            _hasGuiThemePreference = true;
            CurrentTheme = theme;
        }
    }
}
