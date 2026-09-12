#nullable disable

namespace Bridge.Tests;

/// <summary>
/// Bridge 扩展消息处理器单元测试
/// 测试 AuthHandler、DeviceTrustHandler、SecretHandler 以及 ControlRequestHandlerBase 公共行为
/// </summary>
public sealed class BridgeExtensionHandlersTests
{
    private const string TestJwtSecret = "test-secret-key-for-bridge-jwt-at-least-32-chars";

    private static BridgeJwtService CreateJwtService() =>
        new(new BridgeConfig { JwtSecretKey = TestJwtSecret }, NullLogger.Instance);

    private static ControlRequest CreateRequest(Dictionary<string, JsonElement> parameters = null!) =>
        new()
        {
            Id = "req-1",
            Command = "test",
            Params = parameters ?? new Dictionary<string, JsonElement>()
        };

    [Fact]
    public void AuthHandler_Constructor_NullJwtService_ThrowsArgumentNullException()
    {
        var act = () => new AuthHandler(null);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task AuthHandler_MessageType_IsAuthVerify()
    {
        var handler = new AuthHandler(CreateJwtService());

        handler.MessageType.Should().Be("auth/verify");

        var request = CreateRequest(new Dictionary<string, JsonElement>
        {
            ["token"] = JsonSerializer.SerializeToElement("invalid-token")
        });
        var result = await handler.HandleAsync(request, new MessageHandlerContext(), CancellationToken.None).ConfigureAwait(true);

        result.Should().BeOfType<ControlResponse>();
    }

    [Fact]
    public async Task AuthHandler_MissingToken_ReturnsError()
    {
        var handler = new AuthHandler(CreateJwtService());
        var request = CreateRequest();

        var result = await handler.HandleAsync(request, new MessageHandlerContext(), CancellationToken.None).ConfigureAwait(true);

        var response = result.Should().BeOfType<ControlResponse>().Subject;
        response.Success.Should().BeFalse();
        response.Error.Should().Be("Missing 'token' parameter");
    }

    [Fact]
    public async Task AuthHandler_EmptyToken_ReturnsError()
    {
        var handler = new AuthHandler(CreateJwtService());
        var request = CreateRequest(new Dictionary<string, JsonElement>
        {
            ["token"] = JsonSerializer.SerializeToElement("   ")
        });

        var result = await handler.HandleAsync(request, new MessageHandlerContext(), CancellationToken.None).ConfigureAwait(true);

        var response = result.Should().BeOfType<ControlResponse>().Subject;
        response.Success.Should().BeFalse();
        response.Error.Should().Be("Token is empty");
    }

    [Fact]
    public async Task AuthHandler_ValidToken_ReturnsSuccessWithClientId()
    {
        var jwtService = CreateJwtService();
        var token = jwtService.GenerateToken("client-001");
        var handler = new AuthHandler(jwtService);
        var request = CreateRequest(new Dictionary<string, JsonElement>
        {
            ["token"] = JsonSerializer.SerializeToElement(token)
        });

        var result = await handler.HandleAsync(request, new MessageHandlerContext(), CancellationToken.None).ConfigureAwait(true);

        var response = result.Should().BeOfType<ControlResponse>().Subject;
        response.Success.Should().BeTrue();
        response.Result.Should().NotBeNull();
    }

    [Fact]
    public async Task AuthHandler_InvalidToken_ReturnsError()
    {
        var handler = new AuthHandler(CreateJwtService());
        var request = CreateRequest(new Dictionary<string, JsonElement>
        {
            ["token"] = JsonSerializer.SerializeToElement("not.a.valid.token")
        });

        var result = await handler.HandleAsync(request, new MessageHandlerContext(), CancellationToken.None).ConfigureAwait(true);

        var response = result.Should().BeOfType<ControlResponse>().Subject;
        response.Success.Should().BeFalse();
        response.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void DeviceTrustHandler_Constructor_NullStore_ThrowsArgumentNullException()
    {
        var act = () => new DeviceTrustHandler(null);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task DeviceTrustHandler_VerifyTrustedDevice_ReturnsTrue()
    {
        var store = new TrustedDeviceStore(NullLogger<TrustedDeviceStore>.Instance);
        await store.AddAsync(new TrustedDeviceEntry
        {
            DeviceId = "device-1",
            DeviceName = "Test Device",
            PublicKeyFingerprint = "fp-1",
            TrustLevel = DeviceTrustLevel.Basic
        }).ConfigureAwait(true);

        var handler = new DeviceTrustHandler(store);
        var request = CreateRequest(new Dictionary<string, JsonElement>
        {
            ["action"] = JsonSerializer.SerializeToElement("verify"),
            ["deviceId"] = JsonSerializer.SerializeToElement("device-1")
        });

        var result = await handler.HandleAsync(request, new MessageHandlerContext(), CancellationToken.None).ConfigureAwait(true);

        var response = result.Should().BeOfType<ControlResponse>().Subject;
        response.Success.Should().BeTrue();
        response.Result.Should().NotBeNull();
    }

    [Fact]
    public async Task DeviceTrustHandler_VerifyMissingDeviceId_ReturnsError()
    {
        var handler = new DeviceTrustHandler(new TrustedDeviceStore());
        var request = CreateRequest(new Dictionary<string, JsonElement>
        {
            ["action"] = JsonSerializer.SerializeToElement("verify")
        });

        var result = await handler.HandleAsync(request, new MessageHandlerContext(), CancellationToken.None).ConfigureAwait(true);

        var response = result.Should().BeOfType<ControlResponse>().Subject;
        response.Success.Should().BeFalse();
        response.Error.Should().Contain("deviceId");
    }

    [Fact]
    public async Task DeviceTrustHandler_TrustDevice_AddsToStore()
    {
        var store = new TrustedDeviceStore(NullLogger<TrustedDeviceStore>.Instance);
        var handler = new DeviceTrustHandler(store);
        var request = CreateRequest(new Dictionary<string, JsonElement>
        {
            ["action"] = JsonSerializer.SerializeToElement("trust"),
            ["deviceId"] = JsonSerializer.SerializeToElement("device-2"),
            ["deviceName"] = JsonSerializer.SerializeToElement("My Device"),
            ["publicKeyFingerprint"] = JsonSerializer.SerializeToElement("fp-2")
        });

        var result = await handler.HandleAsync(request, new MessageHandlerContext(), CancellationToken.None).ConfigureAwait(true);

        var response = result.Should().BeOfType<ControlResponse>().Subject;
        response.Success.Should().BeTrue();
        (await store.IsTrustedAsync("device-2").ConfigureAwait(true)).Should().BeTrue();
    }

    [Fact]
    public async Task DeviceTrustHandler_RevokeDevice_ReturnsTrueWhenExists()
    {
        var store = new TrustedDeviceStore(NullLogger<TrustedDeviceStore>.Instance);
        await store.AddAsync(new TrustedDeviceEntry
        {
            DeviceId = "device-3",
            DeviceName = "Test Device",
            PublicKeyFingerprint = "fp-3",
            TrustLevel = DeviceTrustLevel.Basic
        }).ConfigureAwait(true);

        var handler = new DeviceTrustHandler(store);
        var request = CreateRequest(new Dictionary<string, JsonElement>
        {
            ["action"] = JsonSerializer.SerializeToElement("revoke"),
            ["deviceId"] = JsonSerializer.SerializeToElement("device-3")
        });

        var result = await handler.HandleAsync(request, new MessageHandlerContext(), CancellationToken.None).ConfigureAwait(true);

        var response = result.Should().BeOfType<ControlResponse>().Subject;
        response.Success.Should().BeTrue();
    }

    [Fact]
    public async Task DeviceTrustHandler_RevokeUnknownDevice_ReturnsError()
    {
        var handler = new DeviceTrustHandler(new TrustedDeviceStore());
        var request = CreateRequest(new Dictionary<string, JsonElement>
        {
            ["action"] = JsonSerializer.SerializeToElement("revoke"),
            ["deviceId"] = JsonSerializer.SerializeToElement("missing")
        });

        var result = await handler.HandleAsync(request, new MessageHandlerContext(), CancellationToken.None).ConfigureAwait(true);

        var response = result.Should().BeOfType<ControlResponse>().Subject;
        response.Success.Should().BeFalse();
        response.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task DeviceTrustHandler_UnknownAction_ReturnsError()
    {
        var handler = new DeviceTrustHandler(new TrustedDeviceStore());
        var request = CreateRequest(new Dictionary<string, JsonElement>
        {
            ["action"] = JsonSerializer.SerializeToElement("destroy")
        });

        var result = await handler.HandleAsync(request, new MessageHandlerContext(), CancellationToken.None).ConfigureAwait(true);

        var response = result.Should().BeOfType<ControlResponse>().Subject;
        response.Success.Should().BeFalse();
        response.Error.Should().Contain("destroy");
    }

    [Fact]
    public void SecretHandler_Constructor_NullStore_ThrowsArgumentNullException()
    {
        var act = () => new SecretHandler(null);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task SecretHandler_ValidateCorrectValue_ReturnsTrue()
    {
        var config = new BridgeConfig { EncryptionKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) };
        var store = new WorkSecretStore(config, NullLogger<WorkSecretStore>.Instance);
        var entry = await store.CreateAsync("api-key", "secret-value").ConfigureAwait(true);

        var handler = new SecretHandler(store);
        var request = CreateRequest(new Dictionary<string, JsonElement>
        {
            ["action"] = JsonSerializer.SerializeToElement("validate"),
            ["secretId"] = JsonSerializer.SerializeToElement(entry.SecretId),
            ["plainValue"] = JsonSerializer.SerializeToElement("secret-value")
        });

        var result = await handler.HandleAsync(request, new MessageHandlerContext(), CancellationToken.None).ConfigureAwait(true);

        var response = result.Should().BeOfType<ControlResponse>().Subject;
        response.Success.Should().BeTrue();
    }

    [Fact]
    public async Task SecretHandler_ValidateMissingParameters_ReturnsError()
    {
        var handler = new SecretHandler(new WorkSecretStore());
        var request = CreateRequest(new Dictionary<string, JsonElement>
        {
            ["action"] = JsonSerializer.SerializeToElement("validate")
        });

        var result = await handler.HandleAsync(request, new MessageHandlerContext(), CancellationToken.None).ConfigureAwait(true);

        var response = result.Should().BeOfType<ControlResponse>().Subject;
        response.Success.Should().BeFalse();
    }

    [Fact]
    public async Task SecretHandler_Rotate_ReturnsNewSecretId()
    {
        var config = new BridgeConfig { EncryptionKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) };
        var store = new WorkSecretStore(config, NullLogger<WorkSecretStore>.Instance);
        var entry = await store.CreateAsync("api-key", "old-value").ConfigureAwait(true);

        var handler = new SecretHandler(store);
        var request = CreateRequest(new Dictionary<string, JsonElement>
        {
            ["action"] = JsonSerializer.SerializeToElement("rotate"),
            ["secretId"] = JsonSerializer.SerializeToElement(entry.SecretId),
            ["newPlainValue"] = JsonSerializer.SerializeToElement("new-value")
        });

        var result = await handler.HandleAsync(request, new MessageHandlerContext(), CancellationToken.None).ConfigureAwait(true);

        var response = result.Should().BeOfType<ControlResponse>().Subject;
        response.Success.Should().BeTrue();
        response.Result.Should().NotBeNull();
    }

    [Fact]
    public async Task SecretHandler_UnknownAction_ReturnsError()
    {
        var handler = new SecretHandler(new WorkSecretStore());
        var request = CreateRequest(new Dictionary<string, JsonElement>
        {
            ["action"] = JsonSerializer.SerializeToElement("delete")
        });

        var result = await handler.HandleAsync(request, new MessageHandlerContext(), CancellationToken.None).ConfigureAwait(true);

        var response = result.Should().BeOfType<ControlResponse>().Subject;
        response.Success.Should().BeFalse();
        response.Error.Should().Contain("delete");
    }

    [Fact]
    public async Task ControlRequestHandlerBase_HandleAsync_NonControlRequest_ReturnsErrorMessage()
    {
        var handler = new TestControlHandler();
        var message = new PingMessage { Id = "ping-1" };

        var result = await handler.HandleAsync(message, new MessageHandlerContext(), CancellationToken.None).ConfigureAwait(true);

        var error = result.Should().BeOfType<ErrorMessage>().Subject;
        error.Code.Should().Be(-32600);
    }

    [Fact]
    public async Task ControlRequestHandlerBase_HandleAsync_ExceptionInHandler_ReturnsErrorResponse()
    {
        var handler = new TestControlHandler { ThrowException = true };
        var request = CreateRequest();

        var result = await handler.HandleAsync(request, new MessageHandlerContext(), CancellationToken.None).ConfigureAwait(true);

        var response = result.Should().BeOfType<ControlResponse>().Subject;
        response.Success.Should().BeFalse();
        response.Error.Should().Be("boom");
    }

    [Fact]
    public void ControlRequestHandlerBase_GetOptionalString_ReturnsNullWhenMissing()
    {
        var parameters = new Dictionary<string, JsonElement>();

        var result = TestControlHandler.PublicGetOptionalString(parameters, "missing");

        result.Should().BeNull();
    }

    [Fact]
    public void ControlRequestHandlerBase_GetRequiredString_ReturnsEmptyWhenMissing()
    {
        var parameters = new Dictionary<string, JsonElement>();

        var result = TestControlHandler.PublicGetRequiredString(parameters, "missing");

        result.Should().BeEmpty();
    }

    private sealed class TestControlHandler : ControlRequestHandlerBase
    {
        public override string MessageType => "test/control";
        public bool ThrowException { get; init; }

        protected override Task<ControlResponse> HandleActionAsync(ControlRequest request, Dictionary<string, JsonElement> parameters, MessageHandlerContext context, CancellationToken cancellationToken)
        {
            if (ThrowException)
            {
                throw new InvalidOperationException("boom");
            }

            return Task.FromResult(CreateSuccessResponse(request));
        }

        public static string PublicGetOptionalString(Dictionary<string, JsonElement> parameters, string key)
            => GetOptionalString(parameters, key);

        public static string PublicGetRequiredString(Dictionary<string, JsonElement> parameters, string key)
            => GetRequiredString(parameters, key);
    }
}
