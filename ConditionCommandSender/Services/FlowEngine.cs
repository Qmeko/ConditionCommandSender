using System.Diagnostics;
using System.Globalization;
using System.Text;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Plugin.Services;
using MoonSharp.Interpreter;
using ConditionCommandSender.Models;

namespace ConditionCommandSender.Services;

public sealed class FlowEngine : IDisposable
{
    private readonly IFramework framework;
    private readonly IChatGui chatGui;
    private readonly IPluginLog pluginLog;
    private readonly IGameGui gameGui;
    private readonly IObjectTable objectTable;
    private readonly ICondition condition;
    private readonly Configuration configuration;

    private readonly object sync = new();
    private readonly List<ExecutionRecord> history = new();
    private readonly Dictionary<Guid, CancellationTokenSource> runningRuleTokens = new();
    private CancellationTokenSource emergencyCts = new();
    private bool emergencyStopped;

    public FlowEngine(
        IFramework framework,
        IChatGui chatGui,
        IPluginLog pluginLog,
        IGameGui gameGui,
        IObjectTable objectTable,
        ICondition condition,
        Configuration configuration)
    {
        this.framework = framework;
        this.chatGui = chatGui;
        this.pluginLog = pluginLog;
        this.gameGui = gameGui;
        this.objectTable = objectTable;
        this.condition = condition;
        this.configuration = configuration;
    }

    public bool EmergencyStopped => emergencyStopped;

    public IReadOnlyList<ExecutionRecord> HistorySnapshot()
    {
        lock (sync) return history.ToArray();
    }

    public string[] RunningRuleNames()
    {
        lock (sync)
        {
            return runningRuleTokens.Keys
                .Select(id => configuration.Rules.FirstOrDefault(r => r.Id == id)?.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .ToArray();
        }
    }

    public bool IsRuleRunning(Guid ruleId)
    {
        lock (sync) return runningRuleTokens.ContainsKey(ruleId);
    }

    public async Task RunAsync(RuleDefinition rule, LogEntry? source, bool forceDryRun, CancellationToken externalToken)
    {
        if (emergencyStopped)
        {
            AddHistory(rule, FlowRunStatus.Blocked, "全停止中のためブロックしました。", 0);
            return;
        }

        CancellationTokenSource ruleCts;
        lock (sync)
        {
            if (runningRuleTokens.ContainsKey(rule.Id) && rule.LockWhileRunning)
            {
                AddHistory(rule, FlowRunStatus.Blocked, "同じルールが実行中です。", 0);
                return;
            }
            ruleCts = new CancellationTokenSource();
            runningRuleTokens[rule.Id] = ruleCts;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(emergencyCts.Token, ruleCts.Token, externalToken);
        CancellationToken token = linked.Token;
        var stopwatch = Stopwatch.StartNew();
        bool dryRun = forceDryRun;

        AddHistory(rule, dryRun ? FlowRunStatus.DryRun : FlowRunStatus.Running,
            source == null ? "手動実行開始" : $"一致ログ: {source.CombinedText}", 0);

        try
        {
            foreach (FlowStep step in rule.FlowSteps.Where(x => x.Enabled))
            {
                token.ThrowIfCancellationRequested();
                switch (step.Type)
                {
                    case FlowStepType.Wait:
                        if (!dryRun)
                            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(step.NumberValue, 0, 600)), token);
                        break;

                    case FlowStepType.Command:
                        await ExecuteCommandStepAsync(rule, step.Text, dryRun, stopwatch.Elapsed.TotalMilliseconds);
                        break;

                    case FlowStepType.Lua:
                        await ExecuteLuaFileAsync(rule, step, dryRun, token, stopwatch.Elapsed.TotalMilliseconds);
                        break;
                }
            }

            AddHistory(rule, dryRun ? FlowRunStatus.DryRun : FlowRunStatus.Completed,
                dryRun ? "ドライラン完了" : "フロー完了", stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            AddHistory(rule, FlowRunStatus.Cancelled, "フローが停止されました。", stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            pluginLog.Error(ex, "Flow failed: {RuleName}", rule.Name);
            AddHistory(rule, FlowRunStatus.Failed, ex.Message, stopwatch.Elapsed.TotalMilliseconds);
        }
        finally
        {
            lock (sync)
            {
                if (runningRuleTokens.Remove(rule.Id, out var cts)) cts.Dispose();
            }
        }
    }

    private async Task ExecuteCommandStepAsync(RuleDefinition rule, string raw, bool dryRun, double elapsed)
    {
        string command = raw.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            AddHistory(rule, FlowRunStatus.Blocked, "Commandが空です。", elapsed);
            return;
        }
        if (!command.StartsWith('/'))
        {
            AddHistory(rule, FlowRunStatus.Blocked, "Commandは / から始めてください: " + command, elapsed);
            return;
        }
        if (!dryRun)
        {
            await framework.RunOnFrameworkThread(() => ChatCommandSender.Send(command));
        }

        AddHistory(
            rule,
            dryRun ? FlowRunStatus.DryRun : FlowRunStatus.Running,
            dryRun ? "Command予定: " + command : "Command送信: " + command,
            elapsed);
    }

    private static string NormalizeLuaPath(string? rawPath)
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

        return string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path);
    }

    private async Task ExecuteLuaFileAsync(
        RuleDefinition rule,
        FlowStep step,
        bool dryRun,
        CancellationToken token,
        double elapsed)
    {
        LuaScriptDefinition? entry = configuration.LuaScripts.FirstOrDefault(x => x.Id == step.LuaScriptId);
        if (entry == null)
        {
            AddHistory(rule, FlowRunStatus.Blocked, "Luaファイルが未選択、または登録から削除されています。", elapsed);
            return;
        }

        string path = NormalizeLuaPath(entry.FilePath);

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            AddHistory(rule, FlowRunStatus.Blocked, $"Luaファイルが見つかりません: {path}", elapsed);
            return;
        }

        if (dryRun)
        {
            AddHistory(rule, FlowRunStatus.DryRun, $"Lua予定: {entry.Name} | {path}", elapsed);
            return;
        }

        token.ThrowIfCancellationRequested();
        AddHistory(rule, FlowRunStatus.Running, $"Lua実行開始: {entry.Name} | {path}", elapsed);

        try
        {
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                string code = File.ReadAllText(path, System.Text.Encoding.UTF8);

                var script = new Script(CoreModules.Preset_Complete);
                script.Options.ScriptLoader = new MoonSharp.Interpreter.Loaders.FileSystemScriptLoader
                {
                    ModulePaths = new[]
                    {
                        Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, "?.lua")
                    }
                };

                RegisterLuaCompatibilityGlobals(script, token);
                script.DoString(code, codeFriendlyName: path);
            }, token);

            AddHistory(rule, FlowRunStatus.Running, $"Lua実行成功: {entry.Name} | {path}", elapsed);
        }
        catch (SyntaxErrorException ex)
        {
            string detail = ex.DecoratedMessage ?? ex.Message;
            AddHistory(rule, FlowRunStatus.Failed, $"Lua構文エラー: {detail}", elapsed);
            throw;
        }
        catch (ScriptRuntimeException ex)
        {
            string detail = ex.DecoratedMessage ?? ex.Message;
            AddHistory(rule, FlowRunStatus.Failed, $"Lua実行エラー: {detail}", elapsed);
            throw;
        }
    }


    private void RegisterLuaCompatibilityGlobals(Script script, CancellationToken token)
    {
        script.Globals["command"] = DynValue.NewCallback((_, args) =>
        {
            string value = args.Count > 0 ? args[0].CastToString()?.Trim() ?? string.Empty : string.Empty;
            ExecuteLuaYield(value, token);
            return DynValue.Nil;
        });

        script.Globals["yield"] = DynValue.NewCallback((_, args) =>
        {
            string value = args.Count > 0 ? args[0].CastToString()?.Trim() ?? string.Empty : string.Empty;
            ExecuteLuaYield(value, token);
            return DynValue.Nil;
        });

        script.Globals["print_ccs"] = DynValue.NewCallback((_, args) =>
        {
            string text = args.Count > 0 ? args[0].ToPrintString() : string.Empty;
            framework.RunOnFrameworkThread(() => chatGui.Print("[CCS Lua] " + text)).GetAwaiter().GetResult();
            return DynValue.Nil;
        });

        script.Globals["sleep"] = DynValue.NewCallback((_, args) =>
        {
            token.ThrowIfCancellationRequested();
            double milliseconds = args.Count > 0 ? args[0].Number : 0;
            int delay = (int)Math.Clamp(milliseconds, 0, 600000);
            if (delay > 0) Task.Delay(delay, token).GetAwaiter().GetResult();
            return DynValue.Nil;
        });

        script.Globals["is_cancelled"] = DynValue.NewCallback((_, _) => DynValue.NewBoolean(token.IsCancellationRequested));

        var dalamud = new Table(script);
        dalamud["Log"] = DynValue.NewCallback((_, args) =>
        {
            string text = args.Count > 0 ? args[0].ToPrintString() : string.Empty;
            pluginLog.Information("[CCS Lua] {Text}", text);
            return DynValue.Nil;
        });
        script.Globals["Dalamud"] = dalamud;

        // DalamudのClientState/ObjectTable/Conditionはフレームワークスレッド限定。
        // Lua本体はTask.Run上で動くため、ここで一度だけメインスレッドへ切り替えて
        // Luaへ渡すスナップショットを取得する。
        bool playerAvailable = false;
        var conditionStates = new bool[101];

        framework.RunOnFrameworkThread(() =>
        {
            playerAvailable = objectTable.LocalPlayer != null;

            for (int i = 0; i < conditionStates.Length; i++)
            {
                try { conditionStates[i] = condition[(ConditionFlag)i]; }
                catch { conditionStates[i] = false; }
            }
        }).GetAwaiter().GetResult();

        var player = new Table(script);
        player["Available"] = playerAvailable;
        script.Globals["Player"] = player;

        var conditionTable = new Table(script);
        for (int i = 0; i < conditionStates.Length; i++)
            conditionTable[i] = conditionStates[i];
        var svc = new Table(script);
        svc["Condition"] = conditionTable;
        script.Globals["Svc"] = svc;

        var addons = new Table(script);
        addons["GetAddon"] = DynValue.NewCallback((_, args) =>
        {
            string name = args.Count > 0 ? args[0].CastToString() ?? string.Empty : string.Empty;
            return GetLuaAddonTable(script, name);
        });
        script.Globals["Addons"] = addons;

        DynValue osValue = script.Globals.Get("os");
        if (osValue.Type == DataType.Table)
        {
            long origin = Stopwatch.GetTimestamp();
            osValue.Table["clock"] = DynValue.NewCallback((_, _) =>
                DynValue.NewNumber((Stopwatch.GetTimestamp() - origin) / (double)Stopwatch.Frequency));
        }
    }

    private DynValue GetLuaAddonTable(Script script, string addonName)
    {
        bool exists = false;
        bool ready = false;
        if (!string.IsNullOrWhiteSpace(addonName))
        {
            framework.RunOnFrameworkThread(() =>
            {
                nint address = gameGui.GetAddonByName(addonName, 1);
                exists = address != nint.Zero;
                if (exists)
                {
                    unsafe
                    {
                        var addon = (AtkUnitBase*)address;
                        ready = addon != null && addon->IsVisible;
                    }
                }
            }).GetAwaiter().GetResult();
        }

        var table = new Table(script);
        table["Exists"] = exists;
        table["Ready"] = ready;
        table["Name"] = addonName;
        return DynValue.NewTable(table);
    }

    private void ExecuteLuaYield(string text, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(text)) return;

        if (text.StartsWith("/wait", StringComparison.OrdinalIgnoreCase))
        {
            string raw = text[5..].Trim();
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
            {
                int delay = (int)Math.Clamp(seconds * 1000.0, 0, 600000);
                if (delay > 0) Task.Delay(delay, token).GetAwaiter().GetResult();
            }
            return;
        }

        if (text.StartsWith("/callback", StringComparison.OrdinalIgnoreCase))
        {
            LuaCallbackRequest request = ParseCallbackCommand(text);
            framework.RunOnFrameworkThread(() => FireAddonCallback(request)).GetAwaiter().GetResult();
            return;
        }

        if (!text.StartsWith('/'))
            throw new ScriptRuntimeException("yield() requires a slash command.");

        framework.RunOnFrameworkThread(() => ChatCommandSender.Send(text)).GetAwaiter().GetResult();
    }

    private static LuaCallbackRequest ParseCallbackCommand(string command)
    {
        List<string> tokens = TokenizeCommand(command);
        if (tokens.Count < 3)
            throw new ScriptRuntimeException("/callback requires: /callback AddonName updateState [values...]");

        bool updateState = bool.TryParse(tokens[2], out bool parsed) && parsed;
        var values = new List<object>();
        for (int i = 3; i < tokens.Count; i++)
        {
            string token = tokens[i];
            if (bool.TryParse(token, out bool boolValue)) values.Add(boolValue);
            else if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue)) values.Add(intValue);
            else if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double numberValue)) values.Add(numberValue);
            else values.Add(token);
        }
        return new LuaCallbackRequest(tokens[1], updateState, values.ToArray());
    }

    private static List<string> TokenizeCommand(string command)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        bool escaping = false;
        foreach (char c in command)
        {
            if (escaping)
            {
                current.Append(c);
                escaping = false;
                continue;
            }
            if (c == '\\' && quoted)
            {
                escaping = true;
                continue;
            }
            if (c == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }

    private unsafe void FireAddonCallback(LuaCallbackRequest request)
    {
        nint address = gameGui.GetAddonByName(request.AddonName, 1);
        if (address == nint.Zero)
            throw new ScriptRuntimeException($"Addon is not available: {request.AddonName}");

        var addon = (AtkUnitBase*)address;
        int count = request.Values.Length;
        Span<AtkValue> values = count == 0 ? Span<AtkValue>.Empty : stackalloc AtkValue[count];
        values.Clear();
        var allocatedStrings = new List<nint>();

        try
        {
            for (int i = 0; i < count; i++)
            {
                object value = request.Values[i];
                switch (value)
                {
                    case bool boolean:
                        values[i].Type = AtkValueType.Bool;
                        values[i].Byte = boolean ? (byte)1 : (byte)0;
                        break;
                    case int integer:
                        values[i].Type = AtkValueType.Int;
                        values[i].Int = integer;
                        break;
                    case double number:
                        values[i].Type = AtkValueType.Float;
                        values[i].Float = (float)number;
                        break;
                    case string str:
                        nint ptr = System.Runtime.InteropServices.Marshal.StringToCoTaskMemUTF8(str);
                        allocatedStrings.Add(ptr);
                        values[i].Type = AtkValueType.String;
                        values[i].String = (byte*)ptr;
                        break;
                    default:
                        throw new ScriptRuntimeException($"Unsupported callback value: {value}");
                }
            }

            fixed (AtkValue* pointer = values)
                addon->FireCallback((uint)count, pointer, request.UpdateState);
        }
        finally
        {
            foreach (nint ptr in allocatedStrings)
                System.Runtime.InteropServices.Marshal.FreeCoTaskMem(ptr);
        }
    }

    private sealed record LuaCallbackRequest(string AddonName, bool UpdateState, object[] Values);

    public bool StopRule(Guid ruleId)
    {
        lock (sync)
        {
            if (!runningRuleTokens.TryGetValue(ruleId, out var cts)) return false;
            cts.Cancel();
            return true;
        }
    }

    public void EmergencyStop()
    {
        emergencyStopped = true;
        emergencyCts.Cancel();
        emergencyCts.Dispose();
        emergencyCts = new CancellationTokenSource();
        lock (sync)
        {
            foreach (var cts in runningRuleTokens.Values) cts.Cancel();
        }
    }

    public void ResumeAfterEmergencyStop() => emergencyStopped = false;

    private void AddHistory(RuleDefinition rule, FlowRunStatus status, string detail, double elapsedMilliseconds)
    {
        lock (sync)
        {
            history.Insert(0, new ExecutionRecord(DateTime.Now, rule.Id, rule.Name, rule.Category, status, detail, elapsedMilliseconds));
            if (history.Count > 500) history.RemoveRange(500, history.Count - 500);
        }
    }

    public void Dispose()
    {
        EmergencyStop();
        emergencyCts.Dispose();
        lock (sync)
        {
            foreach (var cts in runningRuleTokens.Values) cts.Dispose();
            runningRuleTokens.Clear();
        }
    }
}
