
namespace JoinCode.ChatCommands;

/// <summary>
/// /peers 命令 — 列出当前已连接的对等节点
/// 别名: /remote
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Peers, Description = "列出对等节点", Usage = "/peers", Category = ChatCommandCategory.Bridge, Aliases = ["remote"], IsHidden = true)]
public sealed class PeersCommand : ChatCommandBase
{
    /// <summary>
    /// 执行 /peers 命令，输出已连接对等节点列表
    /// </summary>
    /// <param name="context">命令执行上下文，提供服务提供者与取消令牌</param>
    /// <returns>表示命令执行结果的 <see cref="ChatCommandResult"/>，始终为 Continue</returns>
    public override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var sp = context.Services;
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

        if (!peers.Any())
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
