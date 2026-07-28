using JoinCode.Abstractions.Attributes;

namespace Core.Configuration;

/// <summary>
/// 功能权限服务默认实现 — 对齐 TS isBriefEntitled()/isBriefEnabled()
/// 开源项目默认允许所有功能，通过 JCC_BRIEF 环境变量控制
/// </summary>
[Register]
public sealed partial class EntitlementService : IEntitlementService
{
    private readonly IBriefModeService _briefModeService;

    public EntitlementService(IBriefModeService briefModeService)
    {
        _briefModeService = briefModeService ?? throw new ArgumentNullException(nameof(briefModeService));
    }

    /// <summary>
    /// Brief 模式是否有权限 — 对齐 TS isBriefEntitled()
    /// 开源项目默认 true；JCC_BRIEF 环境变量可强制开启/关闭
    /// </summary>
    public bool IsBriefEntitled
    {
        get
        {
            // 对齐 TS: isEnvTruthy(process.env.CLAUDE_CODE_BRIEF)
            var envValue = Environment.GetEnvironmentVariable(JccEnvVarConstants.Brief);
            if (!string.IsNullOrEmpty(envValue))
            {
                // 环境变量显式设置时遵循其值
                return !envValue.Equals("0", StringComparison.OrdinalIgnoreCase)
                    && !envValue.Equals("false", StringComparison.OrdinalIgnoreCase);
            }

            // 开源项目默认允许
            return true;
        }
    }

    /// <summary>
    /// Brief 工具是否当前激活 — 对齐 TS isBriefEnabled()
    /// 需要 entitlement 权限 + 用户 opt-in
    /// </summary>
    public bool IsBriefEnabled => _briefModeService.UserMsgOptIn && IsBriefEntitled;
}
