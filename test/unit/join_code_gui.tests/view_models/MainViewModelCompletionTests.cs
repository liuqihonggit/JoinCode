namespace JoinCode.Gui.Tests.ViewModels;

/// <summary>
/// MainViewModel 补全统一框架测试 — 验证 @ 代理补全、# 文件补全通过 Registry 走通，
/// SlashModeLabel 从 Provider.Label 获取，CompleteSlashSuggestion 回填正确。
/// </summary>
public class MainViewModelCompletionTests {
    private static MainViewModel CreateVm() => new(
        new JoinCode.Gui.Hosting.PlaceholderChatSession(),
        new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"),
        new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"));

    private static void SetInput(MainViewModel vm, string text) {
        vm.InputText = text;
        vm.InputCaretIndex = text.Length;
        vm.RefreshSlashSuggestions();
    }

    [Fact]
    public async Task AtTrigger_OpensPopupWithAgentSuggestions() {
        await using var vm = CreateVm();
        SetInput(vm, "@");
        vm.IsSlashPopupOpen.Should().BeTrue();
        vm.SlashSuggestions.Should().NotBeEmpty();
        vm.SlashModeLabel.Should().Be("代理补全");
    }

    [Fact]
    public async Task AtTrigger_PrefixFiltersAgents() {
        await using var vm = CreateVm();
        SetInput(vm, "@ex");
        vm.IsSlashPopupOpen.Should().BeTrue();
        vm.SlashSuggestions.Should().NotBeEmpty();
        vm.SlashSuggestions.Should().OnlyContain(s =>
            s.Name.StartsWith("ex", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AtTrigger_NonMatchingPrefix_ClosesPopup() {
        await using var vm = CreateVm();
        SetInput(vm, "@zzz-no-such-agent");
        vm.IsSlashPopupOpen.Should().BeFalse();
        vm.SlashSuggestions.Should().BeEmpty();
    }

    [Fact]
    public async Task HashTrigger_OpensPopupWithFileSuggestions() {
        await using var vm = CreateVm();
        SetInput(vm, "#");
        vm.IsSlashPopupOpen.Should().BeTrue();
        vm.SlashModeLabel.Should().Be("文件补全");
    }

    [Fact]
    public async Task CompleteAgentSuggestion_ReplacesPrefixWithAgentName() {
        await using var vm = CreateVm();
        SetInput(vm, "@co");
        vm.SlashSuggestions.Should().NotBeEmpty();
        vm.CompleteSlashSuggestion();
        vm.InputText.Should().StartWith("@coordinator");
        vm.IsSlashPopupOpen.Should().BeFalse();
    }

    [Fact]
    public async Task CompleteFileSuggestion_ReplacesPrefixWithFileName() {
        await using var vm = CreateVm();
        SetInput(vm, "#");
        vm.SlashSuggestions.Should().NotBeEmpty();
        var firstName = vm.SlashSuggestions[0].Name;
        vm.CompleteSlashSuggestion();
        vm.InputText.Should().StartWith("#" + firstName);
        vm.IsSlashPopupOpen.Should().BeFalse();
    }

    [Fact]
    public async Task AtTrigger_SpaceTerminatesCompletion() {
        await using var vm = CreateVm();
        SetInput(vm, "@agent message");
        vm.IsSlashPopupOpen.Should().BeFalse();
    }

    [Fact]
    public async Task HashTrigger_SpaceTerminatesCompletion() {
        await using var vm = CreateVm();
        SetInput(vm, "#file message");
        vm.IsSlashPopupOpen.Should().BeFalse();
    }
}