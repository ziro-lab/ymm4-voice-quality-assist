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
LLM / Codex / manual tool
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

利点:

- LLM障害やAPI変更が編集本体へ影響しにくい
- ChatGPT / Codex / ローカルLLM等を交換可能
- 大量Voiceをまとめてレビューしやすい
- 修正前後をファイルとして監査できる
- 自動適用前に人間が確認できる
- Local Assist単体利用を妨げない

## 4. Export package — draft

canonical形式はJSONを第一候補にします。

例:

```json
{
  "schema": "ymm4.voice-review.v0",
  "project": {
    "name": "example"
  },
  "voices": [
    {
      "ref": "voice-000123",
      "sourceFingerprint": "...",
      "character": "小夜",
      "serif": "このあと<w0>ゲートを抜けます",
      "hatsuon": "このあとげーとをぬけます",
      "previousText": "...",
      "nextText": "...",
      "assist": {
        "enabled": true
      }
    }
  ]
}
```

これはschema案であり、まだfreezeしていません。

### Candidate fields

最低限:

- item reference
- source fingerprint
- character
- speaker / style when useful
- Serif
- Hatsuon
- previous / next context

追加候補:

- official control tags
- clean text
- current boundary positions
- AudioQuery summary
- helper mora controls
- current assist settings

## 5. Stable target identity

Import時に「違うVoiceItemへ誤適用」が起きないことが重要です。

優先案:

1. YMM4側に安定したItem identifierがあれば使用
2. 無ければexport session ID + item fingerprint
3. Serif/Hatsuon/位置情報等をfingerprintへ含め、変更済みならstale扱い

この部分は実ホストLabで確認してからschemaをfreezeします。

## 6. Correction Proposal — draft

LLMは完成プロジェクトを返すのではなく、**構造化された修正命令**を返します。

概念例:

```json
{
  "schema": "ymm4.voice-corrections.v0",
  "corrections": [
    {
      "ref": "voice-000123",
      "expectedSourceFingerprint": "...",
      "operations": [
        {
          "type": "addBoundary",
          "cleanTextPosition": 4
        }
      ],
      "review": {
        "reason": "phrase boundary candidate"
      }
    }
  ]
}
```

operation候補:

- reading override
- add/remove boundary
- helper vowel zero
- helper consonant zero
- optional prosody gesture
- no-change

## 7. Import safety

Import時は最低限次を守ります。

- schema version検証
- target存在確認
- fingerprint一致確認
- stale proposalを自動適用しない
- before/after diff表示
- 選択したCorrectionだけ適用
- 不明operationを無視せずエラー化
- free-form codeを実行しない
- 可能なら一つのUndo単位へまとめる

## 8. Human-readable formats

JSONをcanonicalとしつつ、レビュー用途としてCSV/XLSXを追加できます。

特にXLSXは、

- 1行1Voice
- 元Serif
- 元Hatsuon
- 前後文
- LLM提案読み
- 境界候補
- 採用/却下

のような人間確認UIとして有力です。

ただしXLSXを唯一の交換形式にはせず、nested pronunciation dataやversioningはJSON側へ残す想定です。

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
- stale detection
- actual YMM4 mutation

LLMの役割は**判断・提案**、Pluginの役割は**検証・適用**と分離します。
