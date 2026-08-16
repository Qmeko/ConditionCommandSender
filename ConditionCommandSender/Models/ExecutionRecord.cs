namespace ConditionCommandSender.Models;

public sealed record ExecutionRecord(
    DateTime Timestamp,
    Guid RuleId,
    string RuleName,
    RuleCategory Category,
    FlowRunStatus Status,
    string Detail,
    double ElapsedMilliseconds);
