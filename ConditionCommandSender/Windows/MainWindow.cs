using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using ConditionCommandSender.Models;
using ConditionCommandSender.Services;

namespace ConditionCommandSender.Windows;

public sealed class MainWindow : Window
{
    private readonly Configuration configuration;
    private readonly LogCollector logCollector;
    private readonly RuleEngine ruleEngine;
    private readonly FlowEngine flowEngine;

    private string selectedPage = "01";
    private Guid selectedRuleId = Guid.Empty;

    private string logSearch = string.Empty;
    private RuleCategory? categoryFilter; // legacy views only
    private string ruleSearch = string.Empty;
    private string selectedTagFilter = string.Empty;
    private string newTagText = string.Empty;
    private LogSortColumn sortColumn = LogSortColumn.Timestamp;
    private SortDirection sortDirection = SortDirection.Ascending;

    private string newLuaName = string.Empty;
    private string newLuaFilePath = string.Empty;
    private bool mainSplitterWasActive;
    private bool ruleSplitterWasActive;
    private int selectedLogTab;

    public MainWindow(
        Configuration configuration,
        LogCollector logCollector,
        RuleEngine ruleEngine,
        FlowEngine flowEngine)
        : base(
            "ConditionCommandSender v0.1.10.4###CCSMain",
            ImGuiWindowFlags.NoScrollbar)
    {
        this.configuration = configuration;
        this.logCollector = logCollector;
        this.ruleEngine = ruleEngine;
        this.flowEngine = flowEngine;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(920, 620),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        I18n.Reloaded += RefreshWindowTitle;
        RefreshWindowTitle();
    }

    private void RefreshWindowTitle()
        => WindowName = I18n.Format("window.main.title", "0.1.10.4") + "###CCSMain";

    public override void Draw()
    {
        DrawEmergencyBar();
        DrawMainWorkspace();
    }

    private void DrawMainWorkspace()
    {
        Vector2 available = ImGui.GetContentRegionAvail();
        const float splitterWidth = 7f;
        const float minimumLogWidth = 250f;
        const float minimumRuleWidth = 400f;

        float usableWidth = Math.Max(1f, available.X - splitterWidth);
        float minimumRatio = Math.Min(0.49f, minimumLogWidth / usableWidth);
        float maximumRatio = Math.Max(0.51f, 1f - (minimumRuleWidth / usableWidth));

        if (minimumRatio > maximumRatio)
        {
            minimumRatio = 0.25f;
            maximumRatio = 0.75f;
        }

        configuration.MainSplitRatio = Math.Clamp(
            configuration.MainSplitRatio,
            minimumRatio,
            maximumRatio);

        float logWidth = usableWidth * configuration.MainSplitRatio;

        ImGui.BeginChild(
            "##MainLogPane",
            new Vector2(logWidth, available.Y),
            true);
        DrawLogPane();
        ImGui.EndChild();

        ImGui.SameLine(0, 0);
        ImGui.InvisibleButton(
            "##MainVerticalSplitter",
            new Vector2(splitterWidth, available.Y));

        bool splitterActive = ImGui.IsItemActive();
        if (splitterActive)
        {
            float delta = ImGui.GetIO().MouseDelta.X;
            if (Math.Abs(delta) > float.Epsilon)
            {
                configuration.MainSplitRatio = Math.Clamp(
                    configuration.MainSplitRatio + (delta / usableWidth),
                    minimumRatio,
                    maximumRatio);
            }
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            configuration.MainSplitRatio = 0.33333334f;
            configuration.Save();
        }

        if (mainSplitterWasActive && !splitterActive)
            configuration.Save();

        mainSplitterWasActive = splitterActive;

        ImGui.SameLine(0, 0);
        ImGui.BeginChild(
            "##MainRulePane",
            new Vector2(0, available.Y),
            true);
        DrawRuleWorkspace();
        ImGui.EndChild();
    }

    private void DrawLogPane()
    {
        if (ImGui.BeginTabBar("##LogTabs"))
        {
            if (ImGui.BeginTabItem(I18n.Get("log.tab.all") + "###LogTabAll"))
            {
                selectedLogTab = 0;
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem(I18n.Get("log.tab.battle") + "###LogTabBattle"))
            {
                selectedLogTab = 1;
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        if (configuration.CollectorEnabled)
        {
            if (ImGui.Button(I18n.Get("log.stop")))
            {
                configuration.CollectorEnabled = false;
                configuration.Save();
            }
        }
        else
        {
            if (ImGui.Button(I18n.Get("log.start")))
            {
                configuration.CollectorEnabled = true;
                configuration.Save();
            }
        }

        ImGui.SameLine();
        ImGui.TextDisabled(
            configuration.CollectorEnabled
                ? I18n.Get("log.capturing")
                : I18n.Get("log.stopped"));

        ImGui.SetNextItemWidth(-80);
        ImGui.InputText("##LogSearch", ref logSearch, 500);
        ImGui.SameLine();
        if (ImGui.Button(I18n.Get("log.clear")))
            logCollector.Clear();

        IEnumerable<LogEntry> query = logCollector.Snapshot();
        if (selectedLogTab == 1)
            query = query.Where(x => x.Category == RuleCategory.Battle);

        if (!string.IsNullOrWhiteSpace(logSearch))
        {
            query = query.Where(x =>
                x.CombinedText.Contains(logSearch, StringComparison.OrdinalIgnoreCase)
                || x.LogKindName.Contains(logSearch, StringComparison.OrdinalIgnoreCase));
        }

        query = query.OrderBy(x => x.Timestamp).ThenBy(x => x.Sequence);

        ImGui.BeginChild("##PersistentLogList", new Vector2(0, 0), false,
            ImGuiWindowFlags.AlwaysVerticalScrollbar);

        bool followNewest = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 2f;
        foreach (LogEntry entry in query)
        {
            ImGui.PushID((int)(entry.Sequence & int.MaxValue));
            ImGui.TextWrapped($"[{entry.Timestamp:HH:mm:ss}] [{entry.LogKindName}/{entry.LogKindId}] {entry.CombinedText}");
            if (ImGui.SmallButton(I18n.Get("log.apply_message")))
                ApplyLogToSelectedRule(entry.Message);
            ImGui.SameLine();
            if (ImGui.SmallButton(I18n.Get("log.apply_combined")))
                ApplyLogToSelectedRule(entry.CombinedText);
            ImGui.Separator();
            ImGui.PopID();
        }

        if (followNewest)
            ImGui.SetScrollHereY(1f);
        ImGui.EndChild();
    }

    private void DrawRuleWorkspace()
    {
        if (!ImGui.BeginTabBar("##RuleWorkspaceTabs"))
            return;

        if (ImGui.BeginTabItem(I18n.Get("tab.rules") + "###TabRules"))
        {
            DrawUnifiedRulesPage();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem(I18n.Get("tab.history") + "###TabHistory"))
        {
            DrawExecutionHistory();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem(I18n.Get("tab.settings") + "###TabSettings"))
        {
            DrawSettings();
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }

    private void DrawUnifiedRulesPage()
    {
        ImGui.SetNextItemWidth(230);
        ImGui.InputText(I18n.Get("rules.search"), ref ruleSearch, 300);
        ImGui.SameLine();

        string[] tags = configuration.Rules
            .SelectMany(r => r.Tags ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string tagPreview = string.IsNullOrWhiteSpace(selectedTagFilter)
            ? I18n.Get("rules.all_tags")
            : selectedTagFilter;
        ImGui.SetNextItemWidth(180);
        if (ImGui.BeginCombo(I18n.Get("rules.filter_tag"), tagPreview))
        {
            if (ImGui.Selectable(I18n.Get("rules.all_tags"), string.IsNullOrWhiteSpace(selectedTagFilter)))
                selectedTagFilter = string.Empty;
            foreach (string tag in tags)
            {
                if (ImGui.Selectable(tag, selectedTagFilter.Equals(tag, StringComparison.OrdinalIgnoreCase)))
                    selectedTagFilter = tag;
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (ImGui.Button(I18n.Get("rules.add")))
        {
            var rule = new RuleDefinition
            {
                Name = I18n.Get("rules.new_name"),
                Conditions = [new ConditionDefinition()],
                FlowSteps = [new FlowStep { Type = FlowStepType.Wait, NumberValue = 1.0 }]
            };
            configuration.Rules.Add(rule);
            selectedRuleId = rule.Id;
        }
        ImGui.Separator();

        Vector2 available = ImGui.GetContentRegionAvail();
        const float splitterWidth = 7f;
        const float minimumListWidth = 180f;
        const float minimumEditorWidth = 320f;
        float usableWidth = Math.Max(1f, available.X - splitterWidth);
        float minimumRatio = Math.Min(0.49f, minimumListWidth / usableWidth);
        float maximumRatio = Math.Max(0.51f, 1f - (minimumEditorWidth / usableWidth));
        if (minimumRatio > maximumRatio)
        {
            minimumRatio = 0.25f;
            maximumRatio = 0.75f;
        }

        configuration.RuleEditorSplitRatio = Math.Clamp(
            configuration.RuleEditorSplitRatio, minimumRatio, maximumRatio);
        float listWidth = usableWidth * configuration.RuleEditorSplitRatio;

        ImGui.BeginChild("##UnifiedRuleList", new Vector2(listWidth, available.Y), true);
        IEnumerable<RuleDefinition> filtered = configuration.Rules;
        if (!string.IsNullOrWhiteSpace(ruleSearch))
            filtered = filtered.Where(r => r.Name.Contains(ruleSearch, StringComparison.OrdinalIgnoreCase)
                || (r.Tags ?? []).Any(t => t.Contains(ruleSearch, StringComparison.OrdinalIgnoreCase)));
        if (!string.IsNullOrWhiteSpace(selectedTagFilter))
            filtered = filtered.Where(r => (r.Tags ?? []).Any(t => t.Equals(selectedTagFilter, StringComparison.OrdinalIgnoreCase)));

        foreach (RuleDefinition rule in filtered.OrderByDescending(r => r.Priority).ThenBy(r => r.Name))
        {
            ImGui.PushID(rule.Id.ToString());
            string tagsText = rule.Tags.Count == 0 ? string.Empty : " [" + string.Join(", ", rule.Tags) + "]";
            if (ImGui.Selectable($"{(rule.Enabled ? "ON" : "OFF")} | {rule.Name}{tagsText}", selectedRuleId == rule.Id))
                selectedRuleId = rule.Id;
            ImGui.PopID();
        }
        ImGui.EndChild();

        ImGui.SameLine(0, 0);
        ImGui.InvisibleButton("##RuleEditorVerticalSplitter", new Vector2(splitterWidth, available.Y));
        bool ruleSplitterActive = ImGui.IsItemActive();
        if (ruleSplitterActive)
        {
            float delta = ImGui.GetIO().MouseDelta.X;
            if (Math.Abs(delta) > float.Epsilon)
            {
                configuration.RuleEditorSplitRatio = Math.Clamp(
                    configuration.RuleEditorSplitRatio + (delta / usableWidth),
                    minimumRatio,
                    maximumRatio);
            }
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            configuration.RuleEditorSplitRatio = 0.32f;
            configuration.Save();
        }

        if (ruleSplitterWasActive && !ruleSplitterActive)
            configuration.Save();
        ruleSplitterWasActive = ruleSplitterActive;

        ImGui.SameLine(0, 0);
        ImGui.BeginChild("##UnifiedRuleEditor", new Vector2(0, available.Y), true);
        RuleDefinition? selected = configuration.Rules.FirstOrDefault(r => r.Id == selectedRuleId);
        if (selected == null)
            ImGui.TextWrapped(I18n.Get("rules.select_hint"));
        else
            DrawRuleEditor(selected, "04");
        ImGui.EndChild();
    }

    private void DrawEmergencyBar()
    {
        if (ImGui.Button(I18n.Get("emergency.alloff")))
        {
            foreach (RuleDefinition rule in configuration.Rules)
                rule.Enabled = false;
            configuration.Save();
        }

        ImGui.SameLine();

        if (ImGui.Button(I18n.Get("emergency.allon")))
        {
            foreach (RuleDefinition rule in configuration.Rules)
                rule.Enabled = true;
            configuration.Save();
        }

        ImGui.Separator();
    }

    private void DrawNavigation()
    {
        DrawNav("[01] Dashboard", "01");
        DrawNav("[02] Log History", "02");
        DrawNav("[03] Battle", "03");
        DrawNav("[04] General", "04");
        DrawNav("[05] Crafting", "05");
        DrawNav("[06] Gathering", "06");
        DrawNav("[07] Chat", "07");
        DrawNav("[09] Execution History", "09");
        DrawNav("[10] Settings", "10");
    }

    private void DrawNav(
        string label,
        string page)
    {
        if (ImGui.Selectable(
                label,
                selectedPage == page))
            selectedPage = page;
    }

    private void DrawDashboard()
    {
        ImGui.TextUnformatted("[01] Dashboard");
        ImGui.Separator();

        bool collectorEnabled = configuration.CollectorEnabled;
        if (ImGui.Checkbox(
                I18n.Get("legacy.collector"),
                ref collectorEnabled))
            configuration.CollectorEnabled = collectorEnabled;

        bool triggerEvaluationEnabled = configuration.TriggerEvaluationEnabled;
        if (ImGui.Checkbox(
                I18n.Get("legacy.trigger"),
                ref triggerEvaluationEnabled))
            configuration.TriggerEvaluationEnabled = triggerEvaluationEnabled;

        bool dryRun = configuration.DryRun;
        if (ImGui.Checkbox(
                I18n.Get("legacy.dry_run"),
                ref dryRun))
            configuration.DryRun = dryRun;

        ImGui.Spacing();

        ImGui.TextUnformatted(
            I18n.Format("legacy.log_count", logCollector.Snapshot().Count));

        ImGui.TextUnformatted(
            I18n.Format("legacy.rule_count", configuration.Rules.Count));

        ImGui.TextWrapped(I18n.Get("legacy.dry_run_hint"));
    }

    private void DrawLogHistory()
    {
        ImGui.TextUnformatted("[02] Log History");
        ImGui.Separator();

        ImGui.SetNextItemWidth(300);
        ImGui.InputText(
            I18n.Get("legacy.search"),
            ref logSearch,
            500);

        ImGui.SameLine();

        if (ImGui.Button(I18n.Get("legacy.clear")))
            logCollector.Clear();

        DrawCategoryFilter();
        DrawSortControls();

        IReadOnlyList<LogEntry> source =
            logCollector.Snapshot();

        IEnumerable<LogEntry> query = source;

        if (categoryFilter.HasValue)
            query = query.Where(
                x => x.Category == categoryFilter.Value);

        if (!string.IsNullOrWhiteSpace(logSearch))
            query = query.Where(
                x => x.CombinedText.Contains(
                    logSearch,
                    StringComparison.OrdinalIgnoreCase)
                    || x.LogKindName.Contains(
                        logSearch,
                        StringComparison.OrdinalIgnoreCase));

        query = ApplySort(query);

        float rowHeight =
            ImGui.GetTextLineHeightWithSpacing() * 3.5f;

        float fixedHeight =
            Math.Max(
                1,
                configuration.VisibleLogRows)
            * rowHeight;

        ImGui.BeginChild(
            I18n.Get("legacy.log_list"),
            new Vector2(0, fixedHeight),
            true,
            ImGuiWindowFlags.AlwaysVerticalScrollbar);

        foreach (LogEntry entry in query)
        {
            ImGui.PushID((int)(entry.Sequence & int.MaxValue));

            ImGui.TextWrapped(
                $"[{entry.Timestamp:HH:mm:ss}] "
                + $"[{entry.Category}] "
                + $"[{entry.LogKindName}/{entry.LogKindId}] "
                + entry.CombinedText);

            if (ImGui.SmallButton(
                    I18n.Get("legacy.apply_message")))
                ApplyLogToSelectedRule(entry.Message);

            ImGui.SameLine();

            if (ImGui.SmallButton(
                    I18n.Get("legacy.apply_combined")))
                ApplyLogToSelectedRule(entry.CombinedText);

            ImGui.Separator();
            ImGui.PopID();
        }

        ImGui.EndChild();
    }

    private void DrawCategoryFilter()
    {
        string preview =
            categoryFilter?.ToString() ?? I18n.Get("legacy.all");

        ImGui.SetNextItemWidth(200);

        if (ImGui.BeginCombo(
                I18n.Get("legacy.category"),
                preview))
        {
            if (ImGui.Selectable(
                    I18n.Get("legacy.all"),
                    !categoryFilter.HasValue))
                categoryFilter = null;

            foreach (RuleCategory category
                     in Enum.GetValues<RuleCategory>())
            {
                if (ImGui.Selectable(
                        category.ToString(),
                        categoryFilter == category))
                    categoryFilter = category;
            }

            ImGui.EndCombo();
        }
    }

    private void DrawSortControls()
    {
        ImGui.SetNextItemWidth(200);

        if (ImGui.BeginCombo(
                I18n.Get("legacy.sort"),
                sortColumn.ToString()))
        {
            foreach (LogSortColumn value
                     in Enum.GetValues<LogSortColumn>())
            {
                if (ImGui.Selectable(
                        value.ToString(),
                        sortColumn == value))
                    sortColumn = value;
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();

        if (ImGui.Button(
                sortDirection == SortDirection.Ascending
                    ? I18n.Get("legacy.asc")
                    : I18n.Get("legacy.desc")))
        {
            sortDirection =
                sortDirection == SortDirection.Ascending
                    ? SortDirection.Descending
                    : SortDirection.Ascending;
        }
    }

    private IEnumerable<LogEntry> ApplySort(
        IEnumerable<LogEntry> query)
    {
        Func<LogEntry, object> selector =
            sortColumn switch
            {
                LogSortColumn.Timestamp => x => x.Timestamp,
                LogSortColumn.Category => x => x.Category,
                LogSortColumn.LogKind => x => x.LogKindName,
                LogSortColumn.Sender => x => x.Sender,
                LogSortColumn.Message => x => x.Message,
                LogSortColumn.MatchedRuleCount => x => x.MatchedRuleCount,
                _ => x => x.Timestamp
            };

        return sortDirection == SortDirection.Ascending
            ? query.OrderBy(selector)
            : query.OrderByDescending(selector);
    }

    private void DrawCategoryPage(
        RuleCategory category,
        string title)
    {
        ImGui.TextUnformatted(title);
        ImGui.Separator();

        bool enabled =
            configuration.CategoryEnabled
                .GetValueOrDefault(category, true);

        if (ImGui.Checkbox(
                I18n.Format("legacy.category_toggle", PagePrefix(category)),
                ref enabled))
        {
            configuration.CategoryEnabled[category] =
                enabled;
        }

        ImGui.SameLine();

        if (ImGui.Button(
                I18n.Format("legacy.add_rule", PagePrefix(category))))
        {
            var rule = new RuleDefinition
            {
                Name = I18n.Format("legacy.new_rule_name", category),
                Category = category,
                Conditions =
                [
                    new ConditionDefinition()
                ],
                FlowSteps =
                [
                    new FlowStep()
                ]
            };

            configuration.Rules.Add(rule);
            selectedRuleId = rule.Id;
        }

        ImGui.BeginChild(
            "##RuleList",
            new Vector2(260, 0),
            true);

        DrawRuleList(category);

        ImGui.EndChild();
        ImGui.SameLine();

        ImGui.BeginChild(
            "##RuleEditor",
            new Vector2(0, 0),
            true);

        RuleDefinition? selected =
            configuration.Rules.FirstOrDefault(
                x => x.Id == selectedRuleId
                    && x.Category == category);

        if (selected == null)
        {
            ImGui.TextWrapped(
                I18n.Get("rules.select_hint"));
        }
        else
        {
            DrawRuleEditor(selected, PagePrefix(category));
        }

        ImGui.EndChild();
    }

    private void DrawRuleList(
        RuleCategory category)
    {
        List<RuleDefinition> rules =
            configuration.Rules
                .Where(x => x.Category == category)
                .OrderByDescending(x => x.Priority)
                .ToList();

        foreach (RuleDefinition rule in rules)
        {
            ImGui.PushID(rule.Id.ToString());

            if (ImGui.Selectable(
                    $"{(rule.Enabled ? "ON" : "OFF")} | {rule.Name}",
                    selectedRuleId == rule.Id))
                selectedRuleId = rule.Id;

            ImGui.PopID();
        }
    }

    private void DrawRuleEditor(
        RuleDefinition rule,
        string prefix)
    {
        bool ruleEnabled = rule.Enabled;
        if (ImGui.Checkbox(
                I18n.Format("rule.enabled", prefix),
                ref ruleEnabled))
            rule.Enabled = ruleEnabled;

        ImGui.SetNextItemWidth(-1);
        string ruleName = rule.Name;
        if (ImGui.InputText(
                I18n.Format("rule.name", prefix),
                ref ruleName,
                200))
            rule.Name = ruleName;

        DrawRuleTags(rule, prefix);

        int priority = rule.Priority;
        if (ImGui.InputInt(
                I18n.Format("rule.priority", prefix),
                ref priority))
            rule.Priority = priority;

        float cooldown =
            (float)rule.CooldownSeconds;

        if (ImGui.InputFloat(
                I18n.Format("rule.cooldown", prefix),
                ref cooldown,
                0.1f,
                1f,
                "%.2f"))
            rule.CooldownSeconds =
                Math.Max(0, cooldown);

        bool lockWhileRunning = rule.LockWhileRunning;
        if (ImGui.Checkbox(
                I18n.Format("rule.lock", prefix),
                ref lockWhileRunning))
            rule.LockWhileRunning = lockWhileRunning;

        DrawConditions(rule, prefix);
        DrawFlow(rule, prefix);

        if (ImGui.Button(
                I18n.Format("rule.run", prefix)))
        {
            _ = flowEngine.RunAsync(
                rule,
                null,
                configuration.DryRun,
                CancellationToken.None);
        }

        ImGui.SameLine();

        if (ImGui.Button(
                I18n.Format("rule.duplicate", prefix)))
        {
            var clone =
                new RuleDefinition
                {
                    Name = I18n.Format("rule.copy_suffix", rule.Name),
                    Category = rule.Category,
                    Tags = new List<string>(rule.Tags),
                    Enabled = false,
                    Priority = rule.Priority,
                    CooldownSeconds = rule.CooldownSeconds,
                    LockWhileRunning = rule.LockWhileRunning,
                    JoinMode = rule.JoinMode,
                    Conditions = rule.Conditions
                        .Select(CloneCondition)
                        .ToList(),
                    FlowSteps = rule.FlowSteps
                        .Select(CloneStep)
                        .ToList()
                };

            configuration.Rules.Add(clone);
            selectedRuleId = clone.Id;
        }

        ImGui.SameLine();

        if (ImGui.Button(
                I18n.Format("rule.delete", prefix)))
        {
            configuration.Rules.Remove(rule);
            selectedRuleId = Guid.Empty;
        }
    }

    private void DrawRuleTags(RuleDefinition rule, string prefix)
    {
        rule.Tags ??= new List<string>();
        ImGui.TextUnformatted(I18n.Format("tags.label", prefix));
        int removeTag = -1;
        for (int i = 0; i < rule.Tags.Count; i++)
        {
            ImGui.PushID($"tag-{i}");
            ImGui.TextUnformatted(rule.Tags[i]);
            ImGui.SameLine();
            if (ImGui.SmallButton(I18n.Get("tags.delete")))
                removeTag = i;
            ImGui.SameLine();
            ImGui.PopID();
        }
        if (removeTag >= 0)
            rule.Tags.RemoveAt(removeTag);

        ImGui.SetNextItemWidth(180);
        ImGui.InputText($"##NewTag{rule.Id}", ref newTagText, 100);
        ImGui.SameLine();
        if (ImGui.Button(I18n.Format("tags.add", prefix)))
        {
            string tag = newTagText.Trim();
            if (!string.IsNullOrWhiteSpace(tag)
                && !rule.Tags.Any(x => x.Equals(tag, StringComparison.OrdinalIgnoreCase)))
            {
                rule.Tags.Add(tag);
                newTagText = string.Empty;
            }
        }
    }

    private void DrawConditions(
        RuleDefinition rule,
        string prefix)
    {
        ImGui.Separator();
        ImGui.TextUnformatted(I18n.Format("cond.header", prefix));
        ImGui.Separator();

        if (rule.Conditions.Count > 1)
        {
            string joinPreview =
                rule.JoinMode == ConditionJoinMode.All
                    ? "AND"
                    : "OR";

            ImGui.SetNextItemWidth(120);
            if (ImGui.BeginCombo(
                    I18n.Format("cond.join", prefix),
                    joinPreview))
            {
                if (ImGui.Selectable(
                        "AND",
                        rule.JoinMode == ConditionJoinMode.All))
                    rule.JoinMode = ConditionJoinMode.All;

                if (ImGui.Selectable(
                        "OR",
                        rule.JoinMode == ConditionJoinMode.Any))
                    rule.JoinMode = ConditionJoinMode.Any;

                ImGui.EndCombo();
            }

            if (rule.JoinMode == ConditionJoinMode.All)
            {
                float holdSeconds = (float)Math.Max(0, rule.ConditionHoldSeconds);
                ImGui.SetNextItemWidth(120);
                if (ImGui.InputFloat(
                        I18n.Format("cond.hold", prefix),
                        ref holdSeconds,
                        0.5f,
                        1.0f,
                        I18n.Get("cond.hold_format")))
                {
                    rule.ConditionHoldSeconds = Math.Max(0, holdSeconds);
                }

                ImGui.TextDisabled(I18n.Get("cond.hold_hint"));
            }
        }

        if (ImGui.Button(
                I18n.Format("cond.add", prefix)))
            rule.Conditions.Add(new ConditionDefinition());

        int remove = -1;

        for (int i = 0; i < rule.Conditions.Count; i++)
        {
            ConditionDefinition condition = rule.Conditions[i];
            ImGui.PushID(condition.Id.ToString());

            ImGui.TextUnformatted(I18n.Format("cond.item", i + 1));
            ImGui.SetNextItemWidth(-1);
            string textValue = condition.TextValue;
            if (ImGui.InputText(
                    "##TriggerText",
                    ref textValue,
                    1000))
                condition.TextValue = textValue;

            if (ImGui.SmallButton(I18n.Get("cond.delete")))
                remove = i;

            ImGui.Separator();
            ImGui.PopID();
        }

        if (remove >= 0)
            rule.Conditions.RemoveAt(remove);

        ImGui.TextDisabled(I18n.Get("cond.hint"));
    }


    private void DrawFlow(
        RuleDefinition rule,
        string prefix)
    {
        ImGui.Separator();
        ImGui.TextUnformatted(I18n.Format("flow.header", prefix));
        ImGui.Separator();

        if (ImGui.Button(
                I18n.Format("flow.add", prefix)))
            rule.FlowSteps.Add(new FlowStep());

        int remove = -1;

        for (int i = 0;
             i < rule.FlowSteps.Count;
             i++)
        {
            FlowStep step = rule.FlowSteps[i];

            ImGui.PushID(step.Id.ToString());

            ImGui.TextUnformatted(
                $"{i + 1}.");

            ImGui.SameLine();
            bool stepEnabled = step.Enabled;
            if (ImGui.Checkbox(
                    I18n.Get("flow.on"),
                    ref stepEnabled))
                step.Enabled = stepEnabled;

            ImGui.SameLine();

            if (ImGui.BeginCombo(
                    I18n.Get("flow.type"),
                    step.Type.ToString()))
            {
                foreach (FlowStepType type
                         in Enum.GetValues<FlowStepType>())
                {
                    if (ImGui.Selectable(
                            type.ToString(),
                            step.Type == type))
                        step.Type = type;
                }

                ImGui.EndCombo();
            }

            if (step.Type == FlowStepType.Wait)
            {
                float seconds =
                    (float)step.NumberValue;

                if (ImGui.InputFloat(
                        I18n.Get("flow.seconds"),
                        ref seconds,
                        0.1f,
                        1f,
                        "%.2f"))
                    step.NumberValue =
                        Math.Max(0, seconds);
            }
            else if (step.Type == FlowStepType.Lua)
            {
                LuaScriptDefinition? selectedLua =
                    configuration.LuaScripts.FirstOrDefault(
                        x => x.Id == step.LuaScriptId);

                string preview = selectedLua?.Name ?? I18n.Get("flow.lua_none");

                if (ImGui.BeginCombo("Lua", preview))
                {
                    foreach (LuaScriptDefinition lua
                             in configuration.LuaScripts)
                    {
                        bool selected = lua.Id == step.LuaScriptId;
                        if (ImGui.Selectable(lua.Name, selected))
                            step.LuaScriptId = lua.Id;
                    }

                    ImGui.EndCombo();
                }

                if (configuration.LuaScripts.Count == 0)
                    ImGui.TextDisabled(I18n.Get("flow.lua_hint"));
                else if (selectedLua != null)
                    ImGui.TextDisabled(selectedLua.FilePath);
            }
            else if (step.Type == FlowStepType.Command)
            {
                ImGui.SetNextItemWidth(-1);
                string commandText = step.Text;
                if (ImGui.InputText(
                        "Command",
                        ref commandText,
                        1000))
                    step.Text = commandText;

                ImGui.TextDisabled(I18n.Get("flow.command_hint"));
            }

            if (i > 0)
            {
                if (ImGui.SmallButton("↑"))
                    (rule.FlowSteps[i - 1],
                     rule.FlowSteps[i]) =
                    (rule.FlowSteps[i],
                     rule.FlowSteps[i - 1]);

                ImGui.SameLine();
            }

            if (i < rule.FlowSteps.Count - 1)
            {
                if (ImGui.SmallButton("↓"))
                    (rule.FlowSteps[i + 1],
                     rule.FlowSteps[i]) =
                    (rule.FlowSteps[i],
                     rule.FlowSteps[i + 1]);

                ImGui.SameLine();
            }

            if (ImGui.SmallButton(I18n.Get("cond.delete")))
                remove = i;

            ImGui.Separator();
            ImGui.PopID();
        }

        if (remove >= 0)
            rule.FlowSteps.RemoveAt(remove);
    }

    private void DrawExecutionHistory()
    {
        ImGui.TextUnformatted(I18n.Get("history.title"));
        ImGui.Separator();

        ImGui.BeginChild(
            I18n.Get("history.list"),
            new Vector2(0, 0),
            true,
            ImGuiWindowFlags.AlwaysVerticalScrollbar);

        foreach (ExecutionRecord record
                 in flowEngine.HistorySnapshot())
        {
            ImGui.TextWrapped(
                $"[{record.Timestamp:HH:mm:ss}] "
                + $"[{record.Category}] "
                + $"{record.RuleName} | "
                + $"{record.Status} | "
                + $"{record.ElapsedMilliseconds:F1}ms | "
                + record.Detail);

            ImGui.Separator();
        }

        ImGui.EndChild();
    }

    private static string NormalizeLuaPathForStorage(string rawPath)
    {
        string path = rawPath.Trim();
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
        return Environment.ExpandEnvironmentVariables(path);
    }

    private void DrawSettings()
    {
        ImGui.TextUnformatted(I18n.Get("settings.title"));
        ImGui.Separator();

        DrawLanguageSelector();

        int maximumLogEntries = configuration.MaximumLogEntries;
        if (ImGui.InputInt(
                I18n.Get("settings.log_limit"),
                ref maximumLogEntries))
            configuration.MaximumLogEntries = maximumLogEntries;

        configuration.MaximumLogEntries =
            Math.Clamp(
                configuration.MaximumLogEntries,
                100,
                10000);

        int visibleLogRows = configuration.VisibleLogRows;
        if (ImGui.InputInt(
                I18n.Get("settings.visible_rows"),
                ref visibleLogRows))
            configuration.VisibleLogRows = visibleLogRows;

        configuration.VisibleLogRows =
            Math.Clamp(
                configuration.VisibleLogRows,
                1,
                50);

        ImGui.Separator();

        bool dryRun = configuration.DryRun;
        if (ImGui.Checkbox(
                I18n.Get("settings.dry_run"),
                ref dryRun))
            configuration.DryRun = dryRun;

        ImGui.TextDisabled(I18n.Get("settings.dry_run_hint"));

        ImGui.Separator();
        ImGui.TextUnformatted(I18n.Get("settings.lua_header"));

        ImGui.SetNextItemWidth(260);
        ImGui.InputText(
            I18n.Get("settings.lua_name"),
            ref newLuaName,
            200);

        ImGui.SetNextItemWidth(-1);
        ImGui.InputText(
            I18n.Get("settings.lua_path"),
            ref newLuaFilePath,
            1000);

        ImGui.TextDisabled(I18n.Get("settings.lua_path_hint"));

        if (ImGui.Button(I18n.Get("settings.lua_register"))
            && !string.IsNullOrWhiteSpace(newLuaName)
            && !string.IsNullOrWhiteSpace(newLuaFilePath))
        {
            configuration.LuaScripts.Add(
                new LuaScriptDefinition
                {
                    Name = newLuaName.Trim(),
                    FilePath = NormalizeLuaPathForStorage(newLuaFilePath)
                });

            newLuaName = string.Empty;
            newLuaFilePath = string.Empty;
        }

        int removeLua = -1;
        for (int i = 0; i < configuration.LuaScripts.Count; i++)
        {
            LuaScriptDefinition lua = configuration.LuaScripts[i];
            ImGui.PushID(lua.Id.ToString());

            string luaName = lua.Name;
            ImGui.SetNextItemWidth(260);
            if (ImGui.InputText(I18n.Get("settings.lua_name_edit"), ref luaName, 200))
                lua.Name = luaName;

            string luaFilePath = lua.FilePath;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText(I18n.Get("settings.lua_path_edit"), ref luaFilePath, 1000))
                lua.FilePath = luaFilePath;

            if (ImGui.SmallButton(I18n.Get("cond.delete")))
                removeLua = i;

            ImGui.Separator();
            ImGui.PopID();
        }

        if (removeLua >= 0)
        {
            Guid removedId = configuration.LuaScripts[removeLua].Id;
            configuration.LuaScripts.RemoveAt(removeLua);

            foreach (RuleDefinition rule in configuration.Rules)
            foreach (FlowStep step in rule.FlowSteps)
            {
                if (step.LuaScriptId == removedId)
                    step.LuaScriptId = Guid.Empty;
            }
        }

        if (ImGui.Button(
                I18n.Get("settings.save")))
            configuration.Save();

        ImGui.TextWrapped(I18n.Get("settings.ui_id_hint"));
    }

    private void DrawLanguageSelector()
    {
        string current = string.IsNullOrWhiteSpace(configuration.UiLanguage)
            ? I18n.FollowClient
            : configuration.UiLanguage;

        string preview = current switch
        {
            "en" => I18n.Get("settings.language.en"),
            "ja" => I18n.Get("settings.language.ja"),
            _ => I18n.Get("settings.language.client")
        };

        ImGui.SetNextItemWidth(220);
        if (!ImGui.BeginCombo(I18n.Get("settings.ui_language"), preview))
            return;

        if (ImGui.Selectable(I18n.Get("settings.language.client"), current == I18n.FollowClient))
            SetUiLanguage(I18n.FollowClient);
        if (ImGui.Selectable(I18n.Get("settings.language.en"), current == "en"))
            SetUiLanguage("en");
        if (ImGui.Selectable(I18n.Get("settings.language.ja"), current == "ja"))
            SetUiLanguage("ja");

        ImGui.EndCombo();
    }

    private void SetUiLanguage(string language)
    {
        if (string.Equals(configuration.UiLanguage, language, StringComparison.OrdinalIgnoreCase))
            return;

        configuration.UiLanguage = language;
        configuration.Save();
        I18n.ApplyFromConfig();
    }

    private void ApplyLogToSelectedRule(
        string value)
    {
        RuleDefinition? rule =
            configuration.Rules.FirstOrDefault(
                x => x.Id == selectedRuleId);

        if (rule == null)
            return;

        ConditionDefinition? condition =
            rule.Conditions.FirstOrDefault(
                x => x.Field == ConditionField.Log);

        if (condition == null)
        {
            condition =
                new ConditionDefinition
                {
                    Field = ConditionField.Log
                };

            rule.Conditions.Add(condition);
        }

        condition.TextValue = value;
    }

    private static string PagePrefix(
        RuleCategory category)
        => category switch
        {
            RuleCategory.Battle => "03",
            RuleCategory.General => "04",
            RuleCategory.Crafting => "05",
            RuleCategory.Gathering => "06",
            RuleCategory.Chat => "07",
            RuleCategory.System => "08",
            _ => "11"
        };

    private static ConditionDefinition CloneCondition(
        ConditionDefinition source)
        => new()
        {
            Enabled = source.Enabled,
            Negate = source.Negate,
            Field = source.Field,
            Operator = source.Operator,
            TextValue = source.TextValue,
            NumberValue = source.NumberValue,
            BoolValue = source.BoolValue
        };

    private static FlowStep CloneStep(
        FlowStep source)
        => new()
        {
            Enabled = source.Enabled,
            Type = source.Type,
            Text = source.Text,
            NumberValue = source.NumberValue,
            RetryCount = source.RetryCount,
            RetryDelaySeconds =
                source.RetryDelaySeconds,
            LuaScriptId = source.LuaScriptId
        };
}
