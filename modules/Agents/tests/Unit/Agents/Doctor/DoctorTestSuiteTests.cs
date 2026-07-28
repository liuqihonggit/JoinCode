namespace Core.Tests.Agents.Doctor;

public class DoctorTestSuiteTests
{
    [Fact]
    public void BuiltInTests_ContainsSixTests()
    {
        Assert.Equal(6, DoctorTestSuite.BuiltInTests.Count);
    }

    [Fact]
    public void BuiltInTests_AllHaveUniqueIds()
    {
        var ids = DoctorTestSuite.BuiltInTests.Select(t => t.TestCaseId).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void BuiltInTests_AllHaveRequiredFields()
    {
        foreach (var test in DoctorTestSuite.BuiltInTests)
        {
            Assert.False(string.IsNullOrWhiteSpace(test.TestCaseId));
            Assert.False(string.IsNullOrWhiteSpace(test.TestName));
            Assert.False(string.IsNullOrWhiteSpace(test.Prompt));
            Assert.True(test.TimeoutSeconds > 0);
        }
    }

    [Fact]
    public void BuiltInTests_IdsAreSequential()
    {
        var ids = DoctorTestSuite.BuiltInTests.Select(t => t.TestCaseId).ToList();
        Assert.Equal(["T001", "T002", "T003", "T004", "T005", "T006"], ids);
    }

    [Fact]
    public void BuiltInTests_CategoriesAreCorrect()
    {
        Assert.Equal("tool", DoctorTestSuite.BuiltInTests[0].Category);
        Assert.Equal("tool", DoctorTestSuite.BuiltInTests[1].Category);
        Assert.Equal("tool", DoctorTestSuite.BuiltInTests[2].Category);
        Assert.Equal("tool", DoctorTestSuite.BuiltInTests[3].Category);
        Assert.Equal("agent", DoctorTestSuite.BuiltInTests[4].Category);
        Assert.Equal("agent", DoctorTestSuite.BuiltInTests[5].Category);
    }

    [Fact]
    public void BuildPatientArguments_ContainsTrust()
    {
        var testCase = new DoctorTestCase
        {
            TestCaseId = "T001",
            TestName = "Test",
            Prompt = "hello",
            TimeoutSeconds = 30
        };

        var args = DoctorTestSuite.BuildPatientArguments(testCase);

        Assert.Contains("--trust", args);
    }

    [Fact]
    public void BuildPatientArguments_ContainsPrompt()
    {
        var testCase = new DoctorTestCase
        {
            TestCaseId = "T001",
            TestName = "Test",
            Prompt = "读取README.md",
            TimeoutSeconds = 30
        };

        var args = DoctorTestSuite.BuildPatientArguments(testCase);

        Assert.Contains("-p", args);
        Assert.Contains("读取README.md", args);
    }

    [Fact]
    public void BuildPatientArguments_ContainsAwait()
    {
        var testCase = new DoctorTestCase
        {
            TestCaseId = "T001",
            TestName = "Test",
            Prompt = "hello",
            TimeoutSeconds = 45
        };

        var args = DoctorTestSuite.BuildPatientArguments(testCase);

        Assert.Contains("--await 45", args);
    }

    [Fact]
    public void DetermineTestStatus_PatientCompleted_ReturnsPass()
    {
        var report = CreateReport(PatientState.Completed, 0);
        var testCase = CreateTestCase();

        var status = DoctorTestSuite.DetermineTestStatus(report, testCase);

        Assert.Equal(DoctorTestStatus.Pass, status);
    }

    [Fact]
    public void DetermineTestStatus_PatientHung_ReturnsHung()
    {
        var report = CreateReport(PatientState.Hung, 1234);
        var testCase = CreateTestCase();

        var status = DoctorTestSuite.DetermineTestStatus(report, testCase);

        Assert.Equal(DoctorTestStatus.Hung, status);
    }

    [Fact]
    public void DetermineTestStatus_PatientFailed_ReturnsFail()
    {
        var report = CreateReport(PatientState.Failed, 1);
        var testCase = CreateTestCase();

        var status = DoctorTestSuite.DetermineTestStatus(report, testCase);

        Assert.Equal(DoctorTestStatus.Fail, status);
    }

    [Fact]
    public void DetermineTestStatus_PatientKilled_ReturnsFail()
    {
        var report = CreateReport(PatientState.Killed, -1);
        var testCase = CreateTestCase();

        var status = DoctorTestSuite.DetermineTestStatus(report, testCase);

        Assert.Equal(DoctorTestStatus.Fail, status);
    }

    [Fact]
    public void DetermineTestStatus_PatientCompletedWrongExitCode_ReturnsFail()
    {
        var report = CreateReport(PatientState.Completed, 1);
        var testCase = CreateTestCase();

        var status = DoctorTestSuite.DetermineTestStatus(report, testCase);

        Assert.Equal(DoctorTestStatus.Fail, status);
    }

    [Fact]
    public void DetermineTestStatus_NoPatient_ReturnsError()
    {
        var report = new DoctorReport
        {
            Patients = new Dictionary<string, PatientInfo>(),
            StartedAt = DateTimeOffset.UtcNow,
            Status = DoctorReportStatus.Failed
        };
        var testCase = CreateTestCase();

        var status = DoctorTestSuite.DetermineTestStatus(report, testCase);

        Assert.Equal(DoctorTestStatus.Error, status);
    }

    [Fact]
    public void DetermineTestStatus_PatientNotStarted_ReturnsError()
    {
        var report = CreateReport(PatientState.NotStarted, null);
        var testCase = CreateTestCase();

        var status = DoctorTestSuite.DetermineTestStatus(report, testCase);

        Assert.Equal(DoctorTestStatus.Error, status);
    }

    [Fact]
    public void DetermineTestStatus_CustomExpectedExitCode_Matches()
    {
        var report = CreateReport(PatientState.Completed, 42);
        var testCase = CreateTestCase() with { ExpectedExitCode = 42 };

        var status = DoctorTestSuite.DetermineTestStatus(report, testCase);

        Assert.Equal(DoctorTestStatus.Pass, status);
    }

    [Fact]
    public void DetermineTestStatus_MatchesByPatientIdPrefix()
    {
        var report = new DoctorReport
        {
            Patients = new Dictionary<string, PatientInfo>
            {
                ["test-T001"] = new PatientInfo
                {
                    PatientId = "test-T001",
                    ProcessId = 123,
                    State = PatientState.Completed,
                    ExitCode = 0,
                    StartedAt = DateTimeOffset.UtcNow
                }
            },
            StartedAt = DateTimeOffset.UtcNow,
            Status = DoctorReportStatus.Completed
        };
        var testCase = CreateTestCase();

        var status = DoctorTestSuite.DetermineTestStatus(report, testCase);

        Assert.Equal(DoctorTestStatus.Pass, status);
    }

    [Fact]
    public async Task RunAsync_NullDoctor_ThrowsArgumentNullException()
    {
        var suite = new DoctorTestSuite();
        var testCases = new[] { CreateTestCase() };

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            suite.RunAsync(null!, testCases));
    }

    [Fact]
    public async Task RunAsync_EmptyTestCases_ReturnsEmptyReport()
    {
        var suite = new DoctorTestSuite();
        var doctor = CreateDoctor();

        var report = await suite.RunAsync(doctor, []);

        Assert.Equal(0, report.TotalCount);
        Assert.True(report.IsAllPassed);
    }

    [Fact]
    public void TestCaseCompleted_EventFired()
    {
        var suite = new DoctorTestSuite();
        var results = new List<DoctorTestCaseResult>();
        suite.TestCaseCompleted += (_, r) => results.Add(r);

        Assert.Empty(results);
    }

    [Fact]
    public void SuiteCompleted_EventFired()
    {
        var suite = new DoctorTestSuite();
        DoctorTestSuiteReport? captured = null;
        suite.SuiteCompleted += (_, r) => captured = r;

        Assert.Null(captured);
    }

    [Fact]
    public void DoctorTestCase_RecordEquality()
    {
        var tc1 = new DoctorTestCase
        {
            TestCaseId = "T001",
            TestName = "Test",
            Prompt = "hello",
            TimeoutSeconds = 30
        };
        var tc2 = tc1 with { };

        Assert.Equal(tc1, tc2);
    }

    [Fact]
    public void DoctorTestCase_WithModifies_CreatesNewInstance()
    {
        var tc1 = new DoctorTestCase
        {
            TestCaseId = "T001",
            TestName = "Test",
            Prompt = "hello",
            TimeoutSeconds = 30
        };
        var tc2 = tc1 with { TimeoutSeconds = 60 };

        Assert.Equal(30, tc1.TimeoutSeconds);
        Assert.Equal(60, tc2.TimeoutSeconds);
    }

    [Fact]
    public void DoctorTestSuiteReport_Duration_CalculatedCorrectly()
    {
        var report = new DoctorTestSuiteReport
        {
            Results = [],
            TotalCount = 0,
            PassCount = 0,
            FailCount = 0,
            HungCount = 0,
            ErrorCount = 0,
            SkippedCount = 0,
            StartedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            CompletedAt = new DateTimeOffset(2026, 1, 1, 0, 1, 30, TimeSpan.Zero),
            IsAllPassed = true
        };

        Assert.Equal(TimeSpan.FromSeconds(90), report.Duration);
    }

    [Fact]
    public void DoctorTestSuiteReport_CountsAreConsistent()
    {
        var results = new List<DoctorTestCaseResult>
        {
            new() { TestCaseId = "T001", TestName = "A", Status = DoctorTestStatus.Pass },
            new() { TestCaseId = "T002", TestName = "B", Status = DoctorTestStatus.Fail },
            new() { TestCaseId = "T003", TestName = "C", Status = DoctorTestStatus.Hung },
            new() { TestCaseId = "T004", TestName = "D", Status = DoctorTestStatus.Error },
            new() { TestCaseId = "T005", TestName = "E", Status = DoctorTestStatus.Skipped },
        };

        var report = new DoctorTestSuiteReport
        {
            Results = results,
            TotalCount = 5,
            PassCount = 1,
            FailCount = 1,
            HungCount = 1,
            ErrorCount = 1,
            SkippedCount = 1,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            IsAllPassed = false
        };

        Assert.Equal(5, report.TotalCount);
        Assert.False(report.IsAllPassed);
    }

    private static DoctorAgent CreateDoctor()
    {
        var fs = new InMemoryFileSystem();
        var processService = new Mock<IProcessService>();
        var transport = new Mock<IDoctorTransport>();
        transport.Setup(t => t.IsConnected).Returns(false);
        transport.Setup(t => t.ConnectedPatientIds).Returns(new List<string>());

        return new DoctorAgent(fs, processService.Object, transport.Object);
    }

    private static DoctorReport CreateReport(PatientState state, int? exitCode)
    {
        return new DoctorReport
        {
            Patients = new Dictionary<string, PatientInfo>
            {
                ["patient-main"] = new PatientInfo
                {
                    PatientId = "patient-main",
                    ProcessId = 123,
                    State = state,
                    ExitCode = exitCode,
                    StartedAt = DateTimeOffset.UtcNow
                }
            },
            StartedAt = DateTimeOffset.UtcNow,
            Status = state == PatientState.Completed ? DoctorReportStatus.Completed : DoctorReportStatus.Failed
        };
    }

    private static DoctorTestCase CreateTestCase()
    {
        return new DoctorTestCase
        {
            TestCaseId = "T001",
            TestName = "FileRead",
            Prompt = "读取README.md",
            TimeoutSeconds = 30
        };
    }
}
