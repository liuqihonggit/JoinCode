namespace JoinCode.Abstractions.Utils;

public enum ApiErrorType {
    [EnumValue("none")] None,
    [EnumValue("invalid_api_key")] InvalidApiKey,
    [EnumValue("invalid_api_key_external")] InvalidApiKeyExternal,
    [EnumValue("credit_balance_too_low")] CreditBalanceTooLow,
    [EnumValue("prompt_too_long")] PromptTooLong,
    [EnumValue("rate_limit")] RateLimit,
    [EnumValue("token_revoked")] TokenRevoked,
    [EnumValue("api_timeout")] ApiTimeout,
    [EnumValue("org_disabled")] OrgDisabled,
    [EnumValue("org_disabled_with_oauth")] OrgDisabledWithOAuth,
    [EnumValue("custom_off_switch")] CustomOffSwitch,
    [EnumValue("user_abort")] UserAbort,
    [EnumValue("repeated_overloaded")] RepeatedOverloaded,
    [EnumValue("connection_error")] ConnectionError,
    [EnumValue("server_error")] ServerError,
    [EnumValue("unknown")] Unknown
}