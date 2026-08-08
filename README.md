# CocoroConsole

CocoroConsole は AI人格システム CocoroAI Ver.5 の Windows クライアントです。

https://github.com/hirona98/OtomeKairo のチャット表示、CocoroShell 連携、端末固有設定用 UI として使用します。

ソリューションファイルの UUID はコピー元から継承しています

CocoroAI
https://alice-encoder.booth.pm/items/6821221


----

## 開発環境

- Windows
- C# .NET8
- WPF

## 設定の役割分担

- OtomeKairo WebUI: 人格、モデル、記憶、呼ばれ方、定期思考、デスクトップ取得方針、カメラ、Watcher、MCP、API説明など本体側の設定
- CocoroConsole 設定画面: 表示、アバター（プリセットと VRM）、モーション、マイク（入力元と Console デバイス）、ライセンス
- CocoroConsole メイン画面: STT / TTS / デスクトップウォッチの運用トグル
- 接続先設定（トレイ）: `server_url` / `client_id` / `console_access_token` の bootstrap
- 音声合成と音声起動ワードは OtomeKairo WebUI で編集する

## 会話入力の呼び名

テキスト入力は OtomeKairo の選択中の呼び名を `participants[].display_name` として送信する。
音声入力、音声認識、話者識別は OtomeKairo で実行する。
話者登録は OtomeKairo WebUI から行い、CocoroConsole はマイク入力元と Console 側デバイスだけを設定する。
人物設定用のDBは作成しない。
CocoroConsole は OtomeKairo API `0.7.0` へ接続する。
音声合成は OtomeKairo で実行する。
CocoroConsole は event stream で受信した WAV を CocoroShell へ配送し、CocoroShell が起動していない場合は自身で再生する。
