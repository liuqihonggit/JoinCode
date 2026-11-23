namespace JoinCode.Abstractions.Tools;

/// <summary>
/// CLI 节点契约 — 每个独立 CLI.exe 实现此接口。
/// 节点自己持有描述（node.yaml），自己响应 -h，自己执行；不需要中央生成器按程序集扫描注册。
/// </summary>
public interface ICliToolNode {
    /// <summary>节点自描述。</summary>
    CliToolDescriptor Descriptor { get; }

    /// <summary>执行节点，返回进程退出码。</summary>
    Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken);
}
