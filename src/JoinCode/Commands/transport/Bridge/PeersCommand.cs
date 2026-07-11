
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Peers, Description = "列出对等节点", Usage = "/peers", Category = ChatCommandCategory.Bridge)]
public sealed class PeersCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Peers;
    public string Description => "列出对等节点";
    public string Usage => "/peers";
    public string[] Aliases => ["remote"];
    public string ArgumentHint => string.Empty;
    public bool IsHidden => true;

    public Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var sp = context.Services!.ServiceProvider;
        var peerService = sp?.GetService<JoinCode.Abstractions.Interfaces.IPeerDiscoveryService>();

        if (peerService is null)
        {
            if (!Core.Utils.TestEnvironmentDetector.IsNonInteractive)
            {
                TerminalHelper.WriteLine("对等节点发现服务未初始化");
            }
            return Task.FromResult(ChatCommandResult.Continue());
        }

        var peers = peerService.GetConnectedPeers();

        TerminalHelper.WriteLine("对等节点列表:");
        TerminalHelper.NewLine();

        if (peers.Count == 0)
        {
            TerminalHelper.WriteLine("  (暂无已连接的对等节点)");
        }
        else
        {
            foreach (var peer in peers)
            {
                TerminalHelper.WriteLine($"  {peer.Name} ({peer.Id})");
                TerminalHelper.WriteLine($"    连接时间: {peer.ConnectedAt:HH:mm:ss}");
            }
        }

        return Task.FromResult(ChatCommandResult.Continue());
    }
}
