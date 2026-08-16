namespace ConditionCommandSender.Models;

public sealed record LogEntry(
    long Sequence,
    DateTime Timestamp,
    RuleCategory Category,
    string LogKindName,
    ushort LogKindId,
    string Sender,
    string Message,
    uint TerritoryId,
    bool InCombat)
{
    public int MatchedRuleCount { get; set; }

    public string CombinedText
        => string.IsNullOrWhiteSpace(Sender)
            ? Message
            : $"{Sender}: {Message}";
}
