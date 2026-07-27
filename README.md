# CocoroConsole

CocoroConsole は AI人格システム CocoroAI Ver.5 のチャットおよび設定用UIです

https://github.com/hirona98/OtomeKairo のWindowsクライアントとして使用可能です

ソリューションファイルの UUID はコピー元から継承しています

CocoroAI
https://alice-encoder.booth.pm/items/6821221


----

## 開発環境

- Windows
- C# .NET8
- WPF

## 会話入力の呼び名

テキスト入力は会話入力設定の呼び名を `participants[].display_name` として送信する。
音声入力で話者を識別した場合は、`SpeakerRecognition.db` の話者名を `participants[].display_name` として送信する。
話者登録画面では、OtomeKairoから呼ばれる形で話者名を登録する。
人物設定用のDBは作成しない。
CocoroConsole は OtomeKairo API `0.2.0` へ接続する。
