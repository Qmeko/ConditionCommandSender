# ConditionCommandSender

[日本語](README.ja.md)

ConditionCommandSender is a Dalamud plugin that runs rules against collected game logs. Each enabled rule can wait, send a command, or run a Lua file.

Rules are organized with tags. The default UI language follows Dalamud, and you can switch English / Japanese in Settings.

## Install

1. Run `/xlsettings` and open the **Experimental** tab
2. Add this URL under **Custom Plugin Repositories**:

```
https://raw.githubusercontent.com/Qmeko/DalamudPlugins/refs/heads/main/pluginmaster.json
```

3. Run `/xlplugins` and install **ConditionCommandSender**

## Features

- Unified log capture with Battle / ALL views
- Tag-organized rules
- Conditions based on log text (message, or sender + message)
- Flow steps: Wait, Command, Lua
- Dry run mode
- Execution history
- i18n — English and Japanese UI strings (switchable in Settings)

## Commands

| Command | Description |
| --- | --- |
| `/ccs` | Toggle the window |
| `/ccs help` | Show commands |
| `/ccs on <rule>` | Enable a rule |
| `/ccs off <rule>` | Disable a rule |
| `/ccs toggle <rule>` | Toggle a rule |
| `/ccs allon` | Enable all rules |
| `/ccs alloff` | Disable all rules |
| `/ccs alltoggle` | Toggle all rules |
| `/ccs log start` | Resume log capture |
| `/ccs log stop` | Stop log capture |

## For developers

1. Run `Build.bat`, or `dotnet build ConditionCommandSender.sln -c Release -p:Platform=x64`
2. Point Dalamud’s **dev plugin** path at `artifacts/ConditionCommandSender/ConditionCommandSender.dll`
3. Enable **ConditionCommandSender** in the plugin installer (dev)

The release zip must include `ConditionCommandSender.dll`, `ConditionCommandSender.json`, `MoonSharp.Interpreter.dll`, and `Data/I18n/*.json`.
