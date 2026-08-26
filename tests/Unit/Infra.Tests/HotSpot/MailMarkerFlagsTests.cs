namespace Infra.Tests.HotSpot;

using JoinCode.Abstractions.Models.Agent;

public sealed class MailMarkerFlagsTests
{
    [Fact]
    public void None_ShouldBeZero()
    {
        ((int)MailMarker.None).Should().Be(0);
    }

    [Fact]
    public void IndividualFlags_ShouldBePowerOfTwo()
    {
        ((int)MailMarker.HotFileConflict).Should().Be(1);
        ((int)MailMarker.TestFileConflict).Should().Be(2);
        ((int)MailMarker.ResourceRefChange).Should().Be(4);
    }

    [Fact]
    public void CombinedFlags_HasFlag_ShouldDetectEach()
    {
        var combined = MailMarker.HotFileConflict | MailMarker.TestFileConflict;

        combined.HasFlag(MailMarker.HotFileConflict).Should().BeTrue();
        combined.HasFlag(MailMarker.TestFileConflict).Should().BeTrue();
        combined.HasFlag(MailMarker.ResourceRefChange).Should().BeFalse();
    }

    [Fact]
    public void DeferredMail_IsHighPriority_WithHotFileConflict_ShouldBeTrue()
    {
        var mail = MakeMail(MailMarker.HotFileConflict);
        mail.IsHighPriority.Should().BeTrue();
    }

    [Fact]
    public void DeferredMail_IsHighPriority_WithCombinedHotFile_ShouldBeTrue()
    {
        var mail = MakeMail(MailMarker.HotFileConflict | MailMarker.TestFileConflict);
        mail.IsHighPriority.Should().BeTrue();
    }

    [Fact]
    public void DeferredMail_IsHighPriority_WithResourceRefChange_ShouldBeFalse()
    {
        var mail = MakeMail(MailMarker.ResourceRefChange);
        mail.IsHighPriority.Should().BeFalse();
    }

    [Fact]
    public void DeferredMail_IsHighPriority_WithNone_ShouldBeFalse()
    {
        var mail = MakeMail(MailMarker.None);
        mail.IsHighPriority.Should().BeFalse();
    }

    private static DeferredMail MakeMail(MailMarker marker) =>
        new() { To = "w1", From = "captain", Subject = "s", Body = "b", OpenAfterTurns = 1, Marker = marker, CreatedAt = DateTimeOffset.UtcNow };
}
