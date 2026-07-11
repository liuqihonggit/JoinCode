namespace Tools.Shell;

/// <summary>
/// Shell 执行中间件接口 — 拦截和转换 Shell 命令执行流程
/// 继承通用 Task 中间件接口，复用管道构建和异常捕获机制
/// </summary>
public interface IShellMiddleware : IMiddleware<ShellContext> { }
