# Build notes

この環境にはDalamudのローカル開発用DLLがないため、
ここではソース構成とAPI 15向けプロジェクトを生成していますが、
実ゲーム環境でのコンパイル検証は未実施です。

API変更でエラーが出た場合に最初に確認する箇所:

- `IChatGui.ChatMessage`
- `IHandleableChatMessage`
- `ConditionFlag.InCombat`
- `IDalamudPluginInterface.UiBuilder`
- `Dalamud.Bindings.ImGui`
- `Dalamud.NET.Sdk`のバージョン

修理・マテリア精製・任意のゲームコマンド送信は、
ゲーム内部操作を伴うため、今回のv0.1.0では意図的に未接続です。
