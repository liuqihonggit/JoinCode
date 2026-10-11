namespace JoinCode.Gui.Theming;

/// <summary>文件驱动的外观选项；同一份配置供设置面板和主题资源消费。</summary>
public sealed class AppearanceCatalog {
    /// <summary>主题选项。</summary>
    public ThemeOption[] Themes { get; set; } = [];
    /// <summary>强调色选项。</summary>
    public AccentOption[] Accents { get; set; } = [];

    /// <summary>从嵌入配置加载，不依赖当前工作目录。</summary>
    public static AppearanceCatalog Load() {
#pragma warning disable JCC9104 // Assembly manifest resource is synchronously read in constructor; no asynchronous IO.
        using var stream = typeof(AppearanceCatalog).Assembly.GetManifestResourceStream("JoinCode.Gui.Appearance.json")
            ?? throw new InvalidOperationException("Missing appearance catalog.");
#pragma warning restore JCC9104
        using var reader = new System.IO.StreamReader(stream);
        return RelaxedJsonSerializer.Deserialize(reader.ReadToEnd(), Persistence.GuiJsonContext.Default.AppearanceCatalog)
            ?? throw new InvalidOperationException("Invalid appearance catalog.");
    }
}

/// <summary>主题显示名称与原有主题变体的关联。</summary>
public sealed class ThemeOption {
    /// <summary>主题变体名称。</summary>
    public string Id { get; set; } = "Dark";
    /// <summary>面板显示名称。</summary>
    public string Name { get; set; } = "";
}

/// <summary>强调色在不同明暗表面上的文字、弱底、实底颜色。</summary>
public sealed class AccentOption {
    /// <summary>持久化标识。</summary>
    public string Id { get; set; } = "";
    /// <summary>面板显示名称。</summary>
    public string Name { get; set; } = "";
    /// <summary>深色主题强调文字。</summary>
    public string DarkText { get; set; } = "";
    /// <summary>浅色主题强调文字。</summary>
    public string LightText { get; set; } = "";
    /// <summary>深色主题弱底。</summary>
    public string DarkSubtle { get; set; } = "";
    /// <summary>浅色主题弱底。</summary>
    public string LightSubtle { get; set; } = "";
    /// <summary>白字按钮实底。</summary>
    public string Fill { get; set; } = "";
    /// <summary>强调色预览画刷。</summary>
    public IBrush PreviewBrush => GuiPalette.ToBrush(Fill);
}
