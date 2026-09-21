namespace Testing.Common;

/// <summary>
/// 模块初始化器 — 在 testing.common 模块加载时强制设 Console 编码为 UTF-8。
/// <para>
/// 根因:Windows CI 上 <c>chcp 65001</c> 只改控制台代码页(GetConsoleOutputCP),
/// 但 .NET 运行时的 <c>Console.OutputEncoding</c> 默认读系统 ANSI 代码页(GetACP=936/GBK),
/// 不跟随 chcp。GBK 编码的中文被 GitHub Actions 按 UTF-8 解码 → 乱码。
/// </para>
/// <para>
/// 方案:用 <c>[ModuleInitializer]</c> 在模块加载时显式设 UTF-8,不依赖任何系统配置。
/// 所有引用 testing.common 的测试项目自动生效。
/// </para>
/// </summary>
internal static class ConsoleEncodingInitializer {
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Init() {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        System.Console.InputEncoding = System.Text.Encoding.UTF8;
    }
}
