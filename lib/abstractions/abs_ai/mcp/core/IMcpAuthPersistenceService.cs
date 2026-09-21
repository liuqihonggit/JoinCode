namespace JoinCode.Abstractions.Interfaces;

/// <summary>MCP 认证持久化服务接口。</summary>
public interface IMcpAuthPersistenceService {
    /// <summary>异步保存认证配置。</summary>
    Task SaveAsync(string authName, string authType, string serializedData, CancellationToken ct = default);
    /// <summary>异步按名称加载认证配置。</summary>
    Task<AuthConfigEntry?> LoadAsync(string authName, CancellationToken ct = default);
    /// <summary>异步列出所有认证配置。</summary>
    Task<IReadOnlyList<AuthConfigEntry>> ListAsync(CancellationToken ct = default);
    /// <summary>异步按名称移除认证配置。</summary>
    Task RemoveAsync(string authName, CancellationToken ct = default);
}

/// <summary>认证配置条目。</summary>
public sealed class AuthConfigEntry {
    /// <summary>获取认证名称。</summary>
    public required string Name { get; init; }
    /// <summary>获取认证类型。</summary>
    public required string AuthType { get; init; }
    /// <summary>获取序列化的认证数据。</summary>
    public required string Data { get; init; }
    /// <summary>获取保存时间。</summary>
    public required DateTime SavedAt { get; init; }
}
