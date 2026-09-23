# Voice Review Bridge

## 1. Goal

YMM4のVoiceItem一覧を外へ出し、LLMで文脈レビューし、構造化された修正候補をYMM4へ戻すための疎結合なBridgeです。

LLMをYMM4内部へ常駐させないことを初期設計の前提にします。

## 2. Intended workflow

```text
YMM4
  ↓ Export
Voice Review Package
  ↓
LLM / Codex / manual review
  ↓
Correction Proposal
  ↓ Import
YMM4
  ↓
Validate / Diff / Select
  ↓
Apply + regenerate
```

## 3. Why Export / Import first

- LLM障害やAPI変更が編集本体へ影響しにくい
- ChatGPT / Codex / ローカルLLM等を交換可能
- 大量Voiceをまとめてレビューしやすい
- 修正前後をファイルとして監査できる
- 自動適用前に人間が確認できる
- Local Assist単体利用を妨げない

## 4. Export package — draft

canonical形式はJSONを第一候補にします。

```json
{
  "schema": "ymm4.voice-review.v0",
  "exportSessionId": "session-...",
  "voices": [
    {
      "target": {
        "exportRef": "voice-000123",
        "exportIndex": 123,
        "frame": 420,
        "layer": 3
      },
      "sourceFingerprint": "sha256:...",
      "characterName": "小夜",
      "serif": "このあと<w0>ゲートを抜けます",
      "hatsuon": "このあとげーとをぬけます"
    }
  ]
}
```

## 5. Stable target identity

Lab結果から、YMM4 4.56.1.0のVoiceItemにはPlugin APIから読める安定Guid surfaceがありません。
そのためv0は二段構えにします。

### Same session

`exportSessionId + exportRef` をPlugin内部のVoiceItem object mapへ解決します。

### Cross session

`sourceFingerprint`を第一条件にし、frame/layer/character/contextは候補絞り込み用locatorとして使います。

一意に解決できない場合は `AMBIGUOUS` とし、自動適用しません。

詳細は [SCHEMA_V0.md](SCHEMA_V0.md)。

## 6. Correction Proposal — draft

LLMは完成プロジェクトではなく構造化された修正命令を返します。

operation候補:

- reading override
- add/remove boundary
- helper vowel zero
- helper consonant zero
- optional prosody gesture
- no-change

## 7. Import safety

- schema version検証
- exportSessionId整合
- target再解決
- fingerprint一致確認
- stale/ambiguous proposalを自動適用しない
- before/after diff表示
- 選択したCorrectionだけ適用
- 不明operationをエラー化
- free-form codeを実行しない
- 可能なら一つのUndo単位へまとめる

## 8. Human-readable formats

JSONをcanonicalとしつつ、レビュー用途としてCSV/XLSXを追加できます。

XLSXは1行1Voiceで、元Serif・Hatsuon・前後文・LLM提案・採用/却下を見やすくする派生ビューにします。

## 9. LLM responsibilities

LLMへ任せやすい:

- 文脈依存読み
- 固有名詞候補
- 句境界候補
- helper mora候補
- rough prosody direction

Local deterministic engineへ残す:

- `<w0>`解析
- target mora解決
- duration=0適用
- schema validation
- stale/ambiguous detection
- actual YMM4 mutation

LLMの役割は判断・提案、Pluginの役割は検証・適用と分離します。