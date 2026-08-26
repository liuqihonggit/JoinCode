using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace JoinCode.Gui.ViewModels;

/// <summary>
/// MainViewModel partial — 网络切换相关（需求7）。
/// 切换代理路由（HTTP_PROXY/HTTPS_PROXY 环境变量），不断物理连接。
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>需求7：网络模式 — Auto(自动检测)/Local(直连)/Proxy(走代理)</summary>
    [ObservableProperty]
    private string _networkMode = "Auto";

    /// <summary>需求7：代理地址（NetworkMode=Proxy 时使用，如 http://127.0.0.1:7890）</summary>
    [ObservableProperty]
    private string? _proxyUrl;

    /// <summary>需求7：可用网络模式选项</summary>
    public IReadOnlyList<string> NetworkModeOptions { get; } = ["Auto", "Local", "Proxy"];

    /// <summary>需求7：应用网络模式 — 切换代理路由（设置/清除环境变量）</summary>
    [RelayCommand]
    private void ApplyNetworkMode()
    {
        switch (NetworkMode)
        {
            case "Local":
                Environment.SetEnvironmentVariable("HTTP_PROXY", null);
                Environment.SetEnvironmentVariable("HTTPS_PROXY", null);
                Environment.SetEnvironmentVariable("http_proxy", null);
                Environment.SetEnvironmentVariable("https_proxy", null);
                StatusText = "已切换到本地直连";
                break;
            case "Proxy" when !string.IsNullOrWhiteSpace(ProxyUrl):
                Environment.SetEnvironmentVariable("HTTP_PROXY", ProxyUrl);
                Environment.SetEnvironmentVariable("HTTPS_PROXY", ProxyUrl);
                Environment.SetEnvironmentVariable("http_proxy", ProxyUrl);
                Environment.SetEnvironmentVariable("https_proxy", ProxyUrl);
                StatusText = $"已切换到代理: {ProxyUrl}";
                break;
            case "Auto":
                StatusText = "自动检测网络（VPN 优先）";
                break;
        }
        SavePreferences();
    }
}
