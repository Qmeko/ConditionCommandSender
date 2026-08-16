using Dalamud.Plugin.Services;
using ConditionCommandSender.Models;

namespace ConditionCommandSender.Services;

public sealed class RuleEngine : IDisposable
{
    private readonly LogCollector collector;
    private readonly FlowEngine flowEngine;
    private readonly Configuration configuration;
    private readonly IPluginLog pluginLog;

    private readonly Dictionary<Guid, DateTime> lastExecuted = new();
    private readonly Dictionary<Guid, Dictionary<Guid, DateTime>> conditionMatches = new();
    private readonly HashSet<Guid> runningRules = new();
    private bool started;

    public RuleEngine(
        LogCollector collector,
        FlowEngine flowEngine,
        Configuration configuration,
        IPluginLog pluginLog)
    {
        this.collector = collector;
        this.flowEngine = flowEngine;
        this.configuration = configuration;
        this.pluginLog = pluginLog;
    }

    public void Start()
    {
        if (started)
            return;

        collector.LogReceived += OnLogReceived;
        started = true;
    }

    public bool Evaluate(
        RuleDefinition rule,
        LogEntry entry)
    {
        ConditionDefinition[] conditions = rule.Conditions
            .Where(condition => !string.IsNullOrWhiteSpace(condition.TextValue))
            .ToArray();

        if (conditions.Length == 0)
            return false;

        bool Contains(ConditionDefinition condition)
            => entry.CombinedText.Contains(
                condition.TextValue.Trim(),
                StringComparison.OrdinalIgnoreCase);

        // OR is evaluated against the current log only.
        if (rule.JoinMode == ConditionJoinMode.Any)
            return conditions.Any(Contains);

        double holdSeconds = Math.Max(0, rule.ConditionHoldSeconds);

        // A hold time of zero preserves the old behavior: every condition
        // must exist in the same log entry.
        if (holdSeconds <= 0)
            return conditions.All(Contains);

        DateTime now = DateTime.Now;
        if (!conditionMatches.TryGetValue(rule.Id, out Dictionary<Guid, DateTime>? matches))
        {
            matches = new Dictionary<Guid, DateTime>();
            conditionMatches[rule.Id] = matches;
        }

        foreach (ConditionDefinition condition in conditions)
        {
            if (Contains(condition))
                matches[condition.Id] = now;
        }

        TimeSpan hold = TimeSpan.FromSeconds(holdSeconds);
        foreach (Guid expired in matches
                     .Where(pair => now - pair.Value > hold)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            matches.Remove(expired);
        }

        return conditions.All(condition =>
            matches.TryGetValue(condition.Id, out DateTime matchedAt)
            && now - matchedAt <= hold);
    }

    private void ClearConditionMatches(Guid ruleId)
        => conditionMatches.Remove(ruleId);

    public async Task TestRuleAsync(
        RuleDefinition rule,
        CancellationToken token = default)
    {
        await flowEngine.RunAsync(
            rule,
            null,
            forceDryRun: configuration.DryRun,
            token);
    }

    public async Task<bool> ExecuteManualAsync(
        RuleDefinition rule,
        CancellationToken token = default)
    {
        if (rule.LockWhileRunning
            && runningRules.Contains(rule.Id))
            return false;

        runningRules.Add(rule.Id);

        try
        {
            await flowEngine.RunAsync(
                rule,
                null,
                forceDryRun: configuration.DryRun,
                token);

            return true;
        }
        catch (Exception exception)
        {
            pluginLog.Error(
                exception,
                "Manual rule execution failed: {RuleName}",
                rule.Name);
            return true;
        }
        finally
        {
            runningRules.Remove(rule.Id);
        }
    }

    private void OnLogReceived(
        LogEntry entry)
    {
        if (!configuration.TriggerEvaluationEnabled)
            return;

        var candidates =
            configuration.Rules
                .Where(rule => rule.Enabled)
                .OrderByDescending(rule => rule.Priority)
                .ToArray();

        foreach (RuleDefinition rule in candidates)
        {
            if (!Evaluate(rule, entry))
                continue;

            entry.MatchedRuleCount++;

            if (rule.LockWhileRunning
                && runningRules.Contains(rule.Id))
                continue;

            if (lastExecuted.TryGetValue(
                    rule.Id,
                    out DateTime previous)
                && DateTime.Now - previous
                    < TimeSpan.FromSeconds(
                        Math.Max(0, rule.CooldownSeconds)))
                continue;

            lastExecuted[rule.Id] = DateTime.Now;
            ClearConditionMatches(rule.Id);
            runningRules.Add(rule.Id);

            _ = ExecuteRuleAsync(rule, entry);
        }
    }

    private async Task ExecuteRuleAsync(
        RuleDefinition rule,
        LogEntry entry)
    {
        try
        {
            await flowEngine.RunAsync(
                rule,
                entry,
                forceDryRun: configuration.DryRun,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            pluginLog.Error(
                exception,
                "Rule execution failed: {RuleName}",
                rule.Name);
        }
        finally
        {
            runningRules.Remove(rule.Id);
        }
    }

    public void Dispose()
    {
        if (!started)
            return;

        collector.LogReceived -= OnLogReceived;
        started = false;
    }
}
