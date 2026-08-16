namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 进程编码统一管理接口 — 提供进程 stdin/stdout/stderr 编码的统一入口
/// <para>
/// 核心价值：
/// 1. 消除 64+ 处硬编码 <c>Encoding.UTF8</c> 的散弹式修改 — 未来编码切换只需改一处
/// 2. 支持 UTF-8 / 本地编码随时切换 — <see cref="UseUtf8"/> / <see cref="UseLocal"/>
/// 3. 线程安全 — 内部用 volatile 字段，读取端无锁
/// </para>
/// <para>
/// DI 注册为单例，
/// 所有 <see cref="ProcessOptions"/> / <see cref="InteractiveProcessOptions"/> 的编码字段为 null 时自动回退到此接口提供的编码。
/// </para>
/// </summary>
public interface IProcessEncodingProvider
{
    /// <summary>进程 stdout 编码</summary>
    Encoding Output { get; }

    /// <summary>进程 stderr 编码</summary>
    Encoding Error { get; }

    /// <summary>进程 stdin 编码</summary>
    Encoding Input { get; }

    /// <summary>当前是否为 UTF-8 模式</summary>
    bool IsUtf8Mode { get; }

    /// <summary>切换到 UTF-8 编码（默认模式）</summary>
    void UseUtf8();

    /// <summary>切换到系统本地编码（<see cref="Encoding.Default"/>）</summary>
    void UseLocal();

    /// <summary>设置自定义编码 — 同时应用于 Output/Error/Input</summary>
    /// <param name="encoding">自定义编码实例</param>
    void SetEncoding(Encoding encoding);
}
