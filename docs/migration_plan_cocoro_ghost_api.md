# CocoroConsole → cocoro_ghost API 移行計画書

## 概要

CocoroConsole の設定ファイル（Setting.json）で管理している項目のうち、cocoro_ghost API で管理可能なものを API 経由に移行する。

## 移行対象項目

### 1. LLMプリセット関連

| CocoroConsole 現行項目 | cocoro_ghost API 項目 | 備考 |
|----------------------|---------------------|------|
| （新規追加） | `name` | プリセット名 |
| `CharacterSettings.apiKey` | `llm_api_key` | |
| `CharacterSettings.llmModel` | `llm_model` | |
| `CharacterSettings.localLLMBaseUrl` | `llm_base_url` | |
| `CharacterSettings.reasoning_effort` | `reasoning_effort` | |
| `CharacterSettings.max_turns_window` | `max_turns_window` | |
| `CharacterSettings.max_tokens` | `max_tokens` | |
| `CharacterSettings.max_tokens_vision` | `max_tokens_vision` | |
| `CharacterSettings.visionModel` | `image_model` | |
| `CharacterSettings.visionApiKey` | `image_model_api_key` | |
| （新規追加） | `image_timeout_seconds` | 画像生成タイムアウト秒 |
| （新規追加） | `image_llm_base_url` | 画像系ベースURL |
| `CharacterSettings.embeddedModel` | `embedding_model` | |
| `CharacterSettings.embeddedApiKey` | `embedding_api_key` | |
| `CharacterSettings.embeddedBaseUrl` | `embedding_base_url` | |
| `CharacterSettings.embeddedDimension` | `embedding_dimension` | |
| （新規追加） | `similar_episodes_limit` | 既存設定に追加 |

### 2. キャラクタープリセット関連

| CocoroConsole 現行項目 | cocoro_ghost API 項目 | 備考 |
|----------------------|---------------------|------|
| （新規追加） | `name` | プリセット名 |
| `CharacterSettings.systemPromptFilePath` の内容 | `system_prompt` | ファイル管理からAPI管理へ |
| `CharacterSettings.memoryId` | `memory_id` | |

### 3. 共通設定

| CocoroConsole 現行項目 | cocoro_ghost API 項目 | 備考 |
|----------------------|---------------------|------|
| `ScreenshotSettings.excludePatterns` | `exclude_keywords` | |
| （API取得のみ） | `active_llm_preset_id` | `/settings` のレスポンスを保持 |
| （API取得のみ） | `active_character_preset_id` | `/settings` のレスポンスを保持 |

## CocoroConsole ローカルに残す項目

- ポート設定（cocoroCorePort, cocoroShellPort 等）
- UI系設定（ウィンドウサイズ/位置、最前面表示、カーソル回避等）
- TTS設定（voicevox, style-bert-vits2, aivis-cloud）
- STT設定（amivoice, openai）
- VRM関連設定（vrmFilePath, isConvertMToon 等）
- アニメーション設定
- グラフィックス設定（MSAA, シャドウ等）
- スクリーンショット設定（enabled, intervalMinutes 等、exclude_keywords 以外）
- メッセージウィンドウ設定
- マイク設定
- 定期コマンド設定

## 実装タスク

### Phase 1: API クライアント実装

- [x] 1.1 cocoro_ghost API クライアントクラス作成
  - `Services/CocoroGhostApiClient.cs`
  - 認証ヘッダー（Bearer Token）対応
  - エラーハンドリング

- [x] 1.2 API モデルクラス作成
  - `Models/CocoroGhostApi/` ディレクトリ作成
  - LLMPreset モデル
  - CharacterPreset モデル
  - Settings モデル（exclude_keywords）

### Phase 2: 設定画面の改修

- [ ] 2.1 LLMプリセット管理UI作成
  - プリセット一覧表示
  - プリセット選択・切替
  - プリセット作成・編集・削除
  - 切替時の再起動通知

- [ ] 2.2 キャラクタープリセット管理UI作成
  - プリセット一覧表示
  - プリセット選択・切替
  - プリセット作成・編集・削除
  - システムプロンプト編集
  - 切替時の再起動通知

- [ ] 2.3 共通設定UI改修
  - exclude_keywords のAPI経由管理

### Phase 3: CharacterSettings の分離

- [ ] 3.1 CharacterSettings からLLM関連項目を削除
  - apiKey
  - llmModel
  - localLLMBaseUrl
  - reasoning_effort
  - max_turns_window
  - max_tokens
  - max_tokens_vision
  - visionModel
  - visionApiKey
  - embeddedModel
  - embeddedApiKey
  - embeddedBaseUrl
  - embeddedDimension
  - systemPromptFilePath
  - memoryId

- [ ] 3.2 CharacterSettings に参照IDを追加（オプション）
  - llmPresetId（参照用、実際の設定はAPI側）
  - characterPresetId（参照用、実際の設定はAPI側）

### Phase 4: 既存コードの改修

- [ ] 4.1 設定保存処理の改修
  - LLM関連設定の保存先をAPIに変更
  - ローカル設定とAPI設定の分離

- [ ] 4.2 設定読込処理の改修
  - 起動時にAPIから設定を取得

- [ ] 4.3 チャット処理の改修
  - LLM設定参照先の変更確認

## API エンドポイント一覧（使用するもの）

### LLMプリセット

| メソッド | パス | 用途 |
|---------|------|------|
| GET | `/llm-presets` | 一覧取得 |
| POST | `/llm-presets` | 新規作成 |
| GET | `/llm-presets/{id}` | 詳細取得 |
| PATCH | `/llm-presets/{id}` | 更新 |
| DELETE | `/llm-presets/{id}` | 削除 |
| POST | `/llm-presets/{id}/activate` | 切替 |

### キャラクタープリセット

| メソッド | パス | 用途 |
|---------|------|------|
| GET | `/character-presets` | 一覧取得 |
| POST | `/character-presets` | 新規作成 |
| GET | `/character-presets/{id}` | 詳細取得 |
| PATCH | `/character-presets/{id}` | 更新 |
| DELETE | `/character-presets/{id}` | 削除 |
| POST | `/character-presets/{id}/activate` | 切替 |

### 共通設定

| メソッド | パス | 用途 |
|---------|------|------|
| GET | `/settings` | 全設定取得 |
| PATCH | `/settings` | exclude_keywords 更新 |

## 注意事項

1. **プリセット切替時の再起動**
   - LLM/キャラクタープリセット切替後は cocoro_ghost の再起動が必要
   - UIで再起動が必要な旨を通知する

2. **認証**
   - すべてのAPIリクエストに `Authorization: Bearer <TOKEN>` ヘッダーが必要
   - トークンは設定ファイルまたは環境変数で管理

3. **エラーハンドリング**
   - API接続エラー時の適切なエラー表示（フォールバックなし）
   - cocoro_ghost 未起動時の対応

## スケジュール

Phase 1 → Phase 2 → Phase 3 → Phase 4 の順で実装
