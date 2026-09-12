
namespace JoinCode.Abstractions.Models.Ssh;

public enum SshConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Error
}

public enum SshAuthMethod
{
    Password,
    PrivateKey,
    SshAgent,
    Certificate
}

public enum SshForwardType
{
    Local,
    Remote,
    Dynamic
}

public enum SshKnownHostsPolicy
{
    Strict,
    AcceptNew,
    Ignore
}
