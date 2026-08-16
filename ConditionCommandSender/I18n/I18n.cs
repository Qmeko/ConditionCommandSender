using System.Text.Json;
using Dalamud.Plugin;

namespace ConditionCommandSender;

/// <summary>
/// UI strings from Data/I18n/{lang}.json (fallback: en).
/// Language follows config, or Dalamud UI language when set to "client".
/// </summary>
internal static class I18n
{
    public const string FollowClient = "client";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static IDalamudPluginInterface? pluginInterface;
    private static Func<string?>? getUiLanguage;
    private static Dictionary<string, string> strings = new(StringComparer.Ordinal);
    private static string lang = "en";

    public static event Action? Reloaded;

    public static string CurrentLang => lang;

    public static void Init(
        IDalamudPluginInterface interfaceInstance,
        Func<string?> getConfiguredLanguage)
    {
        ArgumentNullException.ThrowIfNull(interfaceInstance);
        pluginInterface = interfaceInstance;
        getUiLanguage = getConfiguredLanguage;
        interfaceInstance.LanguageChanged += OnClientLanguageChanged;
        ApplyFromConfig();
    }

    public static void Dispose()
    {
        if (pluginInterface != null)
            pluginInterface.LanguageChanged -= OnClientLanguageChanged;

        pluginInterface = null;
        getUiLanguage = null;
        Reloaded = null;
        strings = new(StringComparer.Ordinal);
    }

    public static void ApplyFromConfig()
    {
        Load(ResolveConfiguredLang());
        Reloaded?.Invoke();
    }

    public static string Get(string key)
    {
        if (string.IsNullOrEmpty(key))
            return string.Empty;

        if (strings.TryGetValue(key, out string? value) && value != null)
            return value;

        return key;
    }

    public static string Format(string key, params object?[] args)
    {
        string template = Get(key);
        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    private static void OnClientLanguageChanged(string langCode)
    {
        if (!IsFollowClient())
            return;

        Load(NormalizeLangCode(langCode));
        Reloaded?.Invoke();
    }

    private static bool IsFollowClient()
    {
        string? mode = getUiLanguage?.Invoke();
        return string.IsNullOrWhiteSpace(mode)
            || string.Equals(mode, FollowClient, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveConfiguredLang()
    {
        string? mode = getUiLanguage?.Invoke()?.Trim();
        if (!string.IsNullOrEmpty(mode)
            && !string.Equals(mode, FollowClient, StringComparison.OrdinalIgnoreCase))
            return NormalizeLangCode(mode);

        return NormalizeLangCode(pluginInterface?.UiLanguage);
    }

    private static void Load(string? langCode)
    {
        lang = NormalizeLangCode(langCode);
        Dictionary<string, string> map = ReadLangFile(lang);
        if (map.Count == 0 && !string.Equals(lang, "en", StringComparison.Ordinal))
            map = ReadLangFile("en");

        strings = map;
    }

    private static string NormalizeLangCode(string? langCode)
    {
        string value = string.IsNullOrWhiteSpace(langCode)
            ? "en"
            : langCode.Trim().ToLowerInvariant();
        return value.Length > 2 ? value[..2] : value;
    }

    private static Dictionary<string, string> ReadLangFile(string language)
    {
        try
        {
            string dir = pluginInterface?.AssemblyLocation.DirectoryName
                ?? AppContext.BaseDirectory;
            string path = Path.Combine(dir, "Data", "I18n", $"{language}.json");
            if (!File.Exists(path))
                return new Dictionary<string, string>(StringComparer.Ordinal);

            string json = File.ReadAllText(path);
            Dictionary<string, string>? parsed =
                JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
            return parsed == null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(parsed, StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }
}
