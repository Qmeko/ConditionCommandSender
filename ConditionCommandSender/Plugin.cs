using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ConditionCommandSender.Models;
using ConditionCommandSender.Services;
using ConditionCommandSender.Windows;

namespace ConditionCommandSender;

public sealed class Plugin : IDalamudPlugin
{
    public const string CommandName = "/ccs";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IChatGui chatGui;
    private readonly IPluginLog pluginLog;

    private readonly WindowSystem windowSystem = new("ConditionCommandSender");
    private readonly CommandInfo commandInfo;
    private readonly Configuration configuration;
    private readonly LogCollector logCollector;
    private readonly FlowEngine flowEngine;
    private readonly RuleEngine ruleEngine;
    private readonly MainWindow mainWindow;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IChatGui chatGui,
        IClientState clientState,
        ICondition condition,
        IFramework framework,
        IGameGui gameGui,
        IObjectTable objectTable,
        IPluginLog pluginLog)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.chatGui = chatGui;
        this.pluginLog = pluginLog;

        configuration =
            pluginInterface.GetPluginConfig() as Configuration
            ?? Configuration.CreateDefault();

        configuration.Initialize(pluginInterface);
        I18n.Init(pluginInterface, () => configuration.UiLanguage);

        logCollector = new LogCollector(
            chatGui,
            clientState,
            condition,
            configuration);

        flowEngine = new FlowEngine(
            framework,
            chatGui,
            pluginLog,
            gameGui,
            objectTable,
            condition,
            configuration);

        ruleEngine = new RuleEngine(
            logCollector,
            flowEngine,
            configuration,
            pluginLog);

        mainWindow = new MainWindow(
            configuration,
            logCollector,
            ruleEngine,
            flowEngine);

        windowSystem.AddWindow(mainWindow);

        commandInfo = new CommandInfo(OnCommand)
        {
            HelpMessage = I18n.Get("cmd.help.short")
        };
        commandManager.AddHandler(CommandName, commandInfo);
        I18n.Reloaded += RefreshCommandHelp;

        pluginInterface.UiBuilder.Draw += DrawUi;
        pluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
        pluginInterface.UiBuilder.OpenMainUi += OpenConfigUi;

        logCollector.Start();
        ruleEngine.Start();

        pluginLog.Information(
            "ConditionCommandSender v0.1.10.4 initialized.");
    }

    public void Dispose()
    {
        ruleEngine.Dispose();
        logCollector.Dispose();
        flowEngine.Dispose();

        pluginInterface.UiBuilder.Draw -= DrawUi;
        pluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
        pluginInterface.UiBuilder.OpenMainUi -= OpenConfigUi;

        I18n.Reloaded -= RefreshCommandHelp;
        commandManager.RemoveHandler(CommandName);
        windowSystem.RemoveAllWindows();
        I18n.Dispose();

        configuration.Save();
    }

    private void RefreshCommandHelp()
        => commandInfo.HelpMessage = I18n.Get("cmd.help.short");

    private void DrawUi()
        => windowSystem.Draw();

    private void OpenConfigUi()
        => mainWindow.IsOpen = true;

    private void OnCommand(
        string command,
        string arguments)
    {
        string arg = arguments.Trim();

        if (string.IsNullOrWhiteSpace(arg))
        {
            mainWindow.Toggle();
            return;
        }

        if (arg.Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            PrintHelp();
            return;
        }

        if (TryReadNamedArgument(arg, "on", out string onName))
        {
            SetRuleEnabled(onName, true);
            return;
        }

        if (TryReadNamedArgument(arg, "off", out string offName))
        {
            SetRuleEnabled(offName, false);
            return;
        }

        if (TryReadNamedArgument(arg, "toggle", out string toggleName))
        {
            ToggleRuleEnabled(toggleName);
            return;
        }

        if (arg.Equals("allon", StringComparison.OrdinalIgnoreCase))
        {
            SetAllRulesEnabled(true);
            return;
        }

        if (arg.Equals("alloff", StringComparison.OrdinalIgnoreCase))
        {
            SetAllRulesEnabled(false);
            return;
        }

        if (arg.Equals("alltoggle", StringComparison.OrdinalIgnoreCase))
        {
            ToggleAllRulesEnabled();
            return;
        }

        if (arg.Equals("log start", StringComparison.OrdinalIgnoreCase))
        {
            SetLogCaptureEnabled(true);
            return;
        }

        if (arg.Equals("log stop", StringComparison.OrdinalIgnoreCase))
        {
            SetLogCaptureEnabled(false);
            return;
        }

        chatGui.Print(I18n.Get("cmd.unknown"));
    }

    private RuleDefinition? FindUniqueRule(string ruleName)
    {
        if (string.IsNullOrWhiteSpace(ruleName))
        {
            chatGui.Print(I18n.Get("cmd.need_name"));
            return null;
        }

        RuleDefinition[] matches = configuration.Rules
            .Where(rule => rule.Name.Equals(ruleName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
        {
            chatGui.Print(I18n.Format("cmd.not_found", ruleName));
            return null;
        }

        if (matches.Length > 1)
        {
            chatGui.Print(I18n.Format("cmd.duplicate_name", ruleName));
            return null;
        }

        return matches[0];
    }

    private void SetRuleEnabled(string ruleName, bool enabled)
    {
        RuleDefinition? rule = FindUniqueRule(ruleName);
        if (rule == null) return;
        rule.Enabled = enabled;
        configuration.Save();
        chatGui.Print(I18n.Format(
            "cmd.rule_set",
            enabled ? I18n.Get("cmd.enabled") : I18n.Get("cmd.disabled"),
            rule.Name));
    }

    private void ToggleRuleEnabled(string ruleName)
    {
        RuleDefinition? rule = FindUniqueRule(ruleName);
        if (rule == null) return;

        rule.Enabled = !rule.Enabled;
        configuration.Save();
        chatGui.Print(I18n.Format(
            "cmd.rule_set",
            rule.Enabled ? I18n.Get("cmd.enabled") : I18n.Get("cmd.disabled"),
            rule.Name));
    }

    private void SetAllRulesEnabled(bool enabled)
    {
        foreach (RuleDefinition rule in configuration.Rules)
            rule.Enabled = enabled;

        configuration.Save();
        chatGui.Print(I18n.Format(
            "cmd.all_set",
            configuration.Rules.Count,
            enabled ? I18n.Get("cmd.enabled") : I18n.Get("cmd.disabled"));
    }

    private void ToggleAllRulesEnabled()
    {
        foreach (RuleDefinition rule in configuration.Rules)
            rule.Enabled = !rule.Enabled;

        configuration.Save();
        chatGui.Print(I18n.Format("cmd.all_toggle", configuration.Rules.Count));
    }


    private void SetLogCaptureEnabled(bool enabled)
    {
        configuration.CollectorEnabled = enabled;
        configuration.Save();
        chatGui.Print(I18n.Format(
            "cmd.log_set",
            enabled ? I18n.Get("cmd.log_resumed") : I18n.Get("cmd.log_stopped")));
    }

    private void PrintOverallStatus()
    {
        string[] running = flowEngine.RunningRuleNames();
        chatGui.Print($"[CCS] 全停止={flowEngine.EmergencyStopped} | ドライラン={configuration.DryRun} | 実行中={running.Length}");
        if (running.Length > 0) chatGui.Print("[CCS] 実行中: " + string.Join(", ", running));
    }

    private void PrintRuleList()
    {
        chatGui.Print($"[CCS] 登録ルール: {configuration.Rules.Count}件");
        foreach (RuleDefinition rule in configuration.Rules)
            chatGui.Print($"[CCS] {(rule.Enabled ? "ON" : "OFF")} | {rule.Name} | {rule.Category}");
    }

    private void PrintHelp()
    {
        chatGui.Print(I18n.Get("cmd.help.1"));
        chatGui.Print(I18n.Get("cmd.help.2"));
        chatGui.Print(I18n.Get("cmd.help.3"));
        chatGui.Print(I18n.Get("cmd.help.4"));
        chatGui.Print(I18n.Get("cmd.help.5"));
        chatGui.Print(I18n.Get("cmd.help.6"));
        chatGui.Print(I18n.Get("cmd.help.7"));
        chatGui.Print(I18n.Get("cmd.help.8"));
        chatGui.Print(I18n.Get("cmd.help.9"));
        chatGui.Print(I18n.Get("cmd.help.10"));
    }

    private static bool TryReadNamedArgument(string arg, string verb, out string value)
    {
        string prefix = verb + " ";
        if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = string.Empty;
            return false;
        }
        value = Unquote(arg[prefix.Length..].Trim());
        return true;
    }

    private async Task ExecuteNamedRuleAsync(
        RuleDefinition rule)
    {
        bool started = await ruleEngine.ExecuteManualAsync(rule);

        chatGui.Print(
            started
                ? $"[CCS] ルールを実行しました: {rule.Name}"
                : $"[CCS] ルールはすでに実行中です: {rule.Name}");
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2
            && ((value[0] == '\"' && value[^1] == '\"')
                || (value[0] == '\'' && value[^1] == '\'')))
            return value[1..^1].Trim();

        return value;
    }
}
