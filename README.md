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
音声入力、音声認識、話者識別は OtomeKairo で実行する。
話者登録画面は OtomeKairo API を通じて音声人物を管理する。
人物設定用のDBは作成しない。
CocoroConsole は OtomeKairo API `0.6.0` へ接続する。
音声合成は OtomeKairo で実行する。
CocoroConsole は event stream で受信した WAV を CocoroShell へ配送し、CocoroShell が起動していない場合は自身で再生する。
