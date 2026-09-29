namespace Core.Utils;

public class HostContextSyncServiceExtractJsonFieldTests {
    [Fact]
    public void extracts_simple_field() {
        var json = """{"timestamp":"2026-09-30T12:00:00Z","hostPid":"12345"}""";
        HostContextSyncService.ExtractJsonField(json, "timestamp")
            .Should().Be("2026-09-30T12:00:00Z");
        HostContextSyncService.ExtractJsonField(json, "hostPid")
            .Should().Be("12345");
    }

    [Fact]
    public void returns_null_for_missing_field() {
        var json = """{"timestamp":"2026-09-30T12:00:00Z"}""";
        HostContextSyncService.ExtractJsonField(json, "hostPid")
            .Should().BeNull();
    }

    [Fact]
    public void returns_null_for_empty_json() {
        HostContextSyncService.ExtractJsonField("", "timestamp").Should().BeNull();
        HostContextSyncService.ExtractJsonField("   ", "timestamp").Should().BeNull();
    }

    [Fact]
    public void extracts_field_with_special_chars() {
        var json = """{"hostPid":"abc-123_def"}""";
        HostContextSyncService.ExtractJsonField(json, "hostPid")
            .Should().Be("abc-123_def");
    }

    [Fact]
    public void extracts_first_match_when_duplicate() {
        var json = """{"x":"first","x":"second"}""";
        HostContextSyncService.ExtractJsonField(json, "x")
            .Should().Be("first");
    }
}

public class HostContextSyncServiceSerializeSnapshotTests {
    private static HostContextSnapshot MakeSnapshot(
        DateTimeOffset? ts = null,
        string hostPid = "host-123",
        Dictionary<string, string>? routing = null,
        int pending = 0,
        int running = 0) => new() {
        Timestamp = ts ?? new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero),
        HostProcessId = hostPid,
        RoutingTable = routing ?? new Dictionary<string, string>(),
        PendingMessages = new Dictionary<string, IReadOnlyList<ReadOnlyMemory<byte>>>(),
        BuildQueue = new BuildQueueState {
            PendingCount = pending,
            RunningCount = running,
            PendingTasks = Array.Empty<string>()
        }
    };

    [Fact]
    public void serializes_empty_routing() {
        var snap = MakeSnapshot();
        var json = HostContextSyncService.SerializeSnapshot(snap);
        json.Should().Contain("\"timestamp\":\"2026-09-30T12:00:00");
        json.Should().Contain("\"hostPid\":\"host-123\"");
        json.Should().Contain("\"routing\":{}");
        json.Should().Contain("\"pending\":0");
        json.Should().Contain("\"running\":0");
    }

    [Fact]
    public void serializes_routing_entries() {
        var snap = MakeSnapshot(routing: new Dictionary<string, string> {
            ["agent-1"] = "proc-1",
            ["agent-2"] = "proc-2"
        });
        var json = HostContextSyncService.SerializeSnapshot(snap);
        json.Should().Contain("\"agent-1\":\"proc-1\"");
        json.Should().Contain("\"agent-2\":\"proc-2\"");
    }

    [Fact]
    public void serializes_build_queue_counts() {
        var snap = MakeSnapshot(pending: 5, running: 1);
        var json = HostContextSyncService.SerializeSnapshot(snap);
        json.Should().Contain("\"pending\":5");
        json.Should().Contain("\"running\":1");
    }

    [Fact]
    public void serialized_json_starts_and_ends_with_braces() {
        var json = HostContextSyncService.SerializeSnapshot(MakeSnapshot());
        json.Should().StartWith("{");
        json.Should().EndWith("}");
    }
}

public class HostContextSyncServiceDeserializeSnapshotTests {
    [Fact]
    public void null_or_whitespace_returns_null() {
        HostContextSyncService.DeserializeSnapshot("").Should().BeNull();
        HostContextSyncService.DeserializeSnapshot("   ").Should().BeNull();
    }

    [Fact]
    public void valid_json_extracts_timestamp_and_hostpid() {
        var ts = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        var snap = new HostContextSnapshot {
            Timestamp = ts,
            HostProcessId = "host-999",
            RoutingTable = new Dictionary<string, string>(),
            PendingMessages = new Dictionary<string, IReadOnlyList<ReadOnlyMemory<byte>>>(),
            BuildQueue = new BuildQueueState {
                PendingCount = 3,
                RunningCount = 1,
                PendingTasks = Array.Empty<string>()
            }
        };
        var json = HostContextSyncService.SerializeSnapshot(snap);
        var restored = HostContextSyncService.DeserializeSnapshot(json);
        restored.Should().NotBeNull();
        restored!.HostProcessId.Should().Be("host-999");
        restored.Timestamp.Should().Be(ts);
    }

    [Fact]
    public void missing_fields_fallback_to_defaults() {
        var json = """{"unknown":"data"}""";
        var restored = HostContextSyncService.DeserializeSnapshot(json);
        restored.Should().NotBeNull();
        restored!.HostProcessId.Should().Be("unknown");
    }

    [Fact]
    public void malformed_json_returns_fallback_snapshot() {
        var restored = HostContextSyncService.DeserializeSnapshot("not json at all");
        restored.Should().NotBeNull();
        restored!.HostProcessId.Should().Be("unknown");
    }

    [Fact]
    public void round_trip_preserves_hostpid() {
        var snap = new HostContextSnapshot {
            Timestamp = DateTimeOffset.UtcNow,
            HostProcessId = "round-trip-host",
            RoutingTable = new Dictionary<string, string> { ["a"] = "b" },
            PendingMessages = new Dictionary<string, IReadOnlyList<ReadOnlyMemory<byte>>>(),
            BuildQueue = new BuildQueueState {
                PendingCount = 7,
                RunningCount = 2,
                PendingTasks = Array.Empty<string>()
            }
        };
        var json = HostContextSyncService.SerializeSnapshot(snap);
        var restored = HostContextSyncService.DeserializeSnapshot(json);
        restored.Should().NotBeNull();
        restored!.HostProcessId.Should().Be("round-trip-host");
    }
}
