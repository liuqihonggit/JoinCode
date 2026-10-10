namespace JoinCode.Gui.Tests.Views;

/// <summary>外观设置必须可发现，并且主题配置须独立于 CLI 主题持久化。</summary>
[Collection("GuiUiSequential")]
public sealed class AppearanceExperienceTests {
    [Fact]
    public async Task Settings_OffersAppearanceAndMotionControls() {
        var fs = new InMemoryFileSystem();
        await using var vm = new MainViewModel(new PlaceholderChatSession(),
            new GuiSessionStore(fs, "mem/sessions"), new GuiPreferencesStore(fs, "mem/preferences.json"));
        vm.CurrentTheme.Should().Be(GuiPalette.GuiThemeVariant.Dark, "默认应为暗色主题");
        vm.AnimationsEnabled.Should().BeTrue("默认启用动画");
    }

    [Fact]
    public async Task Preferences_SaveAppearanceDefaults() {
        var fs = new InMemoryFileSystem();
        var store = new GuiPreferencesStore(fs, "mem/preferences.json");
        await store.SaveAsync(new GuiPreferences { GuiTheme = "Dark", AccentId = "violet", AnimationsEnabled = false });
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
                GuiPalette.ContrastRatio(Color.Parse(scheme.ToastForeground), Color.Parse(accent.Fill))
                    .Should().BeGreaterThanOrEqualTo(4.5, $"button {accent.Name}");
            }
        }
    }

    [Theory]
    [InlineData(GuiPalette.GuiThemeVariant.SolarizedDark)]
    [InlineData(GuiPalette.GuiThemeVariant.SolarizedLight)]
    public void Solarized_TextAndMessageSurfacesMeetAA(GuiPalette.GuiThemeVariant variant) {
        var s = GuiPalette.SchemeFor(variant);
        foreach (var surface in new[] { s.WindowBackground, s.SidebarBackground, s.SearchBarBackground, s.ButtonPressed }) {
            foreach (var text in new[] { s.PrimaryText, s.SecondaryText, s.MutedText })
                GuiPalette.ContrastRatio(Color.Parse(text), Color.Parse(surface))
                    .Should().BeGreaterThanOrEqualTo(4.5, $"{variant}: {text} on {surface}");
        }
        GuiPalette.ContrastRatio(Color.Parse(s.PrimaryText), Color.Parse(s.BubbleText)).Should().BeGreaterThanOrEqualTo(4.5);
    }

    [Fact]
    public async Task AccentSelection_AlsoThemesNativeFluentControls() {
        var fs = new InMemoryFileSystem();
        await using var vm = new MainViewModel(new PlaceholderChatSession(),
            new GuiSessionStore(fs, "mem/sessions"), new GuiPreferencesStore(fs, "mem/preferences.json"));
        vm.CurrentTheme.Should().Be(GuiPalette.GuiThemeVariant.Dark, "默认应为暗色主题");
        vm.CurrentTheme = GuiPalette.GuiThemeVariant.Light;
        vm.CurrentTheme.Should().Be(GuiPalette.GuiThemeVariant.Light, "CurrentTheme 可设置");
        vm.AnimationsEnabled.Should().BeTrue("默认启用动画");
        vm.AnimationsEnabled = false;
        vm.AnimationsEnabled.Should().BeFalse("AnimationsEnabled 可设置");
    }
}
