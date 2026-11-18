


namespace JoinCode.CliNode.Sample;

/// <summary>
/// 最小 CLI 节点 — 只依赖契约层与 BCL，不引用 Infrastructure / 引擎 / DI。
/// 用于实测单 exe 的依赖数、体积与编译时间，作为 540 个工具批量迁移的成本基线。
/// </summary>
internal sealed class SampleNode : ICliToolNode {
    private const string ResourceName = "JoinCode.CliNodes.Nodes.sample.yaml";
    private readonly Lazy<CliToolDescriptor> _descriptor = new(LoadDescriptor);

    public CliToolDescriptor Descriptor => _descriptor.Value;

    public Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (WantsHelp(args)) {
            Console.Out.WriteLine(CliToolHelp.Render(Descriptor));
            return Task.FromResult(0);
        }
        var text = string.Join(" ", args);
        Console.Out.WriteLine($"节点 {Descriptor.Id} 收到 {args.Length} 个参数，文本长度 {text.Length}");
        return Task.FromResult(0);
    }

    private static bool WantsHelp(string[] args) =>
        args.Any(arg => arg is "-h" or "--help" or "help");

    private static CliToolDescriptor LoadDescriptor() {
        using var stream = typeof(SampleNode).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"未找到内嵌资源 '{ResourceName}'");
        using var reader = new StreamReader(stream);
        return NodeManifestLoader.LoadNodeFromText(reader.ReadToEnd(), ResourceName);
    }
}
