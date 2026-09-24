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

## 4. Export package — B1 FROZEN

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


B1製品実装では、現在TimelineのVoiceItemをFrame → Layer → original input orderで安定ソートし、`voice-000000`形式のexportRefを付与します。packageにはB0 fingerprint、speaker API/ID、前後Serif context、official control解析結果、enabled Assist helper/prosody、optional generated pronunciation summaryを含めます。

同じYMM4セッション中は、export packageとは別にPlugin内部で `exportRef -> live VoiceItem` mapを保持します。

## 5. Stable target identity

Lab結果から、YMM4 4.56.1.0のVoiceItemにはPlugin APIから読める安定Guid surfaceがありません。
そのためv0は二段構えにします。

### Same session

`exportSessionId + exportRef` をPlugin内部のVoiceItem object mapへ解決します。

### Cross session

`sourceFingerprint`を第一条件にします。exact fingerprintが**1件だけ**ならauto-apply candidateです。複数exact matchはlocatorで勝手に絞らず `AMBIGUOUS` とします。

exact matchが0件の場合だけframe/layer/character/previous/next contextをlocatorとして使います。locatorが1件でもfingerprintが違えば `STALE` であり、自動適用しません。

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

B0 coreでfreeze済み:

- schema version検証
- exportSessionId整合
- exportRef存在
- target再解決state
- fingerprint format / 一致確認
- stale/ambiguous proposalを自動適用しない
- unknown operationをエラー化
- operation payload / clean-text position検証
- conflicting/duplicate correctionの拒否
- `noChange`混在拒否

B3で実装するUI/apply境界:

- before/after diff表示
- 選択したCorrectionだけ適用
- free-form codeを実行しない
- 可能なら一つのUndo単位へまとめる

## 8. Human-readable formats — B1

JSONがcanonicalです。

B1では閲覧用CSVを実装済みです。1行1Voiceで、target、character/speaker、前後Serif、Serif/Hatsuon、cleanText/boundary、prosody/helper、generated pronunciation summary、source fingerprintを固定列順で出力します。

ToolからJSON/CSVのSaveFileDialogを開けます。JSONはUTF-8、CSVは表計算ソフトで扱いやすいUTF-8 BOM付きで保存します。

CSVはImport正本ではありません。XLSXは必要なら将来追加できますがB1完了条件ではありません。

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