namespace JoinCode.Abstractions.Security.Shell;

[Register]
public sealed class CommandClassifier : ICommandClassifier
{
    private readonly IPathValidator _pathValidator;
    private readonly IDestructiveCommandDetector _destructiveDetector;
    private readonly IReadOnlyCommandDetector _readOnlyDetector;

    public CommandClassifier(
        IPathValidator pathValidator,
        IDestructiveCommandDetector destructiveDetector,
        IReadOnlyCommandDetector readOnlyDetector)
    {
        _pathValidator = pathValidator;
        _destructiveDetector = destructiveDetector;
        _readOnlyDetector = readOnlyDetector;
    }

    public CommandClassification Classify(ShellCommand command, string workingDirectory)
    {
        var risks = new List<CommandRisk>();

        if (_readOnlyDetector.IsReadOnly(command))
        {
            return new CommandClassification(CommandCategory.ReadOnly, risks);
        }

        var destructiveCheck = _destructiveDetector.Detect(command);
        if (destructiveCheck.IsDestructive)
        {
            risks.AddRange(destructiveCheck.Risks);

            var pathValidation = _pathValidator.ValidatePaths(command, workingDirectory);
            if (!pathValidation.IsValid)
            {
                risks.Add(CommandRisk.PathEscape);
                return new CommandClassification(
                    CommandCategory.PathViolation,
                    risks,
                    $"{destructiveCheck.Details}\nPath violation: {pathValidation.Message}");
            }

            if (destructiveCheck.Risks.All(IsWorkspaceSafeRisk))
            {
                return new CommandClassification(CommandCategory.ReadOnly, risks);
            }

            return new CommandClassification(
                CommandCategory.Destructive,
                risks,
                destructiveCheck.Details);
        }

        var pathValidationOnly = _pathValidator.ValidatePaths(command, workingDirectory);
        if (!pathValidationOnly.IsValid)
        {
            risks.Add(CommandRisk.PathEscape);
            return new CommandClassification(
                CommandCategory.PathViolation,
                risks,
                pathValidationOnly.Message);
        }

        return new CommandClassification(CommandCategory.Unknown, risks);
    }

    /// <summary>
    /// 判断风险类型是否在工作目录内可安全放行
    /// DataModification（mv/cp/move/copy 等）在工作目录内是安全的日常操作
    /// </summary>
    private static bool IsWorkspaceSafeRisk(CommandRisk risk)
    {
        return risk is CommandRisk.DataModification;
    }
}
