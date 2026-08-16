namespace ConditionCommandSender.Models;

public enum RuleCategory
{
    Battle,
    General,
    Crafting,
    Gathering,
    Chat,
    System,
    Custom
}

public enum ConditionJoinMode
{
    All,
    Any
}

public enum ConditionField
{
    Log,
    Sender,
    LogKindName,
    LogKindId,
    TerritoryId,
    InCombat,
    Category
}

public enum ConditionOperator
{
    Contains,
    Exact,
    StartsWith,
    EndsWith,
    NotContains,
    NotExact,
    EqualsNumber,
    NotEqualsNumber,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual
}

public enum FlowStepType
{
    Wait = 0,
    Command = 2,
    Lua = 7
}

public enum LogSortColumn
{
    Timestamp,
    Category,
    LogKind,
    Sender,
    Message,
    MatchedRuleCount
}

public enum SortDirection
{
    Ascending,
    Descending
}

public enum FlowRunStatus
{
    DryRun,
    Running,
    Completed,
    Failed,
    Cancelled,
    Blocked
}
