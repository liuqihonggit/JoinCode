namespace JoinCode.Abstractions.Utils;

public enum ApiErrorType
{
    None,
    InvalidApiKey,
    InvalidApiKeyExternal,
    CreditBalanceTooLow,
    PromptTooLong,
    RateLimit,
    TokenRevoked,
    ApiTimeout,
    OrgDisabled,
    OrgDisabledWithOAuth,
    CustomOffSwitch,
    UserAbort,
    RepeatedOverloaded,
    ConnectionError,
    ServerError,
    Unknown
}
