


namespace JoinCode.Rg;

/// <summary>
/// jcc-rg 独立 CLI 节点 — ADR 0157 v2 的垂直切片样板。
/// 节点自己持有 node.yaml 自描述、自己响应 -h、自己执行；不依赖中央源码生成器注册。
/// </summary>
internal sealed class RgCliToolNode : ICliToolNode {
    private const string ResourceName = "JoinCode.CliNodes.Nodes.rg.yaml";
    private readonly Lazy<CliToolDescriptor> _descriptor = new(LoadDescriptor);

    public CliToolDescriptor Descriptor => _descriptor.Value;

    public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken) {
        if (WantsHelp(args)) {
            Console.Out.WriteLine(CliToolHelp.Render(Descriptor));
            return 0;
        }
        return await RunSearchAsync(args, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> RunSearchAsync(string[] args, CancellationToken cancellationToken) {
        var rgArgs = new string[args.Length + 1];
        rgArgs[0] = "rg";
        Array.Copy(args, 0, rgArgs, 1, args.Length);
        return await RgSubCommand.ExecuteAsync(rgArgs, cancellationToken).ConfigureAwait(false) ?? 1;
    }

    private static bool WantsHelp(string[] args) =>
        args.Any(arg => arg is "-h" or "--help" or "help");

    private static CliToolDescriptor LoadDescriptor() {
        using var stream = typeof(RgCliToolNode).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"未找到内嵌资源 '{ResourceName}'，请检查 csproj 的 EmbeddedResource 声明");
        using var reader = new StreamReader(stream);
        return NodeManifestLoader.LoadNodeFromText(reader.ReadToEnd(), ResourceName);
    }
}
