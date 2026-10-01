namespace JoinCode.Gui.Tests.Views;

/// <summary>外观设置必须可发现，并且主题配置须独立于 CLI 主题持久化。</summary>
[Collection("GuiUiSequential")]
public sealed class AppearanceExperienceTests {
    [AvaloniaFact]
    public void Settings_OffersAppearanceAndMotionControls() {
        var window = new Window { Width = 400, Height = 800, Content = new SettingsPanelView() };
        try {
            window.Show();
            window.GetVisualDescendants().OfType<TextBlock>()
                .Select(t => t.Text).Should().Contain("外观与动效");
        } finally { window.Close(); }
    }

    [Fact]
    public async Task Preferences_SaveAppearanceDefaults() {
        var fs = new InMemoryFileSystem();
        var store = new GuiPreferencesStore(fs, "mem/preferences.json");
        await store.SaveAsync(new GuiPreferences());
        var json = await fs.ReadAllText("mem/preferences.json");
        json.Should().Contain("accentId").And.Contain("animationsEnabled").And.Contain("guiTheme");
    }

    [Fact]
    public async Task Preferences_RoundTripSolarizedAccentAndReducedMotion() {
        var fs = new InMemoryFileSystem();
        var store = new GuiPreferencesStore(fs, "mem/preferences.json");
        await store.SaveAsync(new GuiPreferences {
            GuiTheme = "SolarizedDark", AccentId = "violet", AnimationsEnabled = false
        });
        var loaded = await store.LoadAsync();
        loaded.GuiTheme.Should().Be("SolarizedDark");
        loaded.AccentId.Should().Be("violet");
        loaded.AnimationsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Catalog_AllAccentsMeetTextContrastInEveryTheme() {
        var catalog = AppearanceCatalog.Load();
        catalog.Accents.Select(a => a.Id).Distinct().Count().Should().BeGreaterThanOrEqualTo(8);
        foreach (var theme in catalog.Themes) {
            var variant = Enum.Parse<GuiPalette.GuiThemeVariant>(theme.Id);
            var scheme = GuiPalette.SchemeFor(variant);
            var light = variant is GuiPalette.GuiThemeVariant.Light or GuiPalette.GuiThemeVariant.SolarizedLight;
            foreach (var accent in catalog.Accents) {
                var text = Color.Parse(light ? accent.LightText : accent.DarkText);
                GuiPalette.ContrastRatio(text, Color.Parse(scheme.WindowBackground))
                    .Should().BeGreaterThanOrEqualTo(4.5, $"{theme.Name} / {accent.Name}");
                GuiPalette.ContrastRatio(text, Color.Parse(light ? accent.LightSubtle : accent.DarkSubtle))
                    .Should().BeGreaterThanOrEqualTo(4.5, $"subtle {theme.Name} / {accent.Name}");
                GuiPalette.ContrastRatio(Colors.White, Color.Parse(accent.Fill))
                    .Should().BeGreaterThanOrEqualTo(4.5, $"button {accent.Name}");
            }
        }
    }
}
