namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// ProcessStartInfo 统一构建器接口 — 强制三道防线 + 统一编码
/// <para>
/// 三道防线：
/// 1. 参数白名单校验 — <c>CommandArgumentValidator</c> 黑名单拦截 shell 元字符
/// 2. 参数化启动 — <c>ProcessStartInfo.ArgumentList</c> 优先于 <c>ProcessStartInfo.Arguments</c>
/// 3. 执行环境限制 — UseShellExecute=false + CreateNoWindow=true
/// </para>
/// <para>
/// 统一编码：options 显式指定编码优先，null 时回退到 <see cref="IProcessEncodingProvider"/> 全局编码。
/// </para>
/// </summary>
public interface IProcessStartInfoBuilder
{
    /// <summary>
    /// 从 <see cref="ProcessOptions"/> 构建 <see cref="System.Diagnostics.ProcessStartInfo"/> — 一次性执行模式
    /// </summary>
    System.Diagnostics.ProcessStartInfo Build(ProcessOptions options);

    /// <summary>
    /// 从 <see cref="InteractiveProcessOptions"/> 构建 <see cref="System.Diagnostics.ProcessStartInfo"/> — 交互式进程模式
    /// </summary>
    System.Diagnostics.ProcessStartInfo BuildInteractive(InteractiveProcessOptions options);

    /// <summary>
    /// 构建 UseShellExecute=true 的 ProcessStartInfo — 仅用于打开 URL/文件/目录（启动即忘模式）
    /// </summary>
    System.Diagnostics.ProcessStartInfo BuildShellOpen(string path);
}
