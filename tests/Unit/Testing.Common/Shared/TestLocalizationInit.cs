// 此文件通过 Directory.Build.props 链接到所有测试项目
// 确保本地化系统在测试启动时初始化为中文
namespace Testing.Common;

internal static class TestLocalizationInit
{
    [ModuleInitializer]
    internal static void Init()
    {
        Infrastructure.Localization.LocalizerInitializer.EnsureInitialized();
    }
}
