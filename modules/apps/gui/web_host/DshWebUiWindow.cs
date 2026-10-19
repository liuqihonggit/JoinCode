namespace JoinCode.Gui.WebHost;

/// <summary>
/// DSH Web UI 窗口（ADR 0143）：起始 <see cref="DshWebHost"/>（回环 HTTP + WS），
/// 内容区由 WebView2 加载该宿主页面。原自研 WebUI（<c>--webui-smoke</c>）暂保留作对照。
/// </summary>
internal sealed class DshWebUiWindow : Window {
    private readonly DshWebHost _host;
    private readonly WebView2Host _view;

    /// <summary>构造并启动宿主</summary>
    /// <param name="userDataDir">WebView2 用户数据目录</param>
    internal DshWebUiWindow(string userDataDir) {
        Title = "JoinCode";
        Width = 1440;
        Height = 900;
        ClientSize = new Size(1440, 900);
        _host = DshWebHost.Start();
        _view = new WebView2Host(_host.BaseUrl, userDataDir);
        _view.InitFailed += ShowFallback;
        Content = _view;
        DshDirectoryPicker.Register(PickFolderAsync);
        App.LogDiag($"[DSH-WEB] window created url={_host.BaseUrl}");
    }

    /// <summary>前端 directoryPicker/pick 的原生实现：UI 线程弹目录选择对话框，取消返回 null</summary>
    private async Task<string?> PickFolderAsync(CancellationToken cancellationToken) {
        var folders = await Dispatcher.UIThread.InvokeAsync(() => StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions {
            Title = "选择工作区目录",
            AllowMultiple = false,
        }));
        if (cancellationToken.IsCancellationRequested) {
            return null;
        }
        return folders.FirstOrDefault()?.Path.LocalPath;
    }

    /// <summary>WebView2 初始化失败回退（不崩溃，给出可操作提示）</summary>
    private void ShowFallback(string reason) {
        App.LogDiag($"[DSH-WEB] init failed: {reason}");
        Content = new StackPanel {
            Margin = new Thickness(24),
            Spacing = 8,
            Children = {
                new TextBlock { Text = "WebView2 初始化失败，无法加载 DSH Web UI。", FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = reason, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "请安装 Microsoft Edge WebView2 Runtime 后重试。", TextWrapping = TextWrapping.Wrap },
            }
        };
    }

    /// <summary>窗口关闭：注销目录选择器 + 停止 HTTP/WS 宿主（WebView2 由控件自身关闭）</summary>
    protected override void OnClosed(EventArgs e) {
        DshDirectoryPicker.Register(null);
        _ = _host.DisposeAsync().AsTask().ContinueWith(task => {
            if (task.IsFaulted) {
                App.LogDiag($"[DSH-WEB] host dispose failed: {task.Exception?.Message}");
            }
        }, TaskScheduler.Default);
        base.OnClosed(e);
    }
}
