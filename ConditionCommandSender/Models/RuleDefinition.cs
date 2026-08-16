namespace ConditionCommandSender.Models;

[Serializable]
public sealed class RuleDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "新規ルール";
    public RuleCategory Category { get; set; } = RuleCategory.General; // legacy metadata; no longer used for triggering
    public List<string> Tags { get; set; } = new();
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 0;
    public double CooldownSeconds { get; set; } = 1.0;
    public bool LockWhileRunning { get; set; } = true;
    public ConditionJoinMode JoinMode { get; set; } = ConditionJoinMode.All;
    public double ConditionHoldSeconds { get; set; } = 5.0;
    public List<ConditionDefinition> Conditions { get; set; } = new();
    public List<FlowStep> FlowSteps { get; set; } = new();
}

[Serializable]
public sealed class ConditionDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool Enabled { get; set; } = true;
    public bool Negate { get; set; }
    public ConditionField Field { get; set; } = ConditionField.Log;
    public ConditionOperator Operator { get; set; } = ConditionOperator.Contains;
    public string TextValue { get; set; } = string.Empty;
    public double NumberValue { get; set; }
    public bool BoolValue { get; set; }
}

[Serializable]
public sealed class FlowStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool Enabled { get; set; } = true;
    public FlowStepType Type { get; set; } = FlowStepType.Wait;
    public string Text { get; set; } = string.Empty;
    public double NumberValue { get; set; }
    public int RetryCount { get; set; }
    public double RetryDelaySeconds { get; set; } = 1.0;
    public Guid LuaScriptId { get; set; } = Guid.Empty;
}

[Serializable]
public sealed class LuaScriptDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "新規Lua";
    public string FilePath { get; set; } = string.Empty;
}
