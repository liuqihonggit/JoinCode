namespace JoinCode.Gui.Views;

/// <summary>独立工作台窗口，复用主窗口模型及主题。</summary>
public sealed partial class WorkbenchWindow : Window {
    private WorkbenchViewModel? _vm;
    /// <summary>初始化界面。</summary>
    public WorkbenchWindow() {
        InitializeComponent();
        DataContextChanged += OnContextChanged;
        Closed += (_, _) => {
            if (_vm is not null) { _vm.CancelOperationCommand.Execute(null); _vm.PropertyChanged -= OnViewModelChanged; _vm.Main.PropertyChanged -= OnMainChanged; }
        };
    }
    private void OnContextChanged(object? sender, EventArgs e) {
        if (_vm is not null) { _vm.PropertyChanged -= OnViewModelChanged; _vm.Main.PropertyChanged -= OnMainChanged; }
        _vm = DataContext as WorkbenchViewModel;
        if (_vm is not null) { _vm.PropertyChanged += OnViewModelChanged; _vm.Main.PropertyChanged += OnMainChanged; ApplyTheme(); UpdatePreviews(); }
    }
    private void OnMainChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName is nameof(MainViewModel.CurrentTheme) or nameof(MainViewModel.AccentId)) ApplyTheme();
    }
    private void ApplyTheme() {
        if (_vm is null) return;
        RequestedThemeVariant = _vm.Main.IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
    }
    private void OnChooseFolder(object? sender, RoutedEventArgs e) => _ = ChooseFolderAsync();
    private async Task ChooseFolderAsync() {
        if (_vm is null) return;
        try {
            var folders = await StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions {
                Title = "选择 JoinCode 工作区", AllowMultiple = false
            });
            if (folders.FirstOrDefault() is { } folder) {
                _vm.Directory = folder.Path.LocalPath;
                await _vm.RefreshFilesCommand.ExecuteAsync(null);
            }
        } catch (Exception ex) { _vm.Status = $"无法选择目录：{ex.Message}"; }
    }
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName is nameof(WorkbenchViewModel.FileContent) or nameof(WorkbenchViewModel.GitContent) or nameof(WorkbenchViewModel.SelectedFile)) UpdatePreviews();
    }
    private void UpdatePreviews() {
        if (_vm is null) return;
        CodePreview.Text = _vm.FileContent;
        GitPreview.Text = _vm.GitContent;
    }
    private void OnEntryDoubleTapped(object? sender, TappedEventArgs e) {
        if (DataContext is WorkbenchViewModel vm) vm.OpenEntryCommand.Execute(null);
    }
}
