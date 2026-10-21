namespace JoinCode.Gui.Tests.Persistence;

/// <summary>
/// Bug2 TDD：IsUnattendedMode 死字段 — GuiPreferences 缺字段，保存后加载丢失。
/// 修复：GuiPreferences 加 IsUnattendedMode 字段 + Save/Load 往返保持。
/// </summary>
public class GuiPreferencesUnattendedTests {
    private static (GuiPreferencesStore store, IO.FileSystem.InMemoryFileSystem fs) CreateStore() {
        var fs = new IO.FileSystem.InMemoryFileSystem();
        return (new GuiPreferencesStore(fs, "mem/gui-preferences.json"), fs);
    }

    [Fact]
    public async Task SaveLoad_PreservesIsUnattendedMode_True() {
        var (store, _) = CreateStore();
        await store.SaveAsync(new GuiPreferences { IsUnattendedMode = true });
        var loaded = await store.LoadAsync();
        loaded.IsUnattendedMode.Should().BeTrue();
    }

    [Fact]
    public async Task SaveLoad_PreservesIsUnattendedMode_False() {
        var (store, _) = CreateStore();
        await store.SaveAsync(new GuiPreferences { IsUnattendedMode = false });
        var loaded = await store.LoadAsync();
        loaded.IsUnattendedMode.Should().BeFalse();
    }

    [Fact]
    public async Task LoadDefault_IsUnattendedMode_False() {
        var (store, _) = CreateStore();
        var loaded = await store.LoadAsync();
        loaded.IsUnattendedMode.Should().BeFalse();
    }
}
