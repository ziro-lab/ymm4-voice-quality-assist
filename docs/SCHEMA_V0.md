# Review Bridge schema v0

Status: **DRAFT / CANDIDATE**

この文書はVoice Review Bridgeの最初の交換形式を具体化します。

## 1. Design goals

- 1 VoiceItem = 1 review record
- LLMはYMM4プロジェクト全体を書き換えない
- Proposalは構造化operationだけを返す
- Import時に元データ変更を検知できる
- タイムライン移動だけで別Item扱いしない
- 対象が一意に解決できない場合は自動適用しない
- unknown operationは黙って無視しない

## 2. Identity model

Labで確認した結果、YMM4 4.56.1.0の`VoiceItem`にはPlugin APIから読める安定Guid surfaceがありません。
そのためv0では`itemGuid`を主キーにしません。

### Same-session identity

ExportしたYMM4セッションが生きている間は、Plugin内部で

```text
exportSessionId + exportRef -> actual VoiceItem object
```

の対応を保持します。

package側:

```json
{
  "exportSessionId": "session-...",
  "target": {
    "exportRef": "voice-000012",
    "exportIndex": 12,
    "frame": 420,
    "layer": 3
  }
}
```

### Cross-session re-resolution

YMM4を閉じた後にImportする場合は、`exportRef`だけでは足りません。
次を使って再解決します。

1. `sourceFingerprint` 完全一致
2. frame/layer/character/contextをlocatorとして候補絞り込み
3. 一意なら適用候補
4. 0件なら`MISSING`
5. 複数件なら`AMBIGUOUS`

`AMBIGUOUS`は自動適用しません。

## 3. Source fingerprint

`sourceFingerprint` はstale proposal検知とcross-session再解決の中核です。

v0 candidate input:

```text
characterName
serif
hatsuon
assist-relevant source controls
```

をcanonical JSONへ正規化し、SHA-256を計算する案です。

原則含めない:

- frame
- layer
- selection state
- UI-only state
- generated cache path

理由は、VoiceItemを移動しただけでレビュー内容までstaleにしたくないためです。

## 4. Export package

schema: `ymm4.voice-review.v0`

top-level required:

- schema
- exportSessionId
- exportedAt
- voices

Voice record required:

- target
- sourceFingerprint
- characterName
- serif
- hatsuon
- context
- controls

### Context

LLMが文脈読みを判断できるよう、最低限前後Voiceの文章を持たせます。

### Derived official control information

Local parserで取れる情報はLLMに再解析させません。

```json
"controls": {
  "cleanText": "このあとゲートを抜けます",
  "boundaries": [
    { "position": 4, "source": "w0" }
  ]
}
```

## 5. Correction proposal

schema: `ymm4.voice-corrections.v0`

Proposalは元packageの`exportSessionId`を返します。

v0 operation候補:

- `setReading`
- `addBoundary`
- `removeBoundary`
- `helperVowelZero`
- `helperConsonantZero`
- `setProsodyGesture`
- `noChange`

### addBoundary

位置はcontrol-tag除去後のclean text上のUTF-16 indexをv0候補とします。
Unicode境界問題は実装前に日本語・絵文字・サロゲートペアで確認します。

## 6. Import resolution states

- `EXACT_SESSION_MATCH`
- `EXACT_FINGERPRINT_MATCH`
- `STALE`
- `MISSING`
- `AMBIGUOUS`

`STALE / MISSING / AMBIGUOUS` は自動適用しません。

## 7. Import validation

最低限:

1. schema/version一致
2. exportSessionId整合
3. target解決
4. sourceFingerprint一致
5. operation type既知
6. operation payload妥当
7. cleanTextPosition範囲内
8. before/after diff生成

## 8. JSON / XLSX split

canonicalはJSON。XLSX / CSVはhuman review用の派生ビューにします。
XLSXだけではnested operationや将来versioningを保持しづらいため、canonicalにはしません。