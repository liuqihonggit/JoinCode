
namespace Core.Configuration;

/// <summary>
/// 简要模式服务实现
/// </summary>
[Register]
public partial class BriefModeService : IBriefModeService
{
    private bool _isEnabled;
    private DateTime? _enabledAt;
    private bool _userMsgOptIn;
    [Inject] private readonly IClockService _clock;

    public bool IsEnabled => _isEnabled;

    public DateTime? EnabledAt => _enabledAt;

    /// <summary>
    /// 用户显式 opt-in — 对齐 TS userMsgOptIn
    /// </summary>
    public bool UserMsgOptIn
    {
        get => _userMsgOptIn;
        set => _userMsgOptIn = value;
    }

    public void Enable()
    {
        _isEnabled = true;
        _enabledAt = _clock.GetLocalNow();
        _userMsgOptIn = true; // 对齐 TS: setUserMsgOptIn(true)
    }

    public void Disable()
    {
        _isEnabled = false;
        _enabledAt = null;
        _userMsgOptIn = false; // 对齐 TS: setUserMsgOptIn(false)
    }

    public bool Toggle()
    {
        if (_isEnabled)
        {
            Disable();
        }
        else
        {
            Enable();
        }
        return _isEnabled;
    }

    public BriefModeStatus GetStatus()
    {
        return _isEnabled
            ? BriefModeStatus.Enabled(_enabledAt ?? _clock.GetLocalNow())
            : BriefModeStatus.Disabled();
    }
}
