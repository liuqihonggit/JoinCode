#pragma warning disable JCC9001 // Screenshots are diagnostic artifacts, following the repository dumps convention.
namespace JoinCode.Gui.Tests.Views;

/// <summary>真实 Skia 帧验证四个主题、外观抽屉和窄窗口布局。</summary>
[Collection("GuiUiSequential")]
public sealed class WorkspacePreviewTests {
    [AvaloniaTheory]
    [InlineData(GuiPalette.GuiThemeVariant.Dark, "violet", 1240, true)]
    [InlineData(GuiPalette.GuiThemeVariant.Light, "rose", 1240, true)]
    [InlineData(GuiPalette.GuiThemeVariant.SolarizedDark, "mint", 1240, false)]
    [InlineData(GuiPalette.GuiThemeVariant.SolarizedLight, "amber", 1240, false)]
    [InlineData(GuiPalette.GuiThemeVariant.Dark, "cyan", 800, true)]
    public async Task CaptureWorkspace(GuiPalette.GuiThemeVariant theme, string accent, int width, bool settings) {
        var fs = new InMemoryFileSystem();
        var prefs = new GuiPreferencesStore(fs, "mem/preferences.json");
        await prefs.SaveAsync(new GuiPreferences { GuiTheme = theme.ToString(), AccentId = accent, AnimationsEnabled = false });
        await using var vm = new MainViewModel(new PlaceholderChatSession(), new GuiSessionStore(fs, "mem/sessions"), prefs);
        vm.IsSettingsPanelOpen = settings;
        var window = new MainWindow { DataContext = vm, Width = width, Height = 820 };
        try {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var composer = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "InputTextBox");
            composer.Bounds.Width.Should().BeGreaterThan(100);
            if (settings) {
                var drawer = window.GetVisualDescendants().OfType<SettingsPanelView>().Single();
                drawer.Bounds.Width.Should().Be(312);
            }
            using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No Skia frame.");
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root.Parent is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
            var path = Path.Combine(root.FullName, "dumps", "gui_experience");
            Directory.CreateDirectory(path);
            frame.Save(Path.Combine(path, $"{theme}-{accent}-{width}.png"));
        } finally { window.Close(); }
    }
}
