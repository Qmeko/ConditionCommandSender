# ConditionCommandSender

[English](README.md)

ConditionCommandSender は、取得したゲームログに対してルールを実行する Dalamud プラグインです。有効なルールは Wait、コマンド送信、Lua ファイル実行ができます。

ルールはタグで整理します。UI言語は既定で Dalamud に合わせ、設定から英語／日本語を切り替えられます。

## インストール

1. `/xlsettings` を実行し、**試験的機能**タブを開く
2. **カスタムプラグインリポジトリ** に次の URL を追加する:

```
https://raw.githubusercontent.com/Qmeko/DalamudPlugins/refs/heads/main/pluginmaster.json
```

3. `/xlplugins` を実行し、**ConditionCommandSender** をインストールする

## 機能

- 統合ログ取得（Battle / ALL）
- タグで整理するルール
- ログ本文、または送信者込み本文による条件
- フロー: Wait / Command / Lua
- ドライラン
- 実行履歴
- i18n — UI 文字列の英語／日本語（設定で切替可）

## コマンド

| コマンド | 説明 |
| --- | --- |
| `/ccs` | 画面の表示切替 |
| `/ccs help` | コマンド一覧 |
| `/ccs on <ルール名>` | ルールを有効化 |
| `/ccs off <ルール名>` | ルールを無効化 |
| `/ccs toggle <ルール名>` | ルールの有効/無効を切替 |
| `/ccs allon` | 全ルールを有効化 |
| `/ccs alloff` | 全ルールを無効化 |
| `/ccs alltoggle` | 全ルールを一括切替 |
| `/ccs log start` | ログ取得を再開 |
| `/ccs log stop` | ログ取得を停止 |

## 開発者向け

1. `Build.bat` を実行するか、`dotnet build ConditionCommandSender.sln -c Release -p:Platform=x64`
2. Dalamud の **dev plugin** パスを `artifacts/ConditionCommandSender/ConditionCommandSender.dll` に向ける
3. プラグインインストーラ（dev）で **ConditionCommandSender** を有効化

配布ZIPには `ConditionCommandSender.dll`、`ConditionCommandSender.json`、`MoonSharp.Interpreter.dll`、`Data/I18n/*.json` を含めてください。
