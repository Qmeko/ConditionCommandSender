using Dalamud.Configuration;
using Dalamud.Plugin;
using ConditionCommandSender.Models;

namespace ConditionCommandSender;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool CollectorEnabled { get; set; } = true;
    public bool TriggerEvaluationEnabled { get; set; } = true;
    // 旧設定との互換性のため残すが、実行判定には使用しない。
    public bool FlowExecutionEnabled { get; set; } = true;
    public bool DryRun { get; set; } = false;

    public int MaximumLogEntries { get; set; } = 1000;
    public int VisibleLogRows { get; set; } = 5;
    public float MainSplitRatio { get; set; } = 0.33333334f;
    public float RuleEditorSplitRatio { get; set; } = 0.32f;

    /// <summary>
    /// UI language: "client" follows Dalamud, "en" or "ja" forces that language.
    /// </summary>
    public string UiLanguage { get; set; } = I18n.FollowClient;

    public Dictionary<RuleCategory, bool> CategoryEnabled { get; set; }
        = Enum.GetValues<RuleCategory>()
            .ToDictionary(x => x, _ => true);

    public List<RuleDefinition> Rules { get; set; } = new();
    public List<LuaScriptDefinition> LuaScripts { get; set; } = new();

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(
        IDalamudPluginInterface interfaceInstance)
    {
        pluginInterface = interfaceInstance;

        foreach (RuleCategory category
                 in Enum.GetValues<RuleCategory>())
        {
            CategoryEnabled.TryAdd(category, true);
        }
        // v0.1.9 migration: trigger every enabled rule from the unified log.
        // Keep only Wait, Command and Lua flow steps.
        foreach (RuleDefinition rule in Rules)
        {
            rule.Tags ??= new List<string>();
            rule.ConditionHoldSeconds = Math.Max(0, rule.ConditionHoldSeconds);
            rule.Conditions ??= new List<ConditionDefinition>();
            foreach (ConditionDefinition condition in rule.Conditions)
            {
                // v0.1.9.3: every condition is unified-log text containment only.
                condition.Enabled = true;
                condition.Negate = false;
                condition.Field = ConditionField.Log;
                condition.Operator = ConditionOperator.Contains;
            }
            rule.FlowSteps ??= new List<FlowStep>();
            rule.FlowSteps.RemoveAll(step => !Enum.IsDefined(typeof(FlowStepType), step.Type));
            foreach (FlowStep step in rule.FlowSteps)
            {
                if (step.Type is not (FlowStepType.Wait or FlowStepType.Command or FlowStepType.Lua))
                    step.Type = FlowStepType.Wait;
            }
        }

        // v0.1.9.2 migration: DryRun had no Settings UI before this version,
        // so existing installs are migrated to actual execution by default.
        if (Version < 5)
            DryRun = false;

        // v0.1.9.4: FlowExecutionEnabledによる強制ドライランを廃止。
        // 旧設定がfalseでも、DryRunがOFFなら実際にフローを実行する。
        FlowExecutionEnabled = true;

        foreach (LuaScriptDefinition lua in LuaScripts)
            lua.FilePath = NormalizeStoredLuaPath(lua.FilePath);

        MainSplitRatio = Math.Clamp(MainSplitRatio, 0.15f, 0.80f);
        RuleEditorSplitRatio = Math.Clamp(RuleEditorSplitRatio, 0.15f, 0.80f);

        Version = 10;
    }

    private static string NormalizeStoredLuaPath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return string.Empty;

        string path = Environment.ExpandEnvironmentVariables(rawPath.Trim());
        while (path.Length >= 2)
        {
            char first = path[0];
            char last = path[^1];
            bool quoted = (first == '"' && last == '"')
                          || (first == '\'' && last == '\'')
                          || (first == '“' && last == '”')
                          || (first == '「' && last == '」');
            if (!quoted)
                break;
            path = path[1..^1].Trim();
        }
        return path;
    }

    public void Save()
        => pluginInterface?.SavePluginConfig(this);

    public static Configuration CreateDefault()
    {
        var config = new Configuration();
        config.Rules.Add(new RuleDefinition
        {
            Name = "サンプルルール",
            Enabled = false,
            Tags = ["サンプル"],
            Conditions =
            [
                new ConditionDefinition
                {
                    Field = ConditionField.Log,
                    Operator = ConditionOperator.Contains,
                    TextValue = "直接入力してください"
                }
            ],
            FlowSteps =
            [
                new FlowStep
                {
                    Type = FlowStepType.Command,
                    Text = "/echo CCS sample"
                }
            ]
        });
        return config;
    }

}
