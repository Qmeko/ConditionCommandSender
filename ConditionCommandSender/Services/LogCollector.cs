using Dalamud.Game.Chat;
using Dalamud.Plugin.Services;
using ConditionCommandSender.Models;

namespace ConditionCommandSender.Services;

public sealed class LogCollector : IDisposable
{
    private readonly IChatGui chatGui;
    private readonly IClientState clientState;
    private readonly ICondition condition;
    private readonly Configuration configuration;

    private readonly object sync = new();
    private readonly List<LogEntry> entries = new();
    private long sequence;
    private bool started;

    public event Action<LogEntry>? LogReceived;

    public LogCollector(
        IChatGui chatGui,
        IClientState clientState,
        ICondition condition,
        Configuration configuration)
    {
        this.chatGui = chatGui;
        this.clientState = clientState;
        this.condition = condition;
        this.configuration = configuration;
    }

    public void Start()
    {
        if (started)
            return;

        chatGui.ChatMessage += OnChatMessage;
        started = true;
    }

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (sync)
            return entries.ToArray();
    }

    public void Clear()
    {
        lock (sync)
            entries.Clear();
    }

    private void OnChatMessage(
        IHandleableChatMessage message)
    {
        if (!configuration.CollectorEnabled)
            return;

        string sender =
            message.Sender.TextValue.Trim();

        string body =
            message.Message.TextValue.Trim();

        if (sender.Length == 0 && body.Length == 0)
            return;

        string kindName = message.LogKind.ToString();
        ushort kindId = (ushort)message.LogKind;
        bool inCombat =
            condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat];

        var entry = new LogEntry(
            Interlocked.Increment(ref sequence),
            DateTime.Now,
            Classify(kindName, inCombat),
            kindName,
            kindId,
            sender,
            body,
            clientState.TerritoryType,
            inCombat);

        lock (sync)
        {
            entries.Add(entry);

            int maximum =
                Math.Clamp(
                    configuration.MaximumLogEntries,
                    100,
                    10000);

            if (entries.Count > maximum)
                entries.RemoveRange(
                    0,
                    entries.Count - maximum);
        }

        LogReceived?.Invoke(entry);
    }

    private static RuleCategory Classify(
        string logKindName,
        bool inCombat)
    {
        string name = logKindName.ToLowerInvariant();

        if (inCombat
            || name.Contains("damage")
            || name.Contains("healing")
            || name.Contains("miss")
            || name.Contains("buff")
            || name.Contains("debuff")
            || name.Contains("action"))
            return RuleCategory.Battle;

        if (name.Contains("craft")
            || name.Contains("synthesis"))
            return RuleCategory.Crafting;

        if (name.Contains("gather")
            || name.Contains("fishing"))
            return RuleCategory.Gathering;

        if (name.Contains("say")
            || name.Contains("tell")
            || name.Contains("party")
            || name.Contains("alliance")
            || name.Contains("freecompany")
            || name.Contains("linkshell")
            || name.Contains("novicenetwork"))
            return RuleCategory.Chat;

        if (name.Contains("system")
            || name.Contains("error")
            || name.Contains("notice"))
            return RuleCategory.System;

        return RuleCategory.General;
    }

    public void Dispose()
    {
        if (!started)
            return;

        chatGui.ChatMessage -= OnChatMessage;
        started = false;
    }
}
