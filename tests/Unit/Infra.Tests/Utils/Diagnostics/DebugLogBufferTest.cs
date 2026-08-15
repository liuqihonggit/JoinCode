using Infrastructure.Utils.Diagnostics;
using JoinCode.Abstractions.Utils.Diagnostics;

namespace Infra.Tests.Utils.Diagnostics;

/// <summary>
/// DebugLogBuffer 单元测试 — 验证环形缓冲区、分类逻辑、事件隔离
/// 注意: Diag.DiagnosticLineWritten 是静态事件，DebugLogBuffer 构造时订阅但未暴露取消订阅方法。
/// 测试中每个方法先 Clear 确保干净状态，避免跨测试污染。
/// </summary>
public sealed class DebugLogBufferTest
{
    private readonly DebugLogBuffer _buffer;

    public DebugLogBufferTest()
    {
        _buffer = new DebugLogBuffer(maxCapacity: 100);
    }

    #region Add / Count

    [Fact]
    public void Count_InitiallyZero()
    {
        _buffer.Count.Should().Be(0);
    }

    [Fact]
    public void Count_IncrementedByDiagWriteLine()
    {
        _buffer.Clear();
        Diag.WriteLine("[STEP] test message");
        _buffer.Count.Should().Be(1);
    }

    [Fact]
    public void Count_IncrementedByDiagWriteError()
    {
        _buffer.Clear();
        Diag.WriteError("test context", new InvalidOperationException("boom"));
        // WriteError 发送主异常行 + 可能的堆栈/内部异常行
        _buffer.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Count_IncrementedByDiagWriteLifecycle()
    {
        _buffer.Clear();
        Diag.WriteLifecycle("[READY] system ready");
        _buffer.Count.Should().Be(1);
    }

    #endregion

    #region GetRecent

    [Fact]
    public void GetRecent_ReturnsMostRecentFirst()
    {
        _buffer.Clear();
        Diag.WriteLine("[STEP] first");
        Diag.WriteLine("[STEP] second");
        Diag.WriteLine("[STEP] third");

        var recent = _buffer.GetRecent(2);
        recent.Should().HaveCount(2);
        recent[0].Message.Should().Be("[STEP] third");
        recent[1].Message.Should().Be("[STEP] second");
    }

    [Fact]
    public void GetRecent_DefaultCountIs100()
    {
        _buffer.Clear();
        for (var i = 0; i < 150; i++)
            Diag.WriteLine($"[STEP] msg{i}");

        var recent = _buffer.GetRecent();
        recent.Should().HaveCount(100);
    }

    [Fact]
    public void GetRecent_CountExceedsBuffer_ReturnsAllAvailable()
    {
        _buffer.Clear();
        Diag.WriteLine("[STEP] only-one");
        var recent = _buffer.GetRecent(50);
        recent.Should().HaveCount(1);
    }

    [Fact]
    public void GetRecent_ZeroCount_ThrowsArgumentOutOfRangeException()
    {
        var act = () => _buffer.GetRecent(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetRecent_NegativeCount_ThrowsArgumentOutOfRangeException()
    {
        var act = () => _buffer.GetRecent(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region GetByLevel

    [Fact]
    public void GetByLevel_FiltersCorrectly()
    {
        _buffer.Clear();
        Diag.WriteLine("[STEP] info msg");
        Diag.WriteError("err context", new Exception("err msg"));

        var errors = _buffer.GetByLevel(DebugLogLevel.Error);
        errors.Should().OnlyContain(e => e.Level == DebugLogLevel.Error);
    }

    [Fact]
    public void GetByLevel_ReturnsMostRecentFirst()
    {
        _buffer.Clear();
        Diag.WriteError("first error", new Exception("e1"));
        Diag.WriteError("second error", new Exception("e2"));

        var errors = _buffer.GetByLevel(DebugLogLevel.Error);
        errors.Should().HaveCountGreaterThanOrEqualTo(2);
        // 最新的在前面
        errors[0].Message.Should().Contain("second error");
    }

    [Fact]
    public void GetByLevel_NoMatchingEntries_ReturnsEmpty()
    {
        _buffer.Clear();
        Diag.WriteLine("[STEP] info only");
        var errors = _buffer.GetByLevel(DebugLogLevel.Error);
        errors.Should().BeEmpty();
    }

    [Fact]
    public void GetByLevel_ZeroCount_ThrowsArgumentOutOfRangeException()
    {
        var act = () => _buffer.GetByLevel(DebugLogLevel.Info, 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region GetByMinLevel

    [Fact]
    public void GetByMinLevel_IncludesLevelAndAbove()
    {
        _buffer.Clear();
        Diag.WriteLine("[WIRE] trace msg");        // Trace
        Diag.WriteLine("[STEP] info msg");          // Info
        Diag.WriteError("err", new Exception("e")); // Error

        var warnAndAbove = _buffer.GetByMinLevel(DebugLogLevel.Warn);
        warnAndAbove.Should().OnlyContain(e => e.Level >= DebugLogLevel.Warn);
    }

    [Fact]
    public void GetByMinLevel_InfoIncludesInfoWarnError()
    {
        _buffer.Clear();
        Diag.WriteLine("[WIRE] trace msg");        // Trace
        Diag.WriteLine("[STEP] info msg");          // Info
        Diag.WriteError("err", new Exception("e")); // Error

        var infoAndAbove = _buffer.GetByMinLevel(DebugLogLevel.Info);
        infoAndAbove.Should().OnlyContain(e => e.Level >= DebugLogLevel.Info);
    }

    [Fact]
    public void GetByMinLevel_TraceIncludesAll()
    {
        _buffer.Clear();
        Diag.WriteLine("[WIRE] trace msg");
        Diag.WriteLine("[STEP] info msg");

        var all = _buffer.GetByMinLevel(DebugLogLevel.Trace);
        all.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void GetByMinLevel_ZeroCount_ThrowsArgumentOutOfRangeException()
    {
        var act = () => _buffer.GetByMinLevel(DebugLogLevel.Info, 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region Clear

    [Fact]
    public void Clear_EmptiesBuffer()
    {
        _buffer.Clear();
        Diag.WriteLine("[STEP] msg1");
        Diag.WriteLine("[STEP] msg2");
        _buffer.Count.Should().BeGreaterThanOrEqualTo(2);

        _buffer.Clear();
        _buffer.Count.Should().Be(0);
    }

    [Fact]
    public void Clear_OnEmptyBuffer_DoesNotThrow()
    {
        _buffer.Clear();
        var act = () => _buffer.Clear();
        act.Should().NotThrow();
    }

    [Fact]
    public void Clear_AfterClear_NewEntriesAreCaptured()
    {
        _buffer.Clear();
        Diag.WriteLine("[STEP] before-clear");
        _buffer.Clear();
        Diag.WriteLine("[STEP] after-clear");

        var recent = _buffer.GetRecent();
        recent.Should().Contain(e => e.Message == "[STEP] after-clear");
        recent.Should().NotContain(e => e.Message == "[STEP] before-clear");
    }

    #endregion

    #region Ring Buffer Overflow

    [Fact]
    public void Overflow_DiscardsOldestEntries()
    {
        // 使用小容量缓冲区测试溢出
        var smallBuffer = new DebugLogBuffer(maxCapacity: 5);
        smallBuffer.Clear();

        for (var i = 0; i < 10; i++)
            Diag.WriteLine($"[STEP] msg{i}");

        smallBuffer.Count.Should().Be(5);

        var recent = smallBuffer.GetRecent(10);
        // 应该保留最新的 5 条（msg5~msg9）
        recent.Should().HaveCount(5);
        recent[0].Message.Should().Be("[STEP] msg9");
    }

    [Fact]
    public void Overflow_WithCapacity1_KeepsOnlyLatest()
    {
        var tinyBuffer = new DebugLogBuffer(maxCapacity: 1);
        tinyBuffer.Clear();

        Diag.WriteLine("[STEP] first");
        Diag.WriteLine("[STEP] second");

        tinyBuffer.Count.Should().Be(1);
        var recent = tinyBuffer.GetRecent();
        recent[0].Message.Should().Be("[STEP] second");
    }

    [Fact]
    public void Constructor_ZeroCapacity_ThrowsArgumentOutOfRangeException()
    {
        var act = () => new DebugLogBuffer(maxCapacity: 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_NegativeCapacity_ThrowsArgumentOutOfRangeException()
    {
        var act = () => new DebugLogBuffer(maxCapacity: -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region ClassifyMessage

    [Fact]
    public void ClassifyMessage_DiagErr_IsError()
    {
        _buffer.Clear();
        Diag.WriteError("test", new Exception("err"));
        var errors = _buffer.GetByLevel(DebugLogLevel.Error);
        errors.Should().NotBeEmpty();
        errors[0].Category.Should().Be("ERROR");
    }

    [Fact]
    public void ClassifyMessage_DiagErrStack_IsError()
    {
        _buffer.Clear();
        var ex = new Exception("outer", new Exception("inner"));
        Diag.WriteError("stack-test", ex);
        var errors = _buffer.GetByLevel(DebugLogLevel.Error);
        // [DIAG-ERR] 和 [DIAG-ERR-STACK] 都应归类为 Error
        errors.Should().OnlyContain(e => e.Level == DebugLogLevel.Error);
    }

    [Fact]
    public void ClassifyMessage_Wire_IsTrace()
    {
        _buffer.Clear();
        Diag.WriteLine("[WIRE] request sent");
        var traces = _buffer.GetByLevel(DebugLogLevel.Trace);
        traces.Should().Contain(e => e.Category == "WIRE");
    }

    [Fact]
    public void ClassifyMessage_Step_IsInfo()
    {
        _buffer.Clear();
        Diag.WriteLine("[STEP] processing turn");
        var infos = _buffer.GetByLevel(DebugLogLevel.Info);
        infos.Should().Contain(e => e.Category == "STEP");
    }

    [Fact]
    public void ClassifyMessage_Ready_IsInfo()
    {
        _buffer.Clear();
        Diag.WriteLine("[READY] system initialized");
        var infos = _buffer.GetByLevel(DebugLogLevel.Info);
        infos.Should().Contain(e => e.Category == "READY");
    }

    [Fact]
    public void ClassifyMessage_DI_IsTrace()
    {
        _buffer.Clear();
        Diag.WriteLine("[DI] registering service");
        var traces = _buffer.GetByLevel(DebugLogLevel.Trace);
        traces.Should().Contain(e => e.Category == "DI");
    }

    [Fact]
    public void ClassifyMessage_Alive_IsTrace()
    {
        _buffer.Clear();
        Diag.WriteLine("[ALIVE] heartbeat");
        var traces = _buffer.GetByLevel(DebugLogLevel.Trace);
        traces.Should().Contain(e => e.Category == "ALIVE");
    }

    [Fact]
    public void ClassifyMessage_DiagTerm_IsTrace()
    {
        _buffer.Clear();
        Diag.WriteLine("[DIAG-TERM] shutdown signal");
        var traces = _buffer.GetByLevel(DebugLogLevel.Trace);
        traces.Should().Contain(e => e.Category == "TERM");
    }

    [Fact]
    public void ClassifyMessage_CrashStore_IsError()
    {
        _buffer.Clear();
        Diag.WriteLine("[CrashStore] captured exception");
        var errors = _buffer.GetByLevel(DebugLogLevel.Error);
        errors.Should().Contain(e => e.Category == "CRASH");
    }

    [Fact]
    public void ClassifyMessage_Run_IsInfo()
    {
        _buffer.Clear();
        Diag.WriteLine("[RUN] executing tool");
        var infos = _buffer.GetByLevel(DebugLogLevel.Info);
        infos.Should().Contain(e => e.Category == "RUN");
    }

    [Fact]
    public void ClassifyMessage_UnknownBracketTag_IsInfoWithCategory()
    {
        _buffer.Clear();
        Diag.WriteLine("[CUSTOM-TAG] custom message");
        var infos = _buffer.GetByLevel(DebugLogLevel.Info);
        infos.Should().Contain(e => e.Category == "[CUSTOM-TAG]");
    }

    [Fact]
    public void ClassifyMessage_NoBracketPrefix_IsGeneralInfo()
    {
        _buffer.Clear();
        Diag.WriteLine("plain message without prefix");
        var infos = _buffer.GetByLevel(DebugLogLevel.Info);
        infos.Should().Contain(e => e.Category == "GENERAL");
    }

    [Fact]
    public void ClassifyMessage_EmptyString_IsGeneralInfo()
    {
        _buffer.Clear();
        Diag.WriteLine(string.Empty);
        var infos = _buffer.GetByLevel(DebugLogLevel.Info);
        infos.Should().Contain(e => e.Category == "GENERAL" && e.Message == string.Empty);
    }

    #endregion

    #region Event Isolation

    [Fact]
    public void EventIsolation_MultipleBuffers_BothReceiveEvents()
    {
        _buffer.Clear();
        var buffer2 = new DebugLogBuffer(maxCapacity: 50);
        buffer2.Clear();

        Diag.WriteLine("[STEP] shared event");

        _buffer.Count.Should().BeGreaterThanOrEqualTo(1);
        buffer2.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    #endregion

    #region DebugLogEntry Properties

    [Fact]
    public void Entry_HasValidTimestamp()
    {
        _buffer.Clear();
        var before = DateTimeOffset.UtcNow;
        Diag.WriteLine("[STEP] timestamp-test");
        var after = DateTimeOffset.UtcNow;

        var recent = _buffer.GetRecent(1);
        recent[0].Timestamp.Should().BeOnOrAfter(before);
        recent[0].Timestamp.Should().BeOnOrBefore(after);
    }

    [Fact]
    public void Entry_PreservesOriginalMessage()
    {
        _buffer.Clear();
        const string original = "[STEP] preserve exact message content";
        Diag.WriteLine(original);

        var recent = _buffer.GetRecent(1);
        recent[0].Message.Should().Be(original);
    }

    #endregion
}
