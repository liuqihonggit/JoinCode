// 让本程序集的 AvaloniaFact 测试使用 TestApp 作为宿主（复用与真实 App 完全一致的资源注册）
[assembly: AvaloniaTestApplication(typeof(JoinCode.Gui.Tests.VisualTestApp))]

namespace JoinCode.Gui.Tests;

/// <summary>
/// Headless 测试宿主 —— 与真实 App 共用 <see cref="GuiAppResources"/> 注册，
/// 并启用 Skia 渲染（UseHeadlessDrawing=false + UseSkia）以支持捕获真实渲染帧做视觉断言。
/// </summary>
public sealed class VisualTestApp : Application
{
    public override void Initialize()
    {
        // 镜像真实 App.axaml 的 RequestedThemeVariant=Dark 启动默认，确保首帧即 Dark、只有明/暗二选一
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        GuiAppResources.Register(this);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<VisualTestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false
            });
}